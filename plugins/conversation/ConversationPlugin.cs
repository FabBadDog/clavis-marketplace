using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using FabioSoft.Contracts.Host;
using FabioSoft.Contracts.Session;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;
using FabioSoft.Nucleus.Plugins.Conversation.ViewModels;
using FabioSoft.Nucleus.Plugins.Conversation.Views;

namespace FabioSoft.Nucleus.Plugins.Conversation;

public sealed class ConversationPlugin : IPlugin<ConversationConfig>
{
    // Cadence for refreshing live elapsed-time readouts. Time is fed into the pure update as a tick
    // message (elm-style); the view only re-renders the duration text, never re-parses content.
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    // The conversation's panel-scoped commands, surfaced to the keymap so they bind and show in the help
    // overlay. Default bindings (Shift+Up/Down) ship from KeyMap. Panel-local, so the host routes them to
    // the focused conversation even while the prompt input holds focus.
    private static readonly IReadOnlyList<CommandDescriptor> PanelCommands =
    [
        new CommandDescriptor("conversation.scroll.up", "conversation.scroll.up", "Panel", "conversation", "Scroll up", true),
        new CommandDescriptor("conversation.scroll.down", "conversation.scroll.down", "Panel", "conversation", "Scroll down", true)
    ];

    public string Id => "Conversation";

    public ConversationConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(ConversationConfig config)
    {
        var errors = new List<string>();
        if (config.InitTimeoutSeconds is < 1 or > 600)
        {
            errors.Add("InitTimeoutSeconds must be between 1 and 600");
        }

        return Task.FromResult<ConfigValidationResult>(
            errors.Count > 0 ? new ConfigInvalid(errors) : new ConfigValid());
    }

    public Task<IDisposable> ActivateAsync(IBus bus, ConversationConfig config)
    {
        var state = ConversationState.Init();
        var lockObj = new object();
        var workingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : config.WorkingDirectory;
        var cts = new CancellationTokenSource();

        ConversationViewModel? viewModel = null;
        DispatcherTimer? tickTimer = null;
        var lastPermissionPending = false;
        IReadOnlyDictionary<string, string> lastPlaceholders = new Dictionary<string, string>();
        // The merged values from every provider's snapshot (keys are namespaced so providers never collide),
        // and the placeholder-driven views the status bar and title-bar cluster render from.
        var mergedPlaceholders = new Dictionary<string, string>();
        var currentTemplates = new StatusLineTemplates();
        PlaceholderStatusBar? statusBar = null;
        PlaceholderStrip? agentCluster = null;
        PlaceholderStrip? titleLeft = null;

        // The window's chrome (title + status bars) follows the active docked panel the host announces. The
        // kind metadata map carries each panel's friendly name and optional default status, learned from the
        // PanelKindRegistration broadcasts; the chat is the default active panel.
        var activeKind = PanelChromeResolver.ChatKind;
        var panelKinds = new Dictionary<string, (string Name, string DefaultStatus)>();

        // Guards the chrome inputs (activeKind, currentTemplates, panelKinds) shared by ApplyChrome and the
        // bus handlers that feed them - they fire on independent bus threads. panelKinds is the sharp edge: a
        // tab load re-broadcasts PanelKindsRequested, so panel plugins re-fire PanelKindRegistration and
        // write this map exactly while a concurrent save/active-panel change reads it in ApplyChrome.
        // Unsynchronised that read drops or garbles the update, so a saved bar change intermittently fails to
        // appear - the reported flakiness. ApplyChrome resolves a snapshot under this lock, then dispatches.
        var chromeLock = new object();

        if (Application.Current is not null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                viewModel = new ConversationViewModel(state, PublishPermission);

                var templates = ConversationViewFactory.LoadTemplates();
                Application.Current.Resources.MergedDictionaries.Add(templates);

                statusBar = new PlaceholderStatusBar(
                    currentTemplates.StatusLeft, currentTemplates.StatusCenter, currentTemplates.StatusRight);
                agentCluster = new PlaceholderStrip();
                agentCluster.SetTemplate(currentTemplates.AgentCluster);
                titleLeft = new PlaceholderStrip();
                titleLeft.SetTemplate(currentTemplates.TitleLeft);

                bus.Send(new UiRegionContribution(
                    "main-content", "Conversation", 0,
                    () => ConversationViewFactory.CreateMainContent(viewModel, bus)));

                bus.Send(new UiRegionContribution(
                    "title-bar-left", "Conversation", 0,
                    () => titleLeft.Element));

                bus.Send(new UiRegionContribution(
                    "title-bar-right", "Conversation", 0,
                    () => agentCluster.Element));

                bus.Send(new UiRegionContribution(
                    "status-bar", "Conversation", 0,
                    () => statusBar.Element));

                tickTimer = new DispatcherTimer { Interval = TickInterval };
                tickTimer.Tick += (_, _) =>
                {
                    // A failed elapsed-time refresh is cosmetic; catch it so one bad tick logs and is skipped
                    // rather than escalating to the dispatcher's fatal handler and taking the app down.
                    try
                    {
                        lock (lockObj)
                        {
                            if (!HasLiveTiming(state))
                            {
                                return;
                            }

                            var (newState, effects) = ConversationUpdate.HandleTick(state, DateTime.UtcNow);
                            HandleUpdate(ref state, newState, effects);
                        }
                    }
                    catch (Exception exception)
                    {
                        bus.LogError(Id, $"Conversation tick failed: {exception.Message}");
                    }
                };
                tickTimer.Start();
            });
        }

        var streamSub = bus.Subscribe<AgentStreamEvent>(evt =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleStreamEvent(state, evt);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var errorSub = bus.Subscribe<AgentParsingError>(error =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleParsingError(
                    state, error.SessionId, error.Message, error.IsIgnorable);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        // Typed commands (exit, restart, ...) are now command-palette concerns: the palette resolves
        // them to bus messages (ApplicationShutdown, FullRestartRequested). Here a submitted prompt is
        // always a prompt for Claude.
        var userSubmittedSub = bus.Subscribe<UserSubmittedPrompt>(msg =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleUserSubmitted(state, msg.Prompt);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var userAbortedSub = bus.Subscribe<UserAborted>(_ =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleUserAborted(state);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var cancelQueuedSub = bus.Subscribe<UserCancelledQueued>(_ =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleUserCancelledQueued(state);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var permissionSub = bus.Subscribe<PermissionDecided>(msg =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandlePermissionDecided(
                    state, msg.RequestId, msg.Decision);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var permissionNavigateSub = bus.Subscribe<UserNavigatedPermission>(msg =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandlePermissionNavigate(state, msg.Delta);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var permissionConfirmSub = bus.Subscribe<UserConfirmedPermission>(_ =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandlePermissionConfirm(state);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var restartSub = bus.Subscribe<FullRestartRequested>(_ =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleFullRestart(state);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        // Register the panel-scoped scroll commands now and on request, so order relative to the keymap and
        // command palette never matters.
        bus.Send(new PanelCommandsRegistered(PanelCommands));
        var panelCommandsSub = bus.Subscribe<RequestPanelCommands>(_ =>
        {
            bus.Send(new PanelCommandsRegistered(PanelCommands));
            return Task.CompletedTask;
        });

        // Announce the agent.*/turn.* placeholders, and re-announce + re-publish on request so the status
        // line / editor catalog builds regardless of activation order.
        bus.Send(new RegisterPlaceholderProvider(Id, ConversationDescriptors.All));
        var placeholdersRequestedSub = bus.Subscribe<PlaceholdersRequested>(_ =>
        {
            bus.Send(new RegisterPlaceholderProvider(Id, ConversationDescriptors.All));
            lock (lockObj)
            {
                PublishPlaceholders(state, force: true);
            }
            return Task.CompletedTask;
        });

        // Merge every provider's snapshot (keys are namespaced, so no collisions) and push the result onto
        // the status bar + title-bar cluster on the dispatcher.
        var placeholderSnapshotSub = bus.Subscribe<PlaceholderSnapshot>(snapshot =>
        {
            Dictionary<string, string> copy;
            lock (lockObj)
            {
                foreach (var pair in snapshot.Values)
                {
                    mergedPlaceholders[pair.Key] = pair.Value;
                }
                copy = new Dictionary<string, string>(mergedPlaceholders);
            }

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                statusBar?.Update(copy);
                agentCluster?.SetValues(copy);
                titleLeft?.SetValues(copy);
            });
            return Task.CompletedTask;
        });

        // The usage limit-plane is a placeholder like any other: feed the latest limit windows to the
        // status/title strips so a configured {limitPlane} draws its dots. Each strip remembers the windows
        // and re-applies them across value/template changes, so it suffices to push on every report.
        var usageReportSub = bus.Subscribe<AgentUsageReport>(report =>
        {
            var windows = report.Windows
                .Select(window => new LimitWindow(
                    window.Name, window.Used, window.Total, window.Unit, window.WindowStart, window.ResetsAt))
                .ToArray();
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                statusBar?.SetLimitWindows(windows);
                agentCluster?.SetLimitWindows(windows);
                titleLeft?.SetLimitWindows(windows);
            });
            return Task.CompletedTask;
        });

        // Ask every provider to (re)announce and (re)publish now, so the bars fill immediately.
        bus.Send(new PlaceholdersRequested());

        // Load the configurable status-line / title templates from the shared "StatusLine" section, seeding
        // the defaults on first run, and re-apply whenever the editor panel saves a change.
        var configResultSub = bus.Subscribe<ConfigResult>(result =>
        {
            switch (result)
            {
                case ConfigFound found when found.PluginId == StatusLineTemplates.SectionId:
                    ApplyTemplates(StatusLineTemplates.Parse(found.RawConfig));
                    break;
                case ConfigNotFound notFound when notFound.PluginId == StatusLineTemplates.SectionId:
                    bus.Send(new SaveConfig(StatusLineTemplates.SectionId, new StatusLineTemplates().Serialize()));
                    break;
            }
            return Task.CompletedTask;
        });
        var configChangedSub = bus.Subscribe<ConfigChanged>(changed =>
        {
            if (changed.PluginId == StatusLineTemplates.SectionId)
            {
                ApplyTemplates(StatusLineTemplates.Parse(changed.RawConfig));
            }
            return Task.CompletedTask;
        });
        bus.Send(new GetConfig(StatusLineTemplates.SectionId));

        // The window chrome follows the active docked panel: learn each panel kind's friendly name + default
        // status from its registration, and re-template the chrome when the host announces a new active panel.
        var panelChromeSub = bus.Subscribe<PanelKindRegistration>(registration =>
        {
            bool affectsActivePanel;
            lock (chromeLock)
            {
                panelKinds[registration.Kind] = (registration.Title, registration.StatusTemplate);
                affectsActivePanel = registration.Kind == activeKind;
            }
            if (affectsActivePanel)
            {
                ApplyChrome();
            }
            return Task.CompletedTask;
        });
        var activePanelSub = bus.Subscribe<ActivePanelChanged>(message =>
        {
            lock (chromeLock)
            {
                activeKind = string.IsNullOrEmpty(message.Kind) ? PanelChromeResolver.ChatKind : message.Kind;
            }
            ApplyChrome();
            return Task.CompletedTask;
        });

        // Register the status-line editor as a dockable panel kind (the conversation owns these templates).
        void AnnounceEditorPanel() => bus.Send(new PanelKindRegistration(
            "status-line-editor", "Status Line", 340, 240, "", true,
            _ => Views.StatusLineEditorView.Create(bus)));
        var panelKindsSub = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            AnnounceEditorPanel();
            return Task.CompletedTask;
        });
        AnnounceEditorPanel();

        // The prompt input is the window host's chrome. Make it available as soon as the conversation is
        // up (the init turn already shows "Starting Claude"): a prompt typed while the agent session is
        // still initialising is held as a queued turn by the pure update and sent automatically once the
        // session reports ready, so the user never has to wait out the whole init turn before typing.
        bus.Send(new PromptInputAvailability(true));

        // A plugin that fails during boot lands as an error row in the init turn instead of leaving an
        // eternal spinner. Generic display data - the conversation names no plugin.
        var pluginErrorSub = bus.Subscribe<PluginError>(message =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandlePluginFailure(state, message.PluginId, message.Reason);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        ScheduleInitTimeout();

        // Start the first session. The old Shell did this at boot; under the kernel nobody published
        // the initial StartNewSession, so the session never started. Use the same session id the
        // ConversationState was initialised with so stream events correlate.
        bus.Send(new StartNewSession(state.ActiveSessionId!.Value, workingDirectory, config.Model));

        bus.Send(new LogEntry(
            LogLevel.Info,
            "Conversation",
            "Conversation plugin activated",
            DateTimeOffset.UtcNow));

        var disposable = new PluginDisposable(
            tickTimer,
            cts,
            streamSub, errorSub, userSubmittedSub, userAbortedSub,
            cancelQueuedSub, permissionSub, permissionNavigateSub, permissionConfirmSub, restartSub,
            panelCommandsSub, placeholdersRequestedSub, placeholderSnapshotSub, usageReportSub,
            configResultSub, configChangedSub, panelKindsSub,
            panelChromeSub, activePanelSub,
            pluginErrorSub);

        return Task.FromResult<IDisposable>(disposable);

        void PublishPermission(string requestId, string decision)
            => bus.Send(new PermissionDecided(requestId, decision));

        void UpdateViewModel(ConversationState newState)
        {
            if (viewModel is null)
            {
                return;
            }

            // The projection runs on the dispatcher and InvokeAsync swallows any throw; catch it so a
            // failed view update logs and is skipped rather than vanishing and leaving the UI stale.
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    viewModel.Update(newState);
                }
                catch (Exception exception)
                {
                    bus.LogError(Id, $"Conversation view update failed: {exception.Message}");
                }
            });
        }

        void HandleUpdate(ref ConversationState current, ConversationState newState, ConversationEffect[] effects)
        {
            current = newState;
            UpdateViewModel(newState);
            PublishPermissionPendingIfChanged(newState);
            PublishPlaceholders(newState);
            ProcessEffects(bus, effects, workingDirectory, config.Model);
        }

        // Project the active session onto the agent.*/turn.* placeholder values and broadcast a snapshot,
        // skipping the send when nothing changed so a streaming turn does not spam the bus.
        void PublishPlaceholders(ConversationState newState, bool force = false)
        {
            var values = AgentValues.Build(newState.ActiveSession);
            if (!force && SameValues(values, lastPlaceholders))
            {
                return;
            }

            lastPlaceholders = values;
            bus.Send(new PlaceholderSnapshot(Id, values));
        }

        // Apply configured templates: refresh the active panel's chrome and re-project the turns (whose stats
        // column reads the new template).
        void ApplyTemplates(StatusLineTemplates templates)
        {
            lock (chromeLock)
            {
                currentTemplates = templates;
            }
            ViewModels.TurnViewModel.StatsTemplate = templates.StatsColumn;
            ApplyChrome();
            Application.Current?.Dispatcher.InvokeAsync(() => viewModel?.Update(state));
        }

        // Resolve the chrome for the active docked panel and push it onto the title/status strips (on the
        // dispatcher; the strips are WPF). The chat shows its own title/status; another panel shows its
        // friendly name and its configured-or-default status, or - with neither - an empty bar the host
        // collapses so the panel fills the space.
        void ApplyChrome()
        {
            // Resolve a consistent snapshot of the chrome inputs under the lock, then send/dispatch outside it
            // so the chrome reflects the latest save and active panel without racing the handlers that write them.
            PanelChromeResolved chrome;
            lock (chromeLock)
            {
                var (friendlyName, defaultStatus) = panelKinds.TryGetValue(activeKind, out var meta)
                    ? meta
                    : (activeKind, "");
                chrome = PanelChromeResolver.Resolve(activeKind, currentTemplates, friendlyName, defaultStatus);
            }
            // Tell the host whether this panel's status bar has any content, so it collapses an empty bar and
            // lets the panel fill the space rather than showing a bare strip.
            bus.Send(new StatusBarAvailability(PanelChromeResolver.HasStatusContent(chrome)));
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                titleLeft?.SetTemplate(chrome.TitleLeft);
                agentCluster?.SetTemplate(chrome.TitleRight);
                statusBar?.SetTemplates(chrome.StatusLeft, chrome.StatusCenter, chrome.StatusRight);
            });
        }

        // The host routes Left/Right/Enter to the permission prompt only while one is pending. Announce
        // edge transitions so it can cache a single bool rather than reach into conversation state.
        void PublishPermissionPendingIfChanged(ConversationState newState)
        {
            var pending = ConversationUpdate.HasPendingPermission(newState);
            if (pending != lastPermissionPending)
            {
                lastPermissionPending = pending;
                bus.Send(new PermissionPending(pending));
            }
        }

        // Closes over the live `state` so it reads the current session at fire time and writes the result
        // back to the shared state every bus handler sees - otherwise the init turn never finishes and its
        // pulsing indicators run forever.
        void ScheduleInitTimeout()
        {
            var sessionId = state.ActiveSessionId!.Value;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(config.InitTimeoutSeconds), cts.Token); }
                catch (OperationCanceledException) { return; }

                lock (lockObj)
                {
                    var (newState, effects) = ConversationUpdate.HandleInitTimedOut(state, sessionId);
                    state = newState;
                    UpdateViewModel(newState);
                    ProcessEffects(bus, effects, workingDirectory, config.Model);
                }
            }, cts.Token);
        }
    }

    // Gate the tick so an idle conversation does no work: only refresh while a turn is running (the init
    // turn is Running throughout startup). Without this the whole turn list would re-project 4x a second
    // forever, and the conversation list is not virtualized.
    private static bool HasLiveTiming(ConversationState state) =>
        state.ActiveSession is { } session && session.Turns.Any(turn => turn.Status is Running);

    private static bool SameValues(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static void ProcessEffects(
        IBus bus, ConversationEffect[] effects, string workingDirectory, string? model)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case SendPromptEffect e:
                    bus.Send(new SendPrompt(e.SessionId, e.Text));
                    break;
                case SendPermissionResponseEffect e:
                    bus.Send(new SendPermissionResponse(e.SessionId, e.RequestId, e.Allow));
                    break;
                case InterruptSessionEffect e:
                    bus.Send(new InterruptSession(e.SessionId));
                    break;
                case DisposeSessionEffect e:
                    bus.Send(new DisposeSession(e.SessionId));
                    break;
                case StartNewSessionEffect e:
                    bus.Send(new StartNewSession(e.SessionId, workingDirectory, model));
                    break;
                case ScheduleInitTimeoutEffect:
                    break;
            }
        }
    }

    private sealed class PluginDisposable(
        DispatcherTimer? tickTimer,
        CancellationTokenSource cts,
        params ISubscription[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            if (tickTimer is not null)
            {
                try { tickTimer.Dispatcher.Invoke(tickTimer.Stop); }
                catch { /* cleanup best-effort */ }
            }

            cts.Cancel();
            cts.Dispose();
            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
