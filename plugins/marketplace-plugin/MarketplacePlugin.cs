using FabioSoft.Nucleus.Contracts;
using FabioSoft.Marketplace.Io;

namespace FabioSoft.Nucleus.Plugins.MarketplacePlugin;

public sealed class MarketplacePluginConfig
{
    /// Watch the working copies and run the lifecycle pipeline (test -> reload -> PLUGIN.md -> commit ->
    /// push) when an item changes on disk.
    public bool WatchForChanges { get; init; } = true;

    /// Push the lifecycle commit to the marketplace remote. When false (or when the remote rejects it) the
    /// commit is kept local.
    public bool AutoPush { get; init; } = true;
}

/// The interactive marketplace surface: turns marketplace bus commands into operations against ~/.clavis
/// (via the F# Installer/Catalog engine) and drives the framework's LoadPlugin/UnloadPlugin. Install /
/// update / uninstall / add / remove / list / search are all handled here; long operations report through
/// the MarketplaceProgress / MarketplaceCompleted / MarketplaceFailed / RestartRequired broadcasts.
public sealed class MarketplacePlugin : IPlugin<MarketplacePluginConfig>
{
    public string Id => "MarketplacePlugin";

    public MarketplacePluginConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(MarketplacePluginConfig config)
        => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    // ~/.clavis by default; CLAVIS_HOME overrides it so a standalone host (e.g. the watcher integration test)
    // can point the watcher and its git operations at a throwaway working copy instead of the real install.
    private static string Home =>
        Environment.GetEnvironmentVariable("CLAVIS_HOME") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clavis");

    private static string NewOperationId() => Guid.NewGuid().ToString("N")[..8];

    public Task<IDisposable> ActivateAsync(IBus bus, MarketplacePluginConfig config)
    {
        var subscriptions = new List<IDisposable>
        {
            bus.Subscribe<ListMarketplaces>(_ =>
            {
                var summaries = Catalog.listMarketplaces(Home)
                    .Select(m => new MarketplaceSummary(m.Id, m.SourceKind, m.SourceDetail))
                    .ToArray();
                bus.Send(new MarketplaceList(summaries));
                return Task.CompletedTask;
            }),

            bus.Subscribe<SearchMarketplace>(message =>
            {
                var items = Catalog.search(Home, message.Query ?? "")
                    .Select(i => new AvailableItemSummary(i.Name, i.Marketplace, i.Version, i.Kind, i.Description))
                    .ToArray();
                bus.Send(new MarketplaceSearchResult(items));
                return Task.CompletedTask;
            }),

            bus.Subscribe<AddMarketplace>(message =>
            {
                var operationId = NewOperationId();
                var id = Installer.addMarketplace(Home, message.Source, Slug(message.Source));
                if (string.IsNullOrEmpty(id))
                    bus.Send(new MarketplaceFailed(operationId, $"could not add marketplace from '{message.Source}'"));
                else
                    bus.Send(new MarketplaceCompleted(operationId, $"added marketplace '{id}'"));
                return Task.CompletedTask;
            }),

            bus.Subscribe<RemoveMarketplace>(message =>
            {
                var operationId = NewOperationId();
                if (Catalog.removeMarketplace(Home, message.Id))
                    bus.Send(new MarketplaceCompleted(operationId, $"removed marketplace '{message.Id}'"));
                else
                    bus.Send(new MarketplaceFailed(operationId, $"no such marketplace '{message.Id}'"));
                return Task.CompletedTask;
            }),

            bus.Subscribe<InstallPlugin>(message => Install(bus, message.Name, message.Marketplace, message.VersionRange)),

            bus.Subscribe<UpdatePlugin>(message =>
            {
                var item = Catalog.search(Home, "").FirstOrDefault(i => i.Name == message.Name);
                if (item is null)
                {
                    bus.Send(new MarketplaceFailed(NewOperationId(), $"'{message.Name}' is not available in any registered marketplace"));
                    return Task.CompletedTask;
                }
                return Install(bus, message.Name, item.Marketplace, "");
            }),

            bus.Subscribe<UninstallPlugin>(message =>
            {
                var operationId = NewOperationId();
                var pluginId = Installer.uninstall(Home, message.Name);
                if (string.IsNullOrEmpty(pluginId))
                {
                    bus.Send(new MarketplaceFailed(operationId, $"'{message.Name}' is not installed"));
                }
                else
                {
                    bus.Send(new UnloadPlugin(pluginId));
                    bus.Send(new MarketplaceCompleted(operationId, $"uninstalled '{message.Name}'"));
                }
                return Task.CompletedTask;
            }),
        };

        if (config.WatchForChanges)
            StartWatcher(bus, config, subscriptions);

        bus.LogInfo(Id, "marketplace plugin activated");
        return Task.FromResult<IDisposable>(new CompositeSubscription(subscriptions));
    }

    // The watcher drives the lifecycle pipeline for both triggers (a developer edit and a Clavis self-edit
    // are both on-disk writes). Runs are serialized through a single gate so concurrent edits never launch
    // overlapping dotnet build/test processes.
    private void StartWatcher(IBus bus, MarketplacePluginConfig config, List<IDisposable> subscriptions)
    {
        var pipelineGate = new SemaphoreSlim(1, 1);
        var pipeline = new LifecyclePipeline(bus, Home, config.AutoPush);
        var workingCopies = DiscoverWorkingCopies(Path.Combine(Home, "marketplaces")).ToList();

        async Task RunGated(string itemDir)
        {
            await pipelineGate.WaitAsync();
            try { await pipeline.RunAsync(itemDir); }
            finally { pipelineGate.Release(); }
        }

        var watcher = new WorkingCopyWatcher(workingCopies, RunGated, detail => bus.LogWarn(Id, detail));

        subscriptions.Add(watcher);
        subscriptions.Add(pipelineGate);
        bus.LogInfo(Id, $"watching {workingCopies.Count} working copies for plugin changes");

        // Catch up surfaces missed while Clavis was closed: on startup, run the pipeline for any item whose
        // code changed since its surface.json was recorded (or that has none yet). Runs in the background
        // through the same gate as the watcher, so it never blocks activation or overlaps a live change.
        _ = Task.Run(async () =>
        {
            foreach (var itemDir in StaleItems(workingCopies))
            {
                await RunGated(itemDir);
            }
        });
    }

    // Items (under plugins/ and shared/) across the working copies whose recorded surface is missing or stale
    // - the drift the startup reconciliation catches up.
    private static IEnumerable<string> StaleItems(IEnumerable<string> workingCopies)
    {
        foreach (var workingCopy in workingCopies)
            foreach (var group in new[] { "plugins", "modules" })
            {
                var groupDir = Path.Combine(workingCopy, group);
                if (!Directory.Exists(groupDir))
                {
                    continue;
                }

                foreach (var itemDir in Directory.GetDirectories(groupDir))
                    if (File.Exists(Path.Combine(itemDir, "PLUGIN.md")) && LifecycleMetadata.surfaceStale(itemDir))
                        yield return itemDir;
            }
    }

    // A marketplace working copy is an immediate child of the marketplaces root that holds a plugins/ or
    // modules/ group (the flat output dirs and build caches have neither, so they are skipped).
    private static IEnumerable<string> DiscoverWorkingCopies(string marketplacesDir)
    {
        if (!Directory.Exists(marketplacesDir))
            yield break;
        foreach (var directory in Directory.GetDirectories(marketplacesDir))
            if (Directory.Exists(Path.Combine(directory, "plugins")) || Directory.Exists(Path.Combine(directory, "modules")))
                yield return directory;
    }

    private static Task Install(IBus bus, string name, string marketplace, string? versionRange)
    {
        var operationId = NewOperationId();
        bus.Send(new MarketplaceProgress(operationId, "installing", name));

        var outcome = Installer.install(Home, name, marketplace, versionRange ?? "");
        if (!string.IsNullOrEmpty(outcome.Error))
        {
            bus.Send(new MarketplaceFailed(operationId, outcome.Error));
            return Task.CompletedTask;
        }

        foreach (var path in outcome.LoadPaths)
            bus.Send(new LoadPlugin(path));

        if (outcome.RestartRequired)
            bus.Send(new RestartRequired("a module was installed; restart Clavis to apply it"));

        bus.Send(new MarketplaceCompleted(operationId, $"installed '{name}'"));
        return Task.CompletedTask;
    }

    private static string Slug(string source)
    {
        var kept = new string(source.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrEmpty(kept) ? "marketplace" : kept.ToLowerInvariant();
    }

    private sealed class CompositeSubscription(List<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
                item.Dispose();
        }
    }
}
