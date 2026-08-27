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
    // overlay. Default bindings (Ctrl+Up/Down) ship from KeyMap. Panel-local, so the host routes them to
    // the focused chat even while the prompt input holds focus.
    private static readonly IReadOnlyList<CommandDescriptor> PanelCommands =
    [
        new CommandDescriptor("conversation.scroll.up", "conversation.scroll.up", "Panel", PanelChromeResolver.ChatKind, "Scroll up", true),
        new CommandDescriptor("conversation.scroll.down", "conversation.scroll.down", "Panel", PanelChromeResolver.ChatKind, "Scroll down", true)
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
        var lockObj = new object();

        // No implicit chat: Workspaces owns session creation (the working directory is per workspace), so a
        // chat comes into being when a workspace reports its session started.
        var state = ConversationState.Empty;
        var cts = new CancellationTokenSource();

        // One view model per chat, created by the chat panel's view factory. Until Workspaces owns chat
        // creation there is exactly one chat, so this behaves as the single view model did.
        ChatViewModels? chatViewModels = null;
        DispatcherTimer? tickTimer = null;
        var activityTracker = new SessionActivityTracker();
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
                chatViewModels = new ChatViewModels(PublishPermission);

                var templates = ConversationViewFactory.LoadTemplates();
                Application.Current.Resources.MergedDictionaries.Add(templates);

                statusBar = new PlaceholderStatusBar(
                    currentTemplates.StatusLeft, currentTemplates.StatusCenter, currentTemplates.StatusRight);
                agentCluster = new PlaceholderStrip();
                agentCluster.SetTemplate(currentTemplates.AgentCluster);
                titleLeft = new PlaceholderStrip();
                titleLeft.SetTemplate(currentTemplates.TitleLeft);

                // Clicking a rendered {limitPlane} toggles the usage-limits panel, wherever the plane is
                // placed across these strips.
                void ToggleUsageLimits() => bus.Send(new TogglePanel("usage-limits"));
                statusBar.SetLimitPlaneClick(ToggleUsageLimits);
                agentCluster.SetLimitPlaneClick(ToggleUsageLimits);
                titleLeft.SetLimitPlaneClick(ToggleUsageLimits);

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

        // The chat is a panel kind so the host can place, dock and tear it off without knowing it is a
        // conversation - but it is not a panel the user manages. It is the workspace: exactly one per
        // workspace, opened by the workspace itself rather than from the panel picker (IsUserOpenable=false,
        // so no toggle command and no shortcut synthesise for it either) and never closable. The instance's
        // blob names the chat it shows, so a restored panel re-attaches instead of being re-seeded.
        void AnnounceChatPanel() => bus.Send(new PanelKindRegistration(
            PanelChromeResolver.ChatKind, "Chat", 320, 200, "", false,
            context => Views.ChatPanelView.Create(bus, BindPanelToChat(context), context))
        {
            Cardinality = PanelCardinality.OnePerWorkspace,

            // The chat is the workspace, not a panel docked inside it: one per workspace, never a second, and
            // never closable. Closing it would leave a workspace with no conversation, which is not a state a
            // workspace has.
            IsClosable = false
        });

        // Register the status-line editor as a dockable panel kind (the conversation owns these templates).
        void AnnounceEditorPanel() => bus.Send(new PanelKindRegistration(
            "status-line-editor", "Status Line", 340, 240, "", true,
            _ => Views.StatusLineEditorView.Create(bus)));
        var panelKindsSub = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            AnnounceChatPanel();
            AnnounceEditorPanel();
            return Task.CompletedTask;
        });
        AnnounceChatPanel();
        AnnounceEditorPanel();

        // Reveal the prompt as soon as the conversation is up (the init turn already shows "Starting
        // Claude"): a prompt typed while the agent session is still initialising is held as a queued turn by
        // the pure update and sent automatically once the session reports ready, so the user never has to wait
        // out the whole init turn before typing. The prompt lives in the chat panel now, so this is a view-model
        // fact rather than a broadcast to the host.
        Application.Current?.Dispatcher.InvokeAsync(() => chatViewModels?.SetPromptAvailable(true));

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

        // A workspace's session has started, so it gets a chat bound to that session and directory. This is
        // where a chat comes into being: the conversation no longer starts a session of its own, because the
        // working directory belongs to the workspace.
        var workspaceSessionSub = bus.Subscribe<WorkspaceSessionStarted>(message =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleWorkspaceSession(
                    state, message.WorkspaceId, message.SessionId, message.WorkingDirectory);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var workspaceActivatedSub = bus.Subscribe<WorkspaceActivated>(message =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleWorkspaceActivated(state, message.WorkspaceId);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

        var workspaceClosedSub = bus.Subscribe<WorkspaceClosed>(message =>
        {
            lock (lockObj)
            {
                var (newState, effects) = ConversationUpdate.HandleWorkspaceClosed(state, message.WorkspaceId);
                HandleUpdate(ref state, newState, effects);
            }
            return Task.CompletedTask;
        });

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
            workspaceSessionSub, workspaceActivatedSub, workspaceClosedSub,
            pluginErrorSub);

        return Task.FromResult<IDisposable>(disposable);

        // Reads the session at fire time rather than closing over one: the decision belongs to whichever
        // session is live when the user answers, which a restart can have replaced since the prompt appeared.
        void PublishPermission(string requestId, string decision)
            => bus.Send(new PermissionDecided(state.ActiveSessionId ?? Guid.Empty, requestId, decision));

        // Which chat a panel instance shows, most specific first: the one named in its saved blob if it still
        // exists, else the chat of the workspace the panel is being created for, else the visible chat. The
        // resolved binding is what the panel persists back, so a hand-opened panel gains a concrete chat id and
        // comes back to the same chat next launch.
        //
        // The workspace step is what makes each workspace its own conversation. A freshly seeded panel has an
        // empty blob, so without it every workspace's chat fell through to "whatever is on screen" - which is
        // the chat of whichever workspace happened to be active - and all of them showed the same conversation
        // in different panel instances.
        ChatPanelBinding BindPanelToChat(PanelInstanceContext context)
        {
            var saved = ChatPanelState.Parse(context.SavedState);
            var workspaceId = context.WorkspaceId != Guid.Empty ? context.WorkspaceId : saved.WorkspaceId;
            lock (lockObj)
            {
                // The visible chat is a fallback only for a panel that names no workspace at all. For a panel
                // that does, "this workspace has no chat yet" must resolve to nothing and wait to be adopted -
                // borrowing whatever is on screen is what bound every workspace to the same conversation, and a
                // workspace's session is obtained asynchronously, so the gap is the normal case rather than a
                // rare one.
                var chat = state.Chats.FirstOrDefault(candidate => candidate.ChatId == saved.ChatId)
                    ?? (workspaceId == Guid.Empty
                        ? state.VisibleChat
                        : state.Chats.FirstOrDefault(candidate => candidate.WorkspaceId == workspaceId));
                var chatId = chat?.ChatId ?? saved.ChatId;
                return new ChatPanelBinding(
                    chatViewModels!.ForChat(chat, chatId, workspaceId),
                    new ChatPanelState(chat?.WorkspaceId ?? workspaceId, chatId));
            }
        }

        // Project only the chats the pure update actually touched - it leaves the rest reference-identical,
        // so a streaming turn in one chat does not re-render the others.
        void UpdateViewModels(ConversationState previous, ConversationState newState)
        {
            if (chatViewModels is null)
            {
                return;
            }

            // The projection runs on the dispatcher and InvokeAsync swallows any throw; catch it so a
            // failed view update logs and is skipped rather than vanishing and leaving the UI stale.
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    chatViewModels.Project(previous, newState);
                    chatViewModels.DropClosed(newState);
                }
                catch (Exception exception)
                {
                    bus.LogError(Id, $"Conversation view update failed: {exception.Message}");
                }
            });
        }

        void HandleUpdate(ref ConversationState current, ConversationState newState, ConversationEffect[] effects)
        {
            var previous = current;
            current = newState;
            UpdateViewModels(previous, newState);
            PublishActivityIfChanged(newState);
            PublishPlaceholders(newState);
            ProcessEffects(effects);
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
            Application.Current?.Dispatcher.InvokeAsync(() => chatViewModels?.RefreshAll(state));
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

        // Announce each session's activity - idle, working, blocked on the user - on transitions only.
        // Every session, not just the active one: the point is that a workspace you are not looking at can
        // still say it needs you.
        void PublishActivityIfChanged(ConversationState newState)
        {
            foreach (var session in newState.AllSessions)
            {
                if (activityTracker.Next(session, DateTimeOffset.UtcNow) is { } changed)
                {
                    bus.Send(changed);
                }
            }
        }

        // Execute the pure update's effects. All but the prompt-mode relay are bus sends; the mode is now a
        // chat-panel fact (the prompt lives there), so it goes onto the view model instead.
        void ProcessEffects(ConversationEffect[] effects)
        {
            foreach (var effect in effects)
            {
                switch (effect)
                {
                    case SendPromptEffect e:
                        bus.Send(new SendPrompt(e.SessionId, e.Text));
                        break;
                    case SendPermissionResponseEffect e:
                        bus.Send(new SendPermissionResponse(e.SessionId, e.RequestId, e.OptionId));
                        break;
                    case InterruptSessionEffect e:
                        bus.Send(new InterruptSession(e.SessionId));
                        break;
                    case DisposeSessionEffect e:
                        bus.Send(new DisposeSession(e.SessionId));
                        break;
                    case StartNewSessionEffect e:
                        bus.Send(new StartNewSession(e.SessionId, e.WorkingDirectory, null));
                        break;
                    case ScheduleInitTimeoutEffect e:
                        ScheduleInitTimeout(e.SessionId);
                        break;
                    case PublishPromptModeEffect e:
                        Application.Current?.Dispatcher.InvokeAsync(
                            () => chatViewModels?.SetPromptMode(e.Mode, e.DisplayName));
                        break;
                }
            }
        }

        // Closes over the live `state` so it reads the current session at fire time and writes the result
        // back to the shared state every bus handler sees - otherwise the init turn never finishes and its
        // pulsing indicators run forever. Armed per session, since each chat initialises on its own clock.
        void ScheduleInitTimeout(Guid sessionId)
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(config.InitTimeoutSeconds), cts.Token); }
                catch (OperationCanceledException) { return; }

                lock (lockObj)
                {
                    var (newState, effects) = ConversationUpdate.HandleInitTimedOut(state, sessionId);
                    HandleUpdate(ref state, newState, effects);
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
