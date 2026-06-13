using System.Collections.Concurrent;
using System.Windows;

using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.Selection;

/// The selection popups built on the shared SelectorWindow: model / effort / mode pickers for the active
/// agent session (fed by the provider bridge's AgentCapabilities, never naming a provider), the panel
/// picker, and the agent-driven ask-the-user selection (SelectionRequested). A pick only ever emits a bus
/// message - SetSessionModel/-Mode/-Effort, OpenPanel, or SelectionCompleted; the indicators elsewhere
/// react to the provider's confirmation events, not to the pick itself.
public sealed class SelectionPlugin : IPlugin<SelectionConfig>
{
    public string Id => "Selection";

    public SelectionConfig DefaultConfig => new();

    // The latest capability snapshot (the active session's axes + choice catalogs) and the user-openable
    // panel kinds (kind -> title), both rebuilt from incoming broadcasts.
    private volatile AgentCapabilities? _capabilities;
    private readonly ConcurrentDictionary<string, string> _panelKinds = new(StringComparer.Ordinal);

    // Lazily loaded XAML row templates (created on the dispatcher on first use).
    private SelectionTemplates? _templates;

    public Task<ConfigValidationResult> ValidateConfigAsync(SelectionConfig config) =>
        Task.FromResult<ConfigValidationResult>(
            config.SelectorWidth is >= 200 and <= 1200
                ? new ConfigValid()
                : new ConfigInvalid(["SelectorWidth must be between 200 and 1200"]));

    public Task<IDisposable> ActivateAsync(IBus bus, SelectionConfig config)
    {
        // The bus dispatches by the static message type, so bind to the base AgentStreamEvent and match
        // the concrete case (the same pattern the Conversation plugin uses).
        var streamSubscription = bus.Subscribe<AgentStreamEvent>(evt =>
        {
            if (evt is AgentCapabilities capabilities)
            {
                _capabilities = capabilities;
            }

            return Task.CompletedTask;
        });

        var panelKindSubscription = bus.Subscribe<PanelKindRegistration>(registration =>
        {
            if (registration.IsUserOpenable)
            {
                _panelKinds[registration.Kind] = registration.Title;
            }
            else
            {
                _panelKinds.TryRemove(registration.Kind, out _);
            }

            return Task.CompletedTask;
        });

        var selectModelSubscription = bus.Subscribe<SelectModel>(_ =>
        {
            OnDispatcher(() => ShowModelSelector(bus, config));
            return Task.CompletedTask;
        });

        var selectEffortSubscription = bus.Subscribe<SelectEffort>(_ =>
        {
            OnDispatcher(() => ShowEffortSelector(bus, config));
            return Task.CompletedTask;
        });

        var selectModeSubscription = bus.Subscribe<SelectMode>(_ =>
        {
            OnDispatcher(() => ShowModeSelector(bus, config));
            return Task.CompletedTask;
        });

        var selectPanelSubscription = bus.Subscribe<SelectPanel>(_ =>
        {
            OnDispatcher(() => ShowPanelSelector(bus, config));
            return Task.CompletedTask;
        });

        var selectionRequestedSubscription = bus.Subscribe<SelectionRequested>(request =>
        {
            OnDispatcher(() => ShowRequestedSelection(bus, config, request));
            return Task.CompletedTask;
        });

        bus.Send(new PanelKindsRequested());
        bus.LogInfo(Id, "Selection plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(
            streamSubscription, panelKindSubscription, selectModelSubscription, selectEffortSubscription,
            selectModeSubscription, selectPanelSubscription, selectionRequestedSubscription));
    }

    private static void OnDispatcher(Action action) =>
        Application.Current?.Dispatcher.InvokeAsync(() => action());

    private SelectionTemplates Templates => _templates ??= new SelectionTemplates();

    private static void Show(SelectionConfig config, SelectorOptions options)
    {
        options.Width = config.SelectorWidth;
        new SelectorWindow(options).ShowSelector();
    }

    private void ShowModelSelector(IBus bus, SelectionConfig config)
    {
        if (_capabilities is not { } capabilities || capabilities.Models.Count == 0)
        {
            return;
        }

        var rows = SelectionRows.BuildModels(capabilities.Models);
        Show(config, new SelectorOptions
        {
            Prompt = "Select model",
            GetSuggestions = filter => SelectionRows.Filter(rows, filter, SelectionRows.SearchableFields),
            ItemTemplate = Templates.Model,
            OnAccept = (_, item) =>
            {
                if (item is ModelRow row
                    && !string.Equals(row.Id, capabilities.Model, StringComparison.OrdinalIgnoreCase))
                {
                    bus.Send(new SetSessionModel(capabilities.SessionId, row.Id));
                }
            },
        });
    }

    private void ShowEffortSelector(IBus bus, SelectionConfig config)
    {
        if (_capabilities is not { } capabilities)
        {
            return;
        }

        var supported = capabilities.Models
            .FirstOrDefault(model => string.Equals(model.Id, capabilities.Model, StringComparison.OrdinalIgnoreCase))
            ?.SupportedEfforts ?? [];
        var rows = SelectionRows.BuildEfforts(capabilities.Efforts, supported);
        if (rows.Count == 0)
        {
            return;
        }

        Show(config, new SelectorOptions
        {
            Prompt = "Select effort",
            GetSuggestions = filter => SelectionRows.Filter(rows, filter, SelectionRows.SearchableFields),
            ItemTemplate = Templates.Effort,
            OnAccept = (_, item) =>
            {
                if (item is EffortRow row
                    && !string.Equals(row.Id, capabilities.Effort, StringComparison.OrdinalIgnoreCase))
                {
                    bus.Send(new SetSessionEffort(capabilities.SessionId, row.Id));
                }
            },
        });
    }

    private void ShowModeSelector(IBus bus, SelectionConfig config)
    {
        if (_capabilities is not { } capabilities || capabilities.Modes.Count == 0)
        {
            return;
        }

        var rows = SelectionRows.BuildModes(capabilities.Modes);
        Show(config, new SelectorOptions
        {
            Prompt = "Select mode",
            GetSuggestions = filter => SelectionRows.Filter(rows, filter, SelectionRows.SearchableFields),
            ItemTemplate = Templates.Mode,
            OnAccept = (_, item) =>
            {
                if (item is ModeRow row
                    && !string.Equals(row.Id, capabilities.Mode, StringComparison.OrdinalIgnoreCase))
                {
                    bus.Send(new SetSessionMode(capabilities.SessionId, row.Id));
                }
            },
        });
    }

    private void ShowPanelSelector(IBus bus, SelectionConfig config)
    {
        var rows = SelectionRows.BuildPanels(_panelKinds);
        if (rows.Count == 0)
        {
            return;
        }

        Show(config, new SelectorOptions
        {
            Prompt = "Open panel",
            GetSuggestions = filter => SelectionRows.Filter(rows, filter, SelectionRows.SearchableFields),
            ItemTemplate = Templates.Panel,
            OnAccept = (_, item) =>
            {
                if (item is PanelRow row)
                {
                    bus.Send(new OpenPanel(row.Kind));
                }
            },
        });
    }

    // The agent-driven selection: always answers, also on dismissal, so the requesting tool never hangs.
    private void ShowRequestedSelection(IBus bus, SelectionConfig config, SelectionRequested request)
    {
        var rows = SelectionRows.BuildOptions(request.Options);
        var answered = false;

        void Answer(bool accepted, string value)
        {
            if (!answered)
            {
                answered = true;
                bus.Send(new SelectionCompleted(request.RequestId, accepted, value));
            }
        }

        Show(config, new SelectorOptions
        {
            Prompt = request.Prompt,
            FreeText = request.AllowFreeText,
            GetSuggestions = filter => SelectionRows.Filter(rows, filter, SelectionRows.SearchableFields),
            ItemTemplate = Templates.Option,
            OnAccept = (text, item) =>
            {
                // The highlighted option wins (it is what the user sees selected); free text that matches
                // no option is returned verbatim.
                var value = item is OptionRow row ? row.Value : text.Trim();
                Answer(true, value);
            },
            OnDismiss = () => Answer(false, ""),
        });
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
