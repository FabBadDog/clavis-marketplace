using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using FabioSoft.Clavis.Placeholders;

namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// Owns the user's markdown panel definitions (durable config) and turns each into its own dockable panel
/// kind ("markdown:{id}") plus a "Markdown Panels" manager kind. Display panels render their definition's
/// body with placeholder tokens resolved live; the manager creates, edits, renames, and deletes
/// definitions. The impure shell: config round-trip, kind registration, the live-render loop, and CRUD
/// side effects. The pure logic lives in MarkdownCatalog / MarkdownPanelFile / MarkdownKind.
public sealed class MarkdownPanelPlugin : IPlugin<MarkdownPanelConfig>
{
    private const double DisplayMinWidth = 240;
    private const double DisplayMinHeight = 160;
    private const double ManagerMinWidth = 460;
    private const double ManagerMinHeight = 320;

    private sealed record DisplayEntry(string DefinitionId, MarkdownPanelView View);

    private readonly PlaceholderEngine _engine = new();
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<PlaceholderDescriptor>> _descriptorsByProvider = new();
    private readonly ConcurrentDictionary<Guid, DisplayEntry> _displays = new();
    private readonly ConcurrentDictionary<Guid, MarkdownManagerView> _managers = new();

    private IBus _bus = null!;
    private volatile IReadOnlyList<MarkdownDefinition> _definitions = [];

    public string Id => "MarkdownPanel";

    public MarkdownPanelConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(MarkdownPanelConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, MarkdownPanelConfig config)
    {
        _bus = bus;

        var subscriptions = new ISubscription[]
        {
            bus.Subscribe<ConfigResult>(OnConfigResult),
            bus.Subscribe<ConfigChanged>(OnConfigChanged),
            bus.Subscribe<PanelKindsRequested>(_ => { AnnounceAll(); return Task.CompletedTask; }),
            bus.Subscribe<PlaceholderSnapshot>(OnSnapshot),
            bus.Subscribe<RegisterPlaceholderProvider>(OnProvider),
            bus.Subscribe<PanelClosed>(OnPanelClosed),
        };

        // The manager is available before the definitions load (it does not depend on them); display kinds
        // are announced once the catalog arrives.
        AnnounceManager();
        bus.Send(new GetConfig(Id));
        bus.Send(new PlaceholdersRequested());
        bus.LogInfo(Id, "Markdown panel plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscriptions));
    }

    private Task OnConfigResult(ConfigResult result)
    {
        if (result is ConfigFound found && found.PluginId == Id)
        {
            LoadCatalog(found.RawConfig);
        }
        else if (result is ConfigNotFound notFound && notFound.PluginId == Id)
        {
            // Seed a starter definition; the SaveConfig echo (ConfigChanged) loads it, keeping one load path.
            _bus.Send(new SaveConfig(Id, MarkdownPanelFile.SerializeStarter()));
        }

        return Task.CompletedTask;
    }

    private Task OnConfigChanged(ConfigChanged changed)
    {
        if (changed.PluginId == Id)
        {
            LoadCatalog(changed.RawConfig);
        }

        return Task.CompletedTask;
    }

    private void LoadCatalog(string rawConfig)
    {
        try
        {
            _definitions = MarkdownPanelFile.Parse(rawConfig);
        }
        catch (Exception exception)
        {
            _bus.LogWarn(Id, $"Failed to parse markdown panel definitions, keeping current: {exception.Message}");
            return;
        }

        // Never leave the catalog empty once it has actually loaded (a fresh install with no starter, or the
        // last panel just deleted): create a blank so the manager always has a panel to edit. Create persists
        // and echoes a fresh catalog back through here, which finds one definition and settles. Guarded on the
        // parsed load - not the transient pre-load [] - so a manager opened before config arrives never seeds
        // a spurious panel alongside the user's real ones.
        if (_definitions.Count == 0)
        {
            CreateDefinition();
            return;
        }

        AnnounceDisplays();
        RefreshOpenViews();
    }

    private Task OnSnapshot(PlaceholderSnapshot snapshot)
    {
        foreach (var pair in snapshot.Values)
        {
            _values[pair.Key] = pair.Value;
        }

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var values = SnapshotValues();
            foreach (var instanceId in _displays.Keys)
            {
                RenderDisplay(instanceId, values);
            }

            foreach (var manager in _managers.Values)
            {
                manager.RefreshPreview();
            }
        });

        return Task.CompletedTask;
    }

    private Task OnProvider(RegisterPlaceholderProvider message)
    {
        _descriptorsByProvider[message.ProviderId] = message.Descriptors;
        return Task.CompletedTask;
    }

    private Task OnPanelClosed(PanelClosed message)
    {
        _displays.TryRemove(message.InstanceId, out _);
        _managers.TryRemove(message.InstanceId, out _);
        return Task.CompletedTask;
    }

    private void AnnounceAll()
    {
        AnnounceManager();
        AnnounceDisplays();
    }

    private void AnnounceManager() =>
        _bus.Send(new PanelKindRegistration(
            MarkdownKind.ManagerKind, "Markdown Panels", ManagerMinWidth, ManagerMinHeight, "", true,
            context => CreateManagerView(context)));

    private void AnnounceDisplays()
    {
        foreach (var definition in _definitions)
        {
            AnnounceDisplay(definition);
        }
    }

    private void AnnounceDisplay(MarkdownDefinition definition)
    {
        var id = definition.Id;
        _bus.Send(new PanelKindRegistration(
            MarkdownKind.ForDefinition(id), definition.Title, DisplayMinWidth, DisplayMinHeight, "", false,
            context => CreateDisplayView(context, id)));
    }

    private object CreateDisplayView(PanelInstanceContext context, string definitionId)
    {
        var view = new MarkdownPanelView();
        _displays[context.InstanceId] = new DisplayEntry(definitionId, view);
        RenderDisplay(context.InstanceId, SnapshotValues());
        return view.Element;
    }

    private object CreateManagerView(PanelInstanceContext context)
    {
        var controller = new MarkdownManagerController(
            () => _definitions,
            AllDescriptors,
            body => _engine.ResolveToText(body ?? "", SnapshotValues()),
            OpenDefinition,
            CreateDefinition,
            SaveDefinition,
            DeleteDefinition);

        var view = new MarkdownManagerView(controller);
        _managers[context.InstanceId] = view;
        view.RefreshList();
        view.RefreshPreview();
        return view.Element;
    }

    // CRUD, invoked on the UI thread from the manager. Each mutates the catalog in memory, announces the
    // affected kind, and persists; the SaveConfig echo (ConfigChanged) reloads and refreshes open views.

    private string CreateDefinition()
    {
        var id = Guid.NewGuid().ToString("N");
        var title = MarkdownCatalog.NextDefaultTitle(_definitions);
        _definitions = MarkdownCatalog.Add(_definitions, id, title, "");
        var created = MarkdownCatalog.Find(_definitions, id);
        if (created is not null)
        {
            AnnounceDisplay(created);
        }

        _bus.Send(new SaveConfig(Id, MarkdownPanelFile.Serialize(_definitions)));
        return id;
    }

    private void SaveDefinition(string id, string title, string body)
    {
        var previous = MarkdownCatalog.Find(_definitions, id);
        _definitions = MarkdownCatalog.Update(_definitions, id, title, body);
        var updated = MarkdownCatalog.Find(_definitions, id);
        if (updated is null)
        {
            return;
        }

        // Re-announce so a future open uses the new title, and retitle any open tabs bound to this definition.
        AnnounceDisplay(updated);
        _bus.Send(new SaveConfig(Id, MarkdownPanelFile.Serialize(_definitions)));

        if (previous is not null && previous.Title != updated.Title)
        {
            foreach (var (instanceId, entry) in _displays)
            {
                if (entry.DefinitionId == id)
                {
                    _bus.Send(new SetPanelTitle(instanceId, updated.Title));
                }
            }
        }

        var values = SnapshotValues();
        foreach (var (instanceId, entry) in _displays)
        {
            if (entry.DefinitionId == id)
            {
                RenderDisplay(instanceId, values);
            }
        }
    }

    private void DeleteDefinition(string id)
    {
        _definitions = MarkdownCatalog.Delete(_definitions, id);

        foreach (var (instanceId, entry) in _displays)
        {
            if (entry.DefinitionId == id)
            {
                _bus.Send(new ClosePanel(instanceId));
            }
        }

        _bus.Send(new SaveConfig(Id, MarkdownPanelFile.Serialize(_definitions)));
    }

    private void OpenDefinition(string id) =>
        _bus.Send(new OpenPanel(MarkdownKind.ForDefinition(id)));

    private void RefreshOpenViews() =>
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var values = SnapshotValues();
            foreach (var instanceId in _displays.Keys)
            {
                RenderDisplay(instanceId, values);
            }

            foreach (var manager in _managers.Values)
            {
                manager.RefreshList();
                manager.RefreshPreview();
            }
        });

    private void RenderDisplay(Guid instanceId, IReadOnlyDictionary<string, string> values)
    {
        if (_displays.TryGetValue(instanceId, out var entry))
        {
            var definition = MarkdownCatalog.Find(_definitions, entry.DefinitionId);
            entry.View.Render(_engine.ResolveToText(definition?.Body ?? "", values));
        }
    }

    private IReadOnlyDictionary<string, string> SnapshotValues() =>
        new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<PlaceholderDescriptor> AllDescriptors()
    {
        var all = new List<PlaceholderDescriptor>();
        foreach (var descriptors in _descriptorsByProvider.Values)
        {
            all.AddRange(descriptors);
        }

        return all;
    }

    private sealed class PluginDisposable(params ISubscription[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
