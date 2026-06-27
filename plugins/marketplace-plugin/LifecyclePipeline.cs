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
            bus.LogError(Source, $"{info.Name} pipeline threw: {ex}");
            bus.Send(new MarketplaceFailed(operationId, $"{info.Name}: {ex.Message}"));
        }
    }

    private void RunPipeline(string operationId, string itemDir, string workingCopy, LifecycleItem info)
    {
        var isShared = info.Kind == "module";

        // 1. Compile (gate + assembly for surface reflection).
        bus.Send(new MarketplaceProgress(operationId, "compiling", info.Name));
        var buildDir = isShared ? InstallLayout.stagingDirectory(home) : GateBuildDir(info.Name);
        var compiled = InProcessGate.Compile(itemDir, buildDir);
        if (compiled.IsCompilationFailed)
        {
            var errors = ((CompilationResult.CompilationFailed)compiled).errors;
            bus.LogError(Source, $"{info.Name} compile failed: {errors}");
            bus.Send(new MarketplaceFailed(operationId, $"{info.Name} failed to compile:\n{Tail(errors)}"));
            return;
        }
        var assemblyPath = compiled.IsUpToDate
            ? ((CompilationResult.UpToDate)compiled).assemblyPath
            : ((CompilationResult.Compiled)compiled).assemblyPath;

        // 2. Tests (unit then integration). Gate only: a failure stops the run. The plugin-under-test's
        // output dir is where the compile above placed its assembly.
        if (!RunTests(operationId, itemDir, workingCopy, info.Name, Path.GetDirectoryName(assemblyPath)!))
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

        // 5-7. Commit only a real change, and only with a version bump. Serialized across pipelines so each
        // commit and tag captures exactly its own item. Three outcomes:
        //   - a real source change     -> ensure a bump (a surface delta already moved Major/Minor; otherwise
        //                                 apply a Build bump), then commit + tag the new version;
        //   - a first-sighting baseline (surface.json recorded for a newly-seen item, no source change) ->
        //                                 commit the baseline without a bump (the one allowed no-bump commit);
        //   - nothing real changed      -> commit nothing, so a no-op recompile never churns a version-only commit.
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

            // The item diff excludes PLUGIN.md / surface.json / project files, so it is non-empty only for a
            // real source change - never for the pipeline's own version/surface writes.
            var diff = GitSource.stagedItemDiff(workingCopy, relativePath);
            var sourceDiff = diff.IsOk ? diff.ResultValue : "";
            var hasSourceChange = !string.IsNullOrWhiteSpace(sourceDiff);

            if (!hasSourceChange && !update.Baseline && !update.Bumped)
            {
                // Nothing real changed - no source edit, no surface delta, no first baseline to record.
                // Never make a version-only commit.
                GitSource.unstage(workingCopy, relativePath);
                bus.Send(new MarketplaceCompleted(operationId, $"{info.Name}: no changes to commit"));
                return;
            }

            // Every committed code change carries a bump: keep the surface-driven bump if there was one,
            // otherwise apply a Build bump. A first-sighting baseline (no source change, no surface delta)
            // commits without a bump.
            var effective = update;
            if (hasSourceChange && !update.Bumped)
            {
                effective = LifecycleMetadata.bumpBuild(itemDir);
                var restaged = GitSource.stage(workingCopy, relativePath);
                if (restaged.IsError)
                {
                    bus.Send(new MarketplaceFailed(operationId, $"{info.Name}: staging failed ({restaged.ErrorValue})"));
                    return;
                }
            }

            // A source edit describes itself; a surface-only catch-up (a stale surface.json reconciled with
            // no source edit) reads as a plain update; a first-sighting baseline records the surface.
            var message = hasSourceChange
                ? SummarizeChange(sourceDiff, info.Name)
                : update.Baseline
                    ? Lifecycle.baselineCommitMessage(info.Name)
                    : Lifecycle.fallbackCommitMessage(info.Name);

            var commit = GitSource.commitStaged(workingCopy, relativePath, message);
            if (commit.IsError)
            {
                bus.LogInfo(Source, $"{info.Name}: nothing to commit ({commit.ErrorValue})");
                bus.Send(new MarketplaceCompleted(operationId, $"{info.Name} reloaded (no changes to commit)"));
                return;
            }

            var pushNote = PushIfAllowed(workingCopy);
            if (effective.Bumped)
                TagVersion(workingCopy, info.Name, effective.Version);

            summary = isShared
                ? $"{info.Name} updated to v{effective.Version}; restart required{pushNote}"
                : $"{info.Name} reloaded at v{effective.Version}{pushNote}";
        }
        bus.Send(new MarketplaceCompleted(operationId, summary));
    }

    // Runs one test project: compile it in-process (TestBuild) and run it via the out-of-process test host
    // (a clean child process - no SDK, no collectible-ALC identity issues). pluginOutputDir is where the
    // compile above placed the plugin-under-test's assembly, which the test build references and the test
    // host probes. A unique staging dir per run keeps re-runs from fighting the previous run's file lock.
    private (bool ok, string output) RunTestProject(string project, string name, string pluginOutputDir)
    {
        var staging = Path.Combine(Path.GetTempPath(), "clavis-test-gate", name, Guid.NewGuid().ToString("N")[..8]);
        var built = TestBuild.compile(project, pluginOutputDir, InProcessGate.ReferenceRoots(), staging);
        if (built.IsError)
            return (false, built.ErrorValue);
        return InProcessGate.RunTest(built.ResultValue, pluginOutputDir);
    }

    private bool RunTests(string operationId, string itemDir, string workingCopy, string name, string pluginOutputDir)
    {
        bus.Send(new MarketplaceProgress(operationId, "unit tests", name));
        var unitProjects = LifecycleMetadata.unitTestProjects(itemDir);
        if (unitProjects.Length == 0)
            bus.LogWarn(Source, $"{name}: no unit test project found");
        foreach (var project in unitProjects)
        {
            var (ok, output) = RunTestProject(project, name, pluginOutputDir);
            if (!ok)
            {
                bus.LogError(Source, $"{name} unit tests failed: {output}");
                bus.Send(new MarketplaceFailed(operationId, $"{name} unit tests failed:\n{Tail(output)}"));
                return false;
            }
        }

        var integration = LifecycleMetadata.integrationTestProject(workingCopy);
        if (integration is not null)
        {
            bus.Send(new MarketplaceProgress(operationId, "integration tests", name));
            var (ok, output) = RunTestProject(integration, name, pluginOutputDir);
            if (!ok)
            {
                bus.Send(new MarketplaceFailed(operationId, $"{name} integration tests failed:\n{Tail(output)}"));
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
