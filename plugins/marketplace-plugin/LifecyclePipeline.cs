using FabioSoft.Nucleus.Contracts;
using FabioSoft.Nucleus.Kernel;
using FabioSoft.Marketplace;
using FabioSoft.Marketplace.Io;
using FabioSoft.Contracts.Session;

namespace FabioSoft.Nucleus.Plugins.MarketplacePlugin;

/// Runs the lifecycle pipeline for one changed item: recompile -> unit tests -> integration tests ->
/// update PLUGIN.md -> reload (or flag restart for a module) -> commit -> push (best-effort).
/// Each step gates the next; a failure stops the run and surfaces MarketplaceFailed, leaving the running
/// plugin untouched. Pure decisions come from Lifecycle (Core); file steps from LifecycleMetadata (Io);
/// recompile + reload from the kernel (PluginCompiler / ReloadPlugin).
internal sealed class LifecyclePipeline(IBus bus, string home, bool autoPush)
{
    private const string Source = "MarketplacePlugin";

    // Serializes the commit+push step across concurrent item pipelines so each scoped commit captures exactly
    // its own item without racing another pipeline on the git index.
    private static readonly object CommitGate = new();

    private static string OperationId() => Guid.NewGuid().ToString("N")[..8];

    // The changed item is compiled into a throwaway directory for the gate + surface reflection, so it does
    // not fight the file lock on the loaded plugin's assembly; the in-place reload recompiles into the real
    // build cache after the old context is unloaded. (Modules build into the shared cache, since
    // they are not loaded from there and only take effect on restart anyway.)
    private static string GateBuildDir(string itemName) =>
        Path.Combine(Path.GetTempPath(), "clavis-lifecycle", itemName);

    public Task RunAsync(string itemDir) => Task.Run(() => Run(itemDir));

    private void Run(string itemDir)
    {
        var workingCopy = Directory.GetParent(itemDir)?.Parent?.FullName;
        if (workingCopy is null)
            return;

        // Removal: the item directory is gone. Commit the deletion; we cannot unload by an unknown id, so a
        // restart drops it fully.
        if (!Directory.Exists(itemDir))
        {
            HandleRemoval(workingCopy, itemDir);
            return;
        }

        var info = LifecycleMetadata.read(itemDir);
        if (!info.Found)
        {
            // PLUGIN.md missing or unreadable mid-edit: not an error, just not ready yet.
            bus.LogInfo(Source, $"skipping {Path.GetFileName(itemDir)}: no readable PLUGIN.md yet");
            return;
        }

        var operationId = OperationId();
        try
        {
            RunPipeline(operationId, itemDir, workingCopy, info);
        }
        catch (Exception ex)
        {
            bus.Send(new MarketplaceFailed(operationId, $"{info.Name}: {ex.Message}"));
        }
    }

    private void RunPipeline(string operationId, string itemDir, string workingCopy, LifecycleItem info)
    {
        var isShared = info.Kind == "module";

        // 1. Compile (gate + assembly for surface reflection).
        bus.Send(new MarketplaceProgress(operationId, "compiling", info.Name));
        var buildDir = isShared ? InstallLayout.stagingDirectory(home) : GateBuildDir(info.Name);
        var compiled = PluginCompiler.compile(itemDir, buildDir);
        if (compiled.IsCompilationFailed)
        {
            var errors = ((CompilationResult.CompilationFailed)compiled).errors;
            bus.Send(new MarketplaceFailed(operationId, $"{info.Name} failed to compile:\n{Tail(errors)}"));
            return;
        }
        var assemblyPath = compiled.IsUpToDate
            ? ((CompilationResult.UpToDate)compiled).assemblyPath
            : ((CompilationResult.Compiled)compiled).assemblyPath;

        // 2. Tests (unit then integration). Gate only: a failure stops the run.
        if (!RunTests(operationId, itemDir, workingCopy, info.Name))
            return;

        // 3. Update PLUGIN.md from the new public surface.
        bus.Send(new MarketplaceProgress(operationId, "updating PLUGIN.md", info.Name));
        var update = LifecycleMetadata.update(itemDir, assemblyPath);

        // 4. Reload in place (source) or flag a restart (module lives in the Default ALC).
        if (isShared)
        {
            bus.Send(new RestartRequired($"module '{info.Name}' changed; restart Clavis to apply it"));
        }
        else
        {
            bus.Send(new MarketplaceProgress(operationId, "reloading", info.Name));
            bus.Send(new ReloadPlugin(info.PluginId, itemDir));
        }

        // 5-7. Stage just this item, summarize the change (Claude) for the message, commit only this item,
        // tag the version, and push (best-effort). Serialized across pipelines so each commit and tag captures
        // exactly its own item. Nothing-to-commit is normal for a no-op save, not a failure.
        bus.Send(new MarketplaceProgress(operationId, "committing", info.Name));
        var relativePath = Path.GetRelativePath(workingCopy, itemDir).Replace('\\', '/');
        string summary;
        lock (CommitGate)
        {
            var staged = GitSource.stage(workingCopy, relativePath);
            if (staged.IsError)
            {
                bus.Send(new MarketplaceFailed(operationId, $"{info.Name}: staging failed ({staged.ErrorValue})"));
                return;
            }

            var diff = GitSource.stagedItemDiff(workingCopy, relativePath);
            var diffText = diff.IsOk ? diff.ResultValue : "";
            var message = SummarizeChange(diffText, info.Name);

            var commit = GitSource.commitStaged(workingCopy, relativePath, message);
            if (commit.IsError)
            {
                bus.LogInfo(Source, $"{info.Name}: nothing to commit ({commit.ErrorValue})");
                bus.Send(new MarketplaceCompleted(operationId, $"{info.Name} reloaded (no changes to commit)"));
                return;
            }

            var pushNote = PushIfAllowed(workingCopy);
            if (update.Bumped)
                TagVersion(workingCopy, info.Name, update.Version);

            summary = isShared
                ? $"{info.Name} updated to v{update.Version}; restart required{pushNote}"
                : $"{info.Name} reloaded at v{update.Version}{pushNote}";
        }
        bus.Send(new MarketplaceCompleted(operationId, summary));
    }

    private bool RunTests(string operationId, string itemDir, string workingCopy, string name)
    {
        bus.Send(new MarketplaceProgress(operationId, "unit tests", name));
        var unitProjects = LifecycleMetadata.unitTestProjects(itemDir);
        if (unitProjects.Length == 0)
            bus.LogWarn(Source, $"{name}: no unit test project found");
        foreach (var project in unitProjects)
        {
            var outcome = TestRunner.run(project);
            if (!outcome.Passed)
            {
                bus.Send(new MarketplaceFailed(operationId, $"{name} unit tests failed:\n{Tail(outcome.Output)}"));
                return false;
            }
        }

        var integration = LifecycleMetadata.integrationTestProject(workingCopy);
        if (integration is not null)
        {
            bus.Send(new MarketplaceProgress(operationId, "integration tests", name));
            var outcome = TestRunner.run(integration);
            if (!outcome.Passed)
            {
                bus.Send(new MarketplaceFailed(operationId, $"{name} integration tests failed:\n{Tail(outcome.Output)}"));
                return false;
            }
        }
        return true;
    }

    private void HandleRemoval(string workingCopy, string itemDir)
    {
        var itemName = Path.GetFileName(itemDir);
        var relativePath = Path.GetRelativePath(workingCopy, itemDir).Replace('\\', '/');
        var operationId = OperationId();
        lock (CommitGate)
        {
            var commit = GitSource.commitPath(workingCopy, relativePath, Lifecycle.removalCommitMessage(itemName));
            if (commit.IsError)
            {
                bus.LogInfo(Source, $"removal of {itemName}: nothing to commit ({commit.ErrorValue})");
                return;
            }
            PushIfAllowed(workingCopy);
        }
        bus.Send(new RestartRequired($"'{itemName}' was removed; restart Clavis to fully unload it"));
        bus.Send(new MarketplaceCompleted(operationId, $"removed {itemName}"));
    }

    private string PushIfAllowed(string workingCopy)
    {
        if (!autoPush)
            return " (push disabled)";
        return GitSource.push(workingCopy).IsError ? " (kept local; push not allowed)" : " (pushed)";
    }

    // Ask the agent facade (IBus.Request<Summarize, SummaryResult>, answered by the provider bridge) to turn
    // the change diff into a one-line commit subject describing what it adds/fixes/removes. Falls back to a
    // generic subject when no bridge is registered (e.g. a headless test host), the request times out, or the
    // summary is empty - so the pipeline never blocks on it.
    private const int CommitSubjectMaxLength = 72;

    private string SummarizeChange(string diff, string itemName)
    {
        var fallback = Lifecycle.fallbackCommitMessage(itemName);
        if (string.IsNullOrWhiteSpace(diff))
            return fallback;

        var framed =
            "Describe what this code change adds, fixes, or removes in terms of features or behaviour. "
            + "Do not mention file names.\n\n" + diff;
        try
        {
            var result = bus.Request<Summarize, SummaryResult>(new Summarize(framed, CommitSubjectMaxLength))
                .GetAwaiter().GetResult();
            var summary = result?.Summary?.Trim();
            return string.IsNullOrWhiteSpace(summary) ? fallback : summary;
        }
        catch (Exception ex)
        {
            bus.LogInfo(Source, $"{itemName}: change summary unavailable ({ex.Message}); using fallback");
            return fallback;
        }
    }

    // Tag the bumped version as v<Major>.<Minor>.<Build>-<plugin-name>, pushing it when auto-push is on. A
    // tag failure (e.g. the tag already exists) is logged, not fatal - the commit still stands.
    private void TagVersion(string workingCopy, string itemName, string version)
    {
        var tag = $"v{version}-{itemName}";
        var result = autoPush ? GitSource.tagAndPush(workingCopy, tag) : GitSource.createTag(workingCopy, tag);
        if (result.IsError)
            bus.LogWarn(Source, $"could not create tag {tag}: {result.ErrorValue}");
    }

    private static string Tail(string output, int lines = 40)
    {
        var all = (output ?? "").Replace("\r\n", "\n").Split('\n');
        return all.Length <= lines ? string.Join("\n", all) : string.Join("\n", all[^lines..]);
    }
}
