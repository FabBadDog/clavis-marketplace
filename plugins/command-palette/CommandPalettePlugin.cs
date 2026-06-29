using System.Collections.Concurrent;
using System.Windows;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Ctrl+Shift+P command palette. Opens a popup that constructs and publishes any string-constructible
/// bus message, resolves user aliases, and passes agent commands (built-in commands + skills) through.
/// The plugin is the impure shell: it owns the bus wiring and the window; the parsing/construction/
/// routing logic is pure. Command discovery is provider-neutral - it listens for AgentCommandsAvailable,
/// which any LLM bridge can emit, rather than anything Claude-specific.
public sealed class CommandPalettePlugin : IPlugin<CommandPaletteConfig>
{
    public string Id => "CommandPalette";

    public CommandPaletteConfig DefaultConfig => new();

    private volatile IReadOnlyDictionary<string, string> _aliases = AliasCatalog.BuiltIns;
    private volatile IReadOnlyList<CommandItem> _externalCommands = [];

    // Agent commands and skills arrive asynchronously (AgentCommandsAvailable). Until the first such event,
    // the palette shows a "loading skills" indicator so an early-opened popup does not look complete.
    private volatile bool _externalCommandsReceived;
    private volatile IReadOnlyDictionary<string, string> _shortcuts = new Dictionary<string, string>();
    private readonly Dictionary<string, CommandDescriptor> _panelCommands = new(StringComparer.Ordinal);

    // Each registered panel kind maps to whether it is user-openable. A non-openable kind (e.g. one that
    // only makes sense once a prerequisite feature exists) still restores from a saved layout but gets no
    // synthesised toggle command.
    private readonly ConcurrentDictionary<string, bool> _panelKinds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _slideIns = new();
    private IReadOnlyList<Type> _catalog = [];
    private PaletteSelector? _palette;

    public Task<ConfigValidationResult> ValidateConfigAsync(CommandPaletteConfig config) =>
        Task.FromResult<ConfigValidationResult>(
            config.PaletteWidth is >= 200 and <= 1200
                ? new ConfigValid()
                : new ConfigInvalid(["PaletteWidth must be between 200 and 1200"]));

    public Task<IDisposable> ActivateAsync(IBus bus, CommandPaletteConfig config)
    {
        _catalog = MessageCatalog.Discover();

        // The bus dispatches by the static message type, so we bind to the base AgentStreamEvent (what
        // the bridge sends under) and match the concrete case - the same pattern the Conversation plugin
        // uses. A direct Subscribe<AgentCommandsAvailable> would never fire.
        var streamSubscription = bus.Subscribe<AgentStreamEvent>(evt =>
        {
            if (evt is AgentCommandsAvailable available)
            {
                _externalCommands = available.Commands
                    .Select(command => CommandItem.FromAgentCommand(command.Name, command.Description))
                    .ToList();
                _externalCommandsReceived = true;
                Application.Current?.Dispatcher.InvokeAsync(() => _palette?.SetLoading(false));
                BroadcastCommands(bus);
            }

            return Task.CompletedTask;
        });

        var configSubscription = bus.Subscribe<ConfigResult>(result =>
        {
            if (result is ConfigFound found && found.PluginId == Id)
            {
                LoadAliases(bus, found.RawConfig);
            }
            else if (result is ConfigNotFound notFound && notFound.PluginId == Id)
            {
                // Seed an editable starter file so the user has the built-in aliases to extend.
                bus.Send(new SaveConfig(Id, AliasCatalog.SerializeStarter()));
            }

            return Task.CompletedTask;
        });

        var changedSubscription = bus.Subscribe<ConfigChanged>(changed =>
        {
            if (changed.PluginId == Id)
            {
                LoadAliases(bus, changed.RawConfig);
            }

            return Task.CompletedTask;
        });

        var toggleSubscription = bus.Subscribe<ToggleCommandPalette>(_ =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() => Toggle(bus, config));
            return Task.CompletedTask;
        });

        // The keymap executes commands through the palette's router, so message/alias/agent commands all
        // run the same way whether typed or triggered by a gesture.
        var runSubscription = bus.Subscribe<RunCommand>(message =>
        {
            Run(bus, message.CommandLine);
            return Task.CompletedTask;
        });

        var requestCommandsSubscription = bus.Subscribe<RequestCommands>(_ =>
        {
            BroadcastCommands(bus);
            return Task.CompletedTask;
        });

        var keymapSubscription = bus.Subscribe<KeymapChanged>(changed =>
        {
            _shortcuts = BuildShortcutLookup(changed.Bindings);
            // An open palette holds a snapshot of the rows, so refresh it in place when bindings change
            // (e.g. the user just bound/unbound a shortcut) so the displayed shortcut updates live.
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (_palette is { IsVisible: true } palette)
                {
                    palette.RefreshSuggestions();
                }
            });
            return Task.CompletedTask;
        });

        // Panels register their panel-scoped commands so the help overlay and management panel can
        // describe them; the palette folds them into its CommandsAvailable broadcast.
        var panelCommandsSubscription = bus.Subscribe<PanelCommandsRegistered>(registered =>
        {
            foreach (var command in registered.Commands)
            {
                _panelCommands[command.Name] = command;
            }

            BroadcastCommands(bus);
            return Task.CompletedTask;
        });

        // Each announced user-openable panel kind gets a synthesised `toggle-<kind>` alias, so every present
        // (and future) panel is open/closeable by name without hardcoding the kinds here.
        var panelKindSubscription = bus.Subscribe<PanelKindRegistration>(registration =>
        {
            var known = _panelKinds.TryGetValue(registration.Kind, out var openable) && openable == registration.IsUserOpenable;
            _panelKinds[registration.Kind] = registration.IsUserOpenable;
            if (!known)
            {
                BroadcastCommands(bus);
            }

            return Task.CompletedTask;
        });

        // A panel anchored as a slide-in gets a synthesised summon command; the matching close drops it.
        var slideInRegisteredSubscription = bus.Subscribe<SlideInRegistered>(registered =>
        {
            _slideIns[registered.InstanceId] = registered.Title;
            BroadcastCommands(bus);
            return Task.CompletedTask;
        });

        var slideInClosedSubscription = bus.Subscribe<SlideInClosed>(closed =>
        {
            if (_slideIns.TryRemove(closed.InstanceId, out _))
            {
                BroadcastCommands(bus);
            }

            return Task.CompletedTask;
        });

        bus.Send(new GetConfig(Id));
        bus.Send(new RequestKeymap());
        bus.Send(new RequestPanelCommands());
        bus.Send(new PanelKindsRequested());
        bus.LogInfo(Id, "Command palette plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(
            this, streamSubscription, configSubscription, changedSubscription, toggleSubscription,
            runSubscription, requestCommandsSubscription, keymapSubscription, panelCommandsSubscription,
            panelKindSubscription, slideInRegisteredSubscription, slideInClosedSubscription));
    }

    /// One gesture per command for display: prefer an application binding, then system, then panel, so
    /// the palette shows the most broadly-applicable shortcut.
    private static IReadOnlyDictionary<string, string> BuildShortcutLookup(IReadOnlyList<KeyBinding> bindings)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings.OrderBy(ScopeRank))
        {
            lookup.TryAdd(binding.Command, binding.Gesture);
        }

        return lookup;
    }

    private static int ScopeRank(KeyBinding binding) => binding.Scope switch
    {
        KeymapScope.Application => 0,
        KeymapScope.System => 1,
        _ => 2
    };

    private void BroadcastCommands(IBus bus)
    {
        var descriptors = CommandCatalog.BuildDescriptors(_catalog, EffectiveAliases(), _externalCommands)
            .Concat(_panelCommands.Values)
            .ToList();
        bus.Send(new CommandsAvailable(descriptors));
    }

    /// The built-in and user aliases merged with a live `toggle-<kind>` alias per user-openable panel kind.
    /// A user-authored alias of the same name overrides the synthesised one.
    private IReadOnlyDictionary<string, string> EffectiveAliases()
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, openable) in _panelKinds)
        {
            if (openable)
            {
                merged[$"toggle-{kind}"] = $"TogglePanel {kind}";
            }
        }

        // One summon command per slide-in, named from its title (e.g. `slide-notes`), each carrying the
        // instance id so ShowSlideIn targets that exact panel. A title clash falls back to a short-id suffix.
        foreach (var (instanceId, title) in _slideIns)
        {
            var name = $"slide-{Slug(title)}";
            if (merged.ContainsKey(name))
            {
                name = $"{name}-{instanceId.ToString()[..4]}";
            }

            merged[name] = $"ShowSlideIn {instanceId}";
        }

        foreach (var (name, template) in _aliases)
        {
            merged[name] = template;
        }

        return merged;
    }

    /// A command-safe slug of a panel title: lowercase, non-alphanumerics folded to single hyphens.
    private static string Slug(string title)
    {
        var folded = new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var collapsed = string.Join('-', folded.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? "panel" : collapsed;
    }

    private void LoadAliases(IBus bus, string rawConfig)
    {
        try
        {
            _aliases = AliasCatalog.Parse(rawConfig);
        }
        catch (Exception exception)
        {
            bus.LogWarn(Id, $"Failed to parse command-palette aliases: {exception.Message}");
        }

        BroadcastCommands(bus);
    }

    private void Run(IBus bus, string commandLine)
    {
        var outcome = Route(commandLine);
        switch (outcome)
        {
            case SendBusMessage:
            case SendAgentPrompt:
                Perform(bus, outcome);
                break;
            case RouteError error:
                bus.LogWarn(Id, $"Command '{commandLine}' failed: {error.Message}");
                break;
            case NoMatch:
                bus.LogError(Id, $"Command '{commandLine}' did not match any known command");
                break;
        }
    }

    private void Toggle(IBus bus, CommandPaletteConfig config)
    {
        _palette ??= new PaletteSelector(
            config.PaletteWidth,
            GetSuggestions,
            Route,
            message => Perform(bus, message),
            (command, gesture) => bus.Send(new SetKeyBinding(command, KeymapScope.Application, "", gesture)),
            gesture => bus.Send(new RemoveKeyBinding(gesture, KeymapScope.Application, "")));

        if (_palette.IsVisible)
        {
            _palette.Hide();
        }
        else
        {
            _palette.Show();
            _palette.SetLoading(!_externalCommandsReceived);
        }
    }

    private IReadOnlyList<CommandItem> GetSuggestions(string input) =>
        CommandSuggestions.Build(input, _catalog, EffectiveAliases(), _externalCommands, _shortcuts);

    private RouteOutcome Route(string input)
    {
        var externalNames = _externalCommands.Select(command => command.Name).ToList();
        return CommandRouter.Route(input, EffectiveAliases(), _catalog, externalNames, Placeholders.Default);
    }

    private void Perform(IBus bus, RouteOutcome outcome)
    {
        switch (outcome)
        {
            case SendBusMessage send:
                BusSender.Send(bus, send.Message);
                break;
            case SendAgentPrompt prompt:
                bus.Send(new UserSubmittedPrompt(prompt.Text));
                break;
        }
    }

    private sealed class PluginDisposable(CommandPalettePlugin plugin, params ISubscription[] subscriptions)
        : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }

            var palette = plugin._palette;
            if (palette is not null)
            {
                try { palette.Close(); }
                catch { /* window may already be closed */ }
            }
        }
    }
}
