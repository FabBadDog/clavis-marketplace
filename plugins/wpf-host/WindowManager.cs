using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Owns every application window and routes window/panel/region bus messages to the right one. The
/// primary window carries the conversation chrome; secondary windows are pure panel hosts. Region
/// contributions keep flowing to the primary window so Conversation / EventsPanel / CommandPalette work
/// unchanged. The whole workspace (windows, docking layout, per-panel state) is persisted and restored.
internal sealed class WindowManager : IDisposable
{
    private const string ConversationKind = "conversation";

    // How a panel kind was last placed, so an Open re-creates it the same way.
    private const string TabMode = "tab";
    private const string SlideMode = "slide";
    private const string WindowMode = "window";
    private const string DefaultSlideEdge = "left";

    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(600);

    private readonly IBus _bus;
    private readonly WpfHostConfig _config;
    private readonly KeymapInput _keymap = new();
    private readonly Dictionary<Guid, WindowHost> _windows = [];
    private readonly List<ISubscription> _subscriptions = [];
    private readonly Guid _primaryWindowId = Guid.NewGuid();
    private readonly ConcurrentDictionary<Guid, string> _panelState = new();
    private readonly Dictionary<Guid, Guid> _pendingRestorePlacement = [];
    private readonly Dictionary<Guid, SlideInRestore> _pendingRestoreSlideIn = [];
    private readonly List<RestoreRequest> _pendingRestoreSends = [];

    // Bus subscriptions feeding each restore placeholder's live compile log, keyed by the panel instance
    // they belong to. Disposed when the placeholder is swapped for the real view (or on host teardown), so
    // a placeholder for a still-compiling panel stops listening the moment its panel materialises.
    private readonly Dictionary<Guid, List<ISubscription>> _placeholderSubscriptions = [];

    // The most recent background-compile activity lines a placeholder shows while its panel's plugin is
    // still compiling.
    private const int PlaceholderLogLines = 5;

    // The placement each panel kind was last seen in. An Open of a kind with no live instance re-creates it
    // here; with a live instance it is revealed in place instead (singleton per kind).
    private readonly Dictionary<string, PanelPlacement> _kindPlacement = new(StringComparer.Ordinal);

    // The persisted workspace lives under this plugin's id in the Configuration plugin (config/WpfHost.yaml).
    private const string PluginId = "WpfHost";

    private readonly DispatcherTimer _saveTimer;
    private readonly FocusTraversal _focusTraversal;
    private readonly TearOffPreview _tearOffPreview = new();
    private GlobalHotkey? _globalHotkey;
    private SummonSignal? _summonSignal;
    private Guid _focusedWindowId;

    // While windows are sliding in or out, further summon/hide requests are ignored: starting a second
    // slide mid-flight would capture an animated position as a window's resting place and park it
    // off-screen (and replacing a running slide silently drops its completion, losing the Hide). The time
    // failsafe keeps the toggle alive even if a completion callback is lost (e.g. a window closed
    // mid-slide). All access is on the dispatcher thread.
    private static readonly TimeSpan VisibilityTransitionFailsafe = TimeSpan.FromSeconds(2);
    private int _pendingVisibilityTransitions;
    private DateTime _visibilityTransitionStarted;

    // The saved layout arrives asynchronously (StateResult), so guard the one-shot restore and remember
    // whether bootstrap already flushed the restore sends (if so, a late restore flushes its own).
    private bool _restoredFromConfig;
    private bool _bootstrapComplete;

    // The windows stay invisible until the essential plugins are ready AND the saved-workspace answer has
    // been applied, then appear once - already at their restored bounds, so the boot never shows a window
    // that then jumps to its saved position. The failsafe reveals anyway when the state answer cannot
    // arrive (a failed Configuration plugin); BootstrapComplete is the final guarantee, ordered before the
    // host's no-window viability check. All access is on the dispatcher thread.
    private static readonly TimeSpan RevealFailsafe = TimeSpan.FromSeconds(2);
    private bool _revealed;
    private bool _essentialsReady;
    private DispatcherTimer? _revealFailsafe;

    // Cached from the Conversation plugin's PermissionPending notifications. Written on a bus thread, read
    // on the UI thread in each window's key handler, so it is volatile. Lets a window route Left/Right/Enter
    // to a pending permission prompt without the host knowing anything about permission internals.
    private volatile bool _permissionPending;

    private readonly record struct RestoreRequest(Guid InstanceId, string Kind, string SavedState);

    private readonly record struct SlideInRestore(Guid WindowId, string Kind, string Title, string Edge);

    private readonly record struct PanelPlacement(Guid WindowId, string Mode, string Edge);

    private readonly record struct LiveInstance(WindowHost Host, Guid PanelId, string Mode);

    public WindowManager(IBus bus, WpfHostConfig config)
    {
        _bus = bus;
        _config = config;
        _focusTraversal = new FocusTraversal(OrderedWindows);
        _saveTimer = new DispatcherTimer { Interval = SaveDebounce };
        _saveTimer.Tick += (_, _) => SaveWorkspace();

        SubscribeToBus();
        SeedDefaultSlidePlacement();

        // The primary window is created now but revealed later (see Reveal): it stays invisible until the
        // essential plugins are up and the saved workspace has been applied, then falls in from the top of
        // the screen - fully formed - as the host's splash drops out the bottom.
        var primary = CreatePrimaryWindow();

        // System-scope bindings register as OS global hotkeys on the primary window; a press runs the
        // bound command through the same RunCommand path as any other binding.
        _globalHotkey = new GlobalHotkey(primary.Window, command => _bus.Send(new RunCommand(command)));

        // A second Clavis launch signals the host's activation event instead of booting; route it into
        // the same summon path as the global hotkey.
        _summonSignal = new SummonSignal(() => _bus.Send(new SummonClavis()));

        // Pull the current keymap and command catalog in case KeyMap / CommandPalette activated first.
        _bus.Send(new RequestKeymap());
        _bus.Send(new RequestCommands());

        // The saved workspace is this plugin's runtime state (the WpfHost section of state.yaml via the
        // Configuration plugin) - disposable layout, not configuration. Request it; StateResult restores
        // bounds, the docking tree, secondary windows and panels onto the already-shown primary - the
        // window appears first and the saved layout follows a moment later.
        _bus.Send(new GetState(PluginId));

        _bus.LogInfo("WpfHost", "WPF host plugin activated; awaiting essentials before the reveal");
    }

    private void SubscribeToBus()
    {
        _subscriptions.Add(_bus.Subscribe<UiRegionContribution>(contribution =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.Regions.AddContribution(contribution));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<UiRegionRemoved>(removal =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.Regions.RemoveContribution(removal));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<PanelInstanceReady>(ready =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => PlacePanel(ready));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<PanelStateChanged>(message =>
        {
            _panelState[message.InstanceId] = message.State;
            Application.Current.Dispatcher.InvokeAsync(ScheduleSave);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<OpenConversation>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.SeedConversation());
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ShowSlideIn>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
                _windows.Values.FirstOrDefault(window => window.HasSlideIn(message.InstanceId))?.ShowSlideIn(message.InstanceId));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CloseWindow>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => CloseSecondaryWindow(message.WindowId));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<FocusInputRequested>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.Focus());
            return Task.CompletedTask;
        }));

        // The conversation owner reports when prompts can be accepted; until then the prompt input
        // stays collapsed (the host knows no session vocabulary - just this availability broadcast).
        _subscriptions.Add(_bus.Subscribe<PromptInputAvailability>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.SetPromptInputVisible(message.Available));
            return Task.CompletedTask;
        }));

        // The active panel's owner reports whether its status bar has content; collapse the primary window's
        // status row when it has none so the panel fills the space (the host knows no placeholder vocabulary).
        _subscriptions.Add(_bus.Subscribe<StatusBarAvailability>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.SetStatusBarVisible(message.Available));
            return Task.CompletedTask;
        }));

        // The saved workspace arrives as this plugin's runtime state; restore it onto the (still hidden)
        // primary, then reveal once the essential set is also up.
        _subscriptions.Add(_bus.Subscribe<StateResult>(result =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnStateResult(result);
                RevealWhenReady();
            });
            return Task.CompletedTask;
        }));

        // The essential plugins are up (Configuration among them, so the state answer normally precedes
        // this). If that answer cannot arrive - a failed Configuration plugin - the failsafe reveals with
        // the default placement after a short grace rather than never.
        _subscriptions.Add(_bus.Subscribe<EssentialPluginsReady>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _essentialsReady = true;
                RevealWhenReady();
                StartRevealFailsafe();
            });
            return Task.CompletedTask;
        }));

        // Restore sends are deferred until every plugin is up, so the registry has the panel kinds it
        // needs to resolve them. Reveal() first: bootstrap completion is the reveal's final guarantee,
        // queued at normal priority so it precedes the host's idle-priority no-window viability check.
        _subscriptions.Add(_bus.Subscribe<BootstrapComplete>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Reveal();
                _bootstrapComplete = true;
                FlushRestoreSends();
            });
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<KeymapChanged>(changed =>
        {
            _keymap.Update(changed.Bindings);
            var systemBindings = changed.Bindings.Where(binding => binding.Scope == KeymapScope.System).ToList();
            Application.Current.Dispatcher.InvokeAsync(() => _globalHotkey?.SetSystemBindings(systemBindings));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CommandsAvailable>(available =>
        {
            _keymap.UpdateCommands(available.Commands);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<TogglePanel>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => TogglePanel(message.Kind));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CloseActivePanel>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(CloseActivePanel);
            return Task.CompletedTask;
        }));

        // A named panel instance is closed by id (e.g. when a markdown definition is deleted, its owner
        // closes every open panel bound to it). Completes the previously-unwired ClosePanel contract.
        _subscriptions.Add(_bus.Subscribe<ClosePanel>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => ClosePanel(message.InstanceId));
            return Task.CompletedTask;
        }));

        // Retitle a live panel's tab (e.g. when its markdown definition is renamed while docked).
        _subscriptions.Add(_bus.Subscribe<SetPanelTitle>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => RetitlePanel(message.InstanceId, message.Title));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ToggleShortcutHelp>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetFocused()?.ToggleHelp());
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CloseActiveWindow>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => CloseSecondaryWindow(_focusedWindowId));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<SummonClavis>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(Summon);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ToggleClavis>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(ToggleVisibility);
            return Task.CompletedTask;
        }));

        // Introspection: report what is currently on screen. Read on the UI thread (it touches live WPF
        // state), then answer with a single WorkspaceSnapshot - the response half of a bus Request.
        _subscriptions.Add(_bus.Subscribe<WorkspaceSnapshotRequested>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => _bus.Send(BuildSnapshot()));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<PermissionPending>(message =>
        {
            _permissionPending = message.Pending;
            return Task.CompletedTask;
        }));
    }

    private WorkspaceSnapshot BuildSnapshot()
    {
        var windows = _windows.Values
            .Select(host => new WindowSnapshot(host.WindowId, "CLAVIS", host.IsPrimary, host.WindowId == _focusedWindowId))
            .ToArray();

        var focusedPanelId = GetFocused()?.Surface.ActivePanelId ?? Guid.Empty;
        var panels = new List<PanelSnapshot>();

        foreach (var host in _windows.Values)
        {
            var isFocusedWindow = host.WindowId == _focusedWindowId;
            var activePanelId = host.Surface.ActivePanelId;

            foreach (var (slot, isActiveTab) in EnumerateSlotsWithVisibility(host.Surface.Capture()))
            {
                panels.Add(new PanelSnapshot(
                    slot.PanelId, slot.PanelKind, slot.Title, host.WindowId,
                    isFocused: isFocusedWindow && slot.PanelId == activePanelId,
                    isVisible: isActiveTab,
                    placement: TabMode));
            }

            foreach (var slide in host.SlideInDetails)
            {
                panels.Add(new PanelSnapshot(
                    slide.InstanceId, slide.Kind, slide.Title, host.WindowId,
                    isFocused: false,
                    isVisible: slide.IsOpen,
                    placement: SlideMode));
            }
        }

        return new WorkspaceSnapshot([.. windows], [.. panels], _focusedWindowId, focusedPanelId);
    }

    /// Register each configured panel kind as an edge slide-in in the primary window. Seeded before the
    /// saved layout is restored, so a layout that already docks a kind as a tab overrides its default; a
    /// kind absent from the layout (slide-ins are not persisted) keeps the slide-in placement, so opening
    /// it - via its status-bar glyph or the palette - reveals it from the configured edge.
    private void SeedDefaultSlidePlacement()
    {
        foreach (var panel in _config.DefaultSlidePanels)
        {
            if (string.IsNullOrEmpty(panel.Kind))
            {
                continue;
            }

            var edge = string.IsNullOrEmpty(panel.Edge) ? DefaultSlideEdge : panel.Edge;
            _kindPlacement[panel.Kind] = new PanelPlacement(_primaryWindowId, SlideMode, edge);
        }
    }

    private bool InVisibilityTransition =>
        _pendingVisibilityTransitions > 0
        && DateTime.UtcNow - _visibilityTransitionStarted < VisibilityTransitionFailsafe;

    private void BeginVisibilityTransition(int slidingWindows)
    {
        _pendingVisibilityTransitions = slidingWindows;
        _visibilityTransitionStarted = DateTime.UtcNow;
    }

    private void CompleteVisibilityTransition()
    {
        if (_pendingVisibilityTransitions > 0)
        {
            _pendingVisibilityTransitions--;
        }
    }

    // The reveal preconditions: the essential plugins are active and the saved workspace has been applied
    // (or determined absent). Configuration is essential, so on a healthy boot both arrive back to back.
    private void RevealWhenReady()
    {
        if (_essentialsReady && _restoredFromConfig)
        {
            Reveal();
        }
    }

    private void StartRevealFailsafe()
    {
        if (_revealed || _revealFailsafe is not null)
        {
            return;
        }

        _revealFailsafe = new DispatcherTimer { Interval = RevealFailsafe };
        _revealFailsafe.Tick += (_, _) =>
        {
            _revealFailsafe?.Stop();
            Reveal();
        };
        _revealFailsafe.Start();
    }

    // The boot's one entrance: every window the restore materialised appears together - secondaries
    // first, the primary last so it ends focused - each already at its restored bounds, falling in from
    // the top of the screen as the host's splash drops out the bottom.
    private void Reveal()
    {
        if (_revealed)
        {
            return;
        }

        _revealed = true;
        _revealFailsafe?.Stop();

        var primary = GetPrimary();
        if (primary is null)
        {
            return;
        }

        foreach (var host in OrderedWindows().Reverse())
        {
            host.Window.Show();
            Motion.fallInWindow(host.Window);
        }

        // The primary is now shown, so it is a valid owner: link any secondary restored before the reveal,
        // which could not be owned while the primary was still hidden.
        foreach (var host in OrderedWindows())
        {
            if (!host.IsPrimary && host.Window.Owner is null)
            {
                host.Window.Owner = primary.Window;
            }
        }

        primary.Window.Activate();
        primary.Focus();
        _bus.LogInfo("WpfHost", "primary window shown");

        // Materialise the restored panels now, at the reveal, rather than waiting for BootstrapComplete:
        // PanelRegistry is essential (up by now) and buffers a restore whose owning plugin is still loading,
        // so each panel pops in as its plugin comes up instead of all of them appearing seconds later.
        FlushRestoreSends();
    }

    private void Summon()
    {
        var primary = GetPrimary();
        if (primary is null || !_revealed || InVisibilityTransition)
        {
            return;
        }

        // Secondaries first, the primary last so it ends up activated with keyboard focus. A hidden
        // window falls in from the top; a minimized one just restores (the OS restore already presents
        // it in place); an already-visible one comes forward without replaying the entrance.
        var entering = new List<Window>();
        foreach (var host in OrderedWindows().Reverse())
        {
            var window = host.Window;
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
                window.Show();
            }
            else if (!window.IsVisible)
            {
                entering.Add(window);
            }
            else
            {
                window.Show();
            }
        }

        BeginVisibilityTransition(entering.Count);
        foreach (var window in entering)
        {
            Motion.showWindowFallingIn(window, CompleteVisibilityTransition);
        }

        primary.Window.Activate();
        primary.Window.Topmost = true;
        primary.Window.Topmost = false;
        primary.Focus();
    }

    /// One gesture both summons and banishes the application: with a Clavis window focused, every window
    /// rises up out of the screen and hides; otherwise they are all summoned to the foreground.
    private void ToggleVisibility()
    {
        if (!_revealed || InVisibilityTransition)
        {
            return;
        }

        if (_windows.Values.Any(host => host.Window.IsActive))
        {
            HideAll();
        }
        else
        {
            Summon();
        }
    }

    private void HideAll()
    {
        if (InVisibilityTransition)
        {
            return;
        }

        var sliding = _windows.Values
            .Select(host => host.Window)
            .Where(window => window.IsVisible && window.WindowState != WindowState.Minimized)
            .ToList();

        BeginVisibilityTransition(sliding.Count);
        foreach (var host in _windows.Values)
        {
            var window = host.Window;
            if (sliding.Contains(window))
            {
                Motion.riseOutWindow(window, () =>
                {
                    window.Hide();
                    CompleteVisibilityTransition();
                });
            }
            else
            {
                window.Hide();
            }
        }
    }

    private WindowHost CreatePrimaryWindow()
    {
        var host = new WindowHost(_bus, _config, _keymap, () => _permissionPending, _primaryWindowId, isPrimary: true);
        _focusedWindowId = host.WindowId;
        Application.Current.MainWindow = host.Window;
        Register(host);

        // Default placement until (and unless) the saved layout arrives: centre-screen with the conversation
        // seeded, so the window is never blank. A StateFound restore later applies the saved bounds and
        // rebuilds the surface; SeedConversation is idempotent and Surface.Restore replaces it cleanly.
        host.Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        host.SeedConversation();

        host.Window.Closing += (_, _) =>
        {
            SaveWorkspace();
            _bus.Send(new ApplicationShutdown());
        };

        return host;
    }

    // Restore the saved workspace once, when its state arrives. Other plugins receive StateResult on the
    // same subject, so it is filtered to this plugin's id; StateNotFound (first run, or a deleted state.yaml)
    // leaves the default primary in place. The one-shot guard keeps a later result from rebuilding live
    // windows.
    private void OnStateResult(StateResult result)
    {
        if (_restoredFromConfig)
        {
            return;
        }

        switch (result)
        {
            case StateFound found when found.PluginId == PluginId:
                _restoredFromConfig = true;
                var saved = WorkspaceStore.Deserialize(found.RawState);
                if (saved is not null)
                {
                    RestoreSavedLayout(saved);
                }

                break;

            case StateNotFound notFound when notFound.PluginId == PluginId:
                _restoredFromConfig = true;
                break;
        }
    }

    private void RestoreSavedLayout(WorkspaceLayout saved)
    {
        var primary = GetPrimary();
        var primaryEntry = saved.Windows.FirstOrDefault(window => window.IsPrimary);
        if (primary is not null && primaryEntry is not null)
        {
            if (ApplyBounds(primary.Window, primaryEntry.Bounds))
            {
                primary.Window.WindowState = WindowState.Maximized;
            }

            RestoreLayout(primary, primaryEntry);
        }

        foreach (var entry in saved.Windows.Where(window => !window.IsPrimary))
        {
            RecreateSecondaryWindow(entry);
        }

        // If the window is already up (reveal or BootstrapComplete happened before this restore landed -
        // the failsafe/late-config paths), flush the sends this restore just queued; otherwise Reveal or
        // the BootstrapComplete handler will flush them.
        if (_revealed || _bootstrapComplete)
        {
            FlushRestoreSends();
        }
    }

    private void RecreateSecondaryWindow(PersistedWindow entry)
    {
        var host = NewSecondaryHost(Guid.NewGuid());
        ApplyBounds(host.Window, entry.Bounds);
        RestoreLayout(host, entry);

        // Before the reveal the recreated window stays hidden - Reveal() presents all windows in one
        // entrance. A restore that lands after the reveal (failsafe path) shows it directly.
        if (_revealed)
        {
            ShowWithFade(host.Window);
        }
    }

    private WindowHost NewSecondaryHost(Guid windowId)
    {
        var host = new WindowHost(_bus, _config, _keymap, () => _permissionPending, windowId, isPrimary: false);
        host.Window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Owner can only be set once the primary has been shown; during a pre-reveal restore it is still
        // hidden (WPF would throw), so the owner link is deferred to Reveal in that case.
        LinkToPrimaryOwner(host.Window);
        host.Window.Closing += (_, _) =>
        {
            // A window's slide-ins are not in its docking layout, so retire them explicitly: drop their
            // palette summon commands and dispose the panel instances.
            foreach (var slideInId in host.SlideInIds)
            {
                _bus.Send(new SlideInClosed(slideInId));
                _bus.Send(new PanelClosed(slideInId));
            }

            _windows.Remove(windowId);
            _bus.Send(new WindowClosed(windowId));
            ScheduleSave();
        };
        Register(host);
        return host;
    }

    // Make the primary window own a secondary, so the pair minimize/restore together and the secondary
    // centres on the primary. WPF rejects an owner that has not been shown, so this is a no-op while the
    // primary is still hidden (a pre-reveal restore); Reveal links any such secondary once the primary is up.
    private void LinkToPrimaryOwner(Window secondary)
    {
        var primary = GetPrimary()?.Window;
        if (primary is not null && primary.IsVisible && !ReferenceEquals(secondary, primary))
        {
            secondary.Owner = primary;
        }
    }

    private void CloseSecondaryWindow(Guid windowId)
    {
        if (_windows.TryGetValue(windowId, out var host) && !host.IsPrimary)
        {
            var window = host.Window;
            Motion.fadeWindow(window, 0.0, window.Close);
        }
    }

    // Retire a secondary window once its last panel is gone (no docked panels and no slide-ins). The primary
    // window is never closed this way - its sole panel is locked, so it cannot become empty.
    private void CloseIfEmptySecondary(WindowHost host)
    {
        if (!host.IsPrimary && !host.Surface.PanelIds.Any() && host.SlideInIds.Count == 0)
        {
            CloseSecondaryWindow(host.WindowId);
        }
    }

    // Window entrance: a secondary window falls in from the top of the screen, matching the primary window's
    // drop-in. Close still fades out (CloseSecondaryWindow / CloseWithFade).
    private static void ShowWithFade(Window window)
    {
        window.Show();
        Motion.fallInWindow(window);
    }

    private void Register(WindowHost host)
    {
        _windows[host.WindowId] = host;
        host.FocusTraversal = _focusTraversal;

        host.PanelCloseRequested += (_, panelId) =>
        {
            if (host.IsSolePanelLocked)
            {
                return;
            }

            host.Surface.RemovePanel(panelId);
            _bus.Send(new PanelClosed(panelId));
            ScheduleSave();
        };

        host.Surface.LayoutChanged += (_, _) => ScheduleSave();

        // A panel closed off a secondary window's surface (its last) leaves it empty - retire the window so
        // closing or dragging out the last panel closes the window. Drag-outs are handled at the move sites.
        host.Surface.PanelRemoved += (_, _) => CloseIfEmptySecondary(host);

        host.SlideInMade += (_, made) =>
            _kindPlacement[made.Kind] = new PanelPlacement(host.WindowId, SlideMode, made.Edge);

        host.Surface.ExternalPanelDropped += (_, drop) => MovePanelAcrossWindows(host, drop);

        host.Surface.DragFellThrough += (_, fell) => ResolveCrossWindowDrop(host, fell);

        host.Surface.DragMoving += (_, screenPoint) => UpdateCrossWindowHint(host, screenPoint);

        host.Surface.DragCompleted += (_, _) => ClearCrossWindowHints();

        host.Window.Activated += (_, _) =>
        {
            _focusedWindowId = host.WindowId;
            Application.Current.MainWindow = host.Window;
            _bus.Send(new WindowFocusChanged(host.WindowId));
        };

        host.Window.LocationChanged += (_, _) => ScheduleSave();
        host.Window.StateChanged += (_, _) => ScheduleSave();

        // Magnetic snapping: while this window is dragged, pull its edges to the other windows and the
        // monitor work areas. The neighbour rectangles are read fresh on each move, so they always
        // reflect the live layout.
        WindowSnapBehavior.Attach(host.Window, () => OtherWindowRects(host));
    }

    // The physical-pixel rectangles of every window except the given one, so a dragged window can snap to
    // its neighbours. Hidden and minimized windows yield null and are skipped.
    private IReadOnlyList<ScreenRectangle> OtherWindowRects(WindowHost self)
    {
        var rects = new List<ScreenRectangle>();
        foreach (var host in _windows.Values)
        {
            if (ReferenceEquals(host, self))
            {
                continue;
            }

            if (WindowSnapBehavior.RectOf(host.Window) is { } rect)
            {
                rects.Add(rect);
            }
        }

        return rects;
    }

    private void RestoreLayout(WindowHost host, PersistedWindow entry)
    {
        host.Surface.Restore(entry.Layout, (panelId, kind) => ResolveRestoreView(host, panelId, kind));

        // The conversation must always exist in the primary window. A layout persisted without it (e.g. an
        // earlier build let the Chat panel be closed) would otherwise restore to a blank window, so re-seed.
        if (host.IsPrimary && !EnumerateSlots(entry.Layout).Any(slot => slot.PanelKind == ConversationKind))
        {
            host.SeedConversation();
        }

        foreach (var slot in EnumerateSlots(entry.Layout))
        {
            if (slot.PanelKind == ConversationKind)
            {
                continue;
            }

            _panelState[slot.PanelId] = slot.SavedState ?? "";
            _pendingRestorePlacement[slot.PanelId] = host.WindowId;
            _kindPlacement[slot.PanelKind] = new PanelPlacement(host.WindowId, TabMode, "");
            _pendingRestoreSends.Add(new RestoreRequest(slot.PanelId, slot.PanelKind, slot.SavedState ?? ""));
        }

        // Slide-ins are not part of the docking tree, so re-materialise them separately: parked (hidden) on
        // the same edge of this window, and remembered as the kind's placement so opening it later slides it
        // back in from there rather than docking a fresh tab.
        foreach (var slide in entry.SlideIns ?? [])
        {
            if (slide.Kind == ConversationKind)
            {
                continue;
            }

            _panelState[slide.PanelId] = slide.SavedState ?? "";
            _pendingRestoreSlideIn[slide.PanelId] = new SlideInRestore(host.WindowId, slide.Kind, slide.Title, slide.Edge);
            _kindPlacement[slide.Kind] = new PanelPlacement(host.WindowId, SlideMode, slide.Edge);
            _pendingRestoreSends.Add(new RestoreRequest(slide.PanelId, slide.Kind, slide.SavedState ?? ""));
        }
    }

    private FrameworkElement ResolveRestoreView(WindowHost host, Guid panelId, string kind) =>
        kind == ConversationKind ? host.ConversationPanelView : CreatePlaceholderView(panelId, kind);

    private void FlushRestoreSends()
    {
        foreach (var request in _pendingRestoreSends)
        {
            _bus.Send(new RestorePanel(request.InstanceId, request.Kind, request.SavedState));
        }

        _pendingRestoreSends.Clear();
    }

    private void PlacePanel(PanelInstanceReady ready)
    {
        // Restore path: the slot already exists in the surface (showing its compile-log placeholder while the
        // panel's plugin was still building). Stop the placeholder's log, swap in the real view, and fade it
        // in so the panel resolves out of the placeholder rather than hard-cutting.
        if (_pendingRestorePlacement.Remove(ready.InstanceId, out var windowId)
            && _windows.TryGetValue(windowId, out var owner))
        {
            DisposePlaceholder(ready.InstanceId);
            var view = (FrameworkElement)ready.View.Invoke();
            owner.Surface.ReplacePanelView(ready.InstanceId, view);
            Motion.appear(view);
            return;
        }

        // Slide-in restore path: re-anchor the panel to its saved edge, parked (hidden) until summoned.
        if (_pendingRestoreSlideIn.Remove(ready.InstanceId, out var slide)
            && _windows.TryGetValue(slide.WindowId, out var slideHost))
        {
            slideHost.AddSlideIn(ready.InstanceId, ready.Kind, ready.Title, (FrameworkElement)ready.View.Invoke(), slide.Edge, show: false);
            return;
        }

        // Singleton per kind: if a panel of this kind is already live, reveal it where it sits and drop the
        // duplicate the registry just minted (its view was never built, so there is nothing to dispose).
        var existing = FindLiveInstance(ready.Kind, ready.InstanceId);
        if (existing is not null)
        {
            RevealInstance(existing.Value);
            _bus.Send(new PanelClosed(ready.InstanceId));
            return;
        }

        PlaceFresh(ready, (FrameworkElement)ready.View.Invoke());
        ScheduleSave();
    }

    /// The live instance of a kind, if one is currently placed: a docked tab or an edge slide-in in any
    /// window. Excludes the just-minted instance so an Open does not match itself.
    private LiveInstance? FindLiveInstance(string kind, Guid exclude)
    {
        foreach (var host in _windows.Values)
        {
            foreach (var slot in EnumerateSlots(host.Surface.Capture()))
            {
                if (slot.PanelKind == kind && slot.PanelId != exclude)
                {
                    return new LiveInstance(host, slot.PanelId, TabMode);
                }
            }
        }

        foreach (var host in _windows.Values)
        {
            foreach (var (instanceId, slideKind) in host.SlideInInstances)
            {
                if (slideKind == kind && instanceId != exclude)
                {
                    return new LiveInstance(host, instanceId, SlideMode);
                }
            }
        }

        return null;
    }

    /// Bring an existing panel back to the user: surface its window, then either slide it in or focus its
    /// tab. Covers "the window is open but not displayed" (activate) and "it was slid in" (re-summon).
    private static void RevealInstance(LiveInstance instance)
    {
        BringToFront(instance.Host.Window);

        if (instance.Mode == SlideMode)
        {
            instance.Host.ShowSlideIn(instance.PanelId);
        }
        else
        {
            instance.Host.Surface.FocusPanel(instance.PanelId);
            instance.Host.FocusSurface();
        }
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    /// Open the kind if it has no live instance, or close/dismiss it if it does - the one gesture that both
    /// summons and banishes a panel. A live slide-in toggles its visibility (shown -> hidden, hidden ->
    /// revealed); a live docked tab closes; with no live instance it opens through the normal placement path.
    private void TogglePanel(string kind)
    {
        var live = FindLiveInstance(kind, Guid.Empty);
        if (live is null)
        {
            _bus.Send(new OpenPanel(kind));
            return;
        }

        var instance = live.Value;
        if (instance.Mode == SlideMode)
        {
            if (instance.Host.IsSlideInOpen(instance.PanelId))
            {
                instance.Host.HideSlideIn(instance.PanelId);
            }
            else
            {
                BringToFront(instance.Host.Window);
                instance.Host.ShowSlideIn(instance.PanelId);
            }

            return;
        }

        // The primary window's locked sole panel cannot be toggled away - the main window always keeps it.
        if (instance.Host.IsSolePanelLocked)
        {
            return;
        }

        instance.Host.Surface.RemovePanel(instance.PanelId);
        _bus.Send(new PanelClosed(instance.PanelId));
        ScheduleSave();
    }

    /// Close or dismiss whatever panel the focused window currently surfaces: an open slide-in is hidden
    /// first, otherwise the active docked panel (never the conversation) is closed. Backs the panel-scoped
    /// Esc binding, which resolves only while a closeable panel holds focus.
    private void CloseActivePanel()
    {
        var host = GetFocused() ?? GetPrimary();
        if (host is null)
        {
            return;
        }

        if (host.DismissOpenSlideIns())
        {
            return;
        }

        var closed = host.CloseActiveDockedPanel();
        if (closed != Guid.Empty)
        {
            _bus.Send(new PanelClosed(closed));
            ScheduleSave();
        }
    }

    /// Close a docked panel instance by id, wherever it is docked: remove it from its surface, announce
    /// PanelClosed, and persist. Mirrors the tab-close path so an emptied secondary window is retired via
    /// Surface.PanelRemoved. The primary window's locked sole panel (the conversation) is never removed.
    /// A slide-in instance is left to the window/close paths (markdown display panels dock as tabs).
    private void ClosePanel(Guid instanceId)
    {
        foreach (var host in _windows.Values)
        {
            if (host.Surface.PanelIds.Contains(instanceId))
            {
                if (host.IsSolePanelLocked)
                {
                    return;
                }

                host.Surface.RemovePanel(instanceId);
                _bus.Send(new PanelClosed(instanceId));
                ScheduleSave();
                return;
            }
        }
    }

    /// Retitle a docked panel's tab in place. The surface's LayoutChanged (fired by RetitlePanel) schedules
    /// the layout save, so the new title persists.
    private void RetitlePanel(Guid instanceId, string title)
    {
        foreach (var host in _windows.Values)
        {
            if (host.Surface.PanelIds.Contains(instanceId))
            {
                host.Surface.RetitlePanel(instanceId, title);
                return;
            }
        }
    }

    /// First open (no live instance) of a kind: place it in the mode it was last in - a tab in a window, an
    /// edge slide-in, or a fresh standalone window - falling back to a tab in the active window.
    private void PlaceFresh(PanelInstanceReady ready, FrameworkElement view)
    {
        var placement = _kindPlacement.GetValueOrDefault(ready.Kind);

        switch (placement.Mode)
        {
            case SlideMode:
            {
                var host = ResolveWindow(placement.WindowId);
                var edge = string.IsNullOrEmpty(placement.Edge) ? DefaultSlideEdge : placement.Edge;
                host.AddSlideIn(ready.InstanceId, ready.Kind, ready.Title, view, edge);
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, SlideMode, edge);
                return;
            }

            case WindowMode:
            {
                var host = NewSecondaryHost(Guid.NewGuid());
                host.Surface.AddPanel(ready.InstanceId, ready.Kind, ready.Title, view, DockTarget.IntoActiveGroup);
                ShowWithFade(host.Window);
                _bus.Send(new WindowOpened(host.WindowId, "CLAVIS"));
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, WindowMode, "");
                return;
            }

            default:
            {
                var host = ResolveWindow(placement.WindowId);
                host.Surface.AddPanel(ready.InstanceId, ready.Kind, ready.Title, view, DockTarget.IntoActiveGroup);
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, TabMode, "");
                return;
            }
        }
    }

    private WindowHost ResolveWindow(Guid windowId) =>
        _windows.TryGetValue(windowId, out var host) ? host : (GetFocused() ?? GetPrimary())!;

    /// A panel was dropped on targetHost's surface but lives in another window. Lift it from its current
    /// surface (preserving its live view) and adopt it here, so panels drag freely between windows. This is
    /// the OLE path, taken when the target window did register as a drop target (e.g. the primary window).
    private void MovePanelAcrossWindows(WindowHost targetHost, ExternalPanelDrop drop)
    {
        var source = _windows.Values.FirstOrDefault(window =>
            !ReferenceEquals(window, targetHost) && window.Surface.PanelIds.Contains(drop.PanelId));
        if (source is null)
        {
            return;
        }

        var transfer = source.Surface.TryTakePanel(drop.PanelId);
        if (transfer is null)
        {
            return;
        }

        targetHost.Surface.AddExistingPanel(transfer, drop.Target);
        _kindPlacement[transfer.Slot.PanelKind] = new PanelPlacement(targetHost.WindowId, TabMode, "");
        CloseIfEmptySecondary(source);
        ScheduleSave();
    }

    /// A drag off sourceHost ended with no window accepting the OLE drop - the case where the target is an
    /// owned, transparent window the OS never registered as a drop target. Resolve by the cursor's screen
    /// point: drop into another window's surface at the zone under the cursor, leave the panel be if it
    /// landed back over its own window (no zone), or - dropped clear of every window - tear it off into a
    /// brand-new window at that point.
    private void ResolveCrossWindowDrop(WindowHost sourceHost, DragFellThrough fell)
    {
        var target = _windows.Values.FirstOrDefault(window =>
            !ReferenceEquals(window, sourceHost) && IsPointOverSurface(window, fell.ScreenPoint));
        if (target is not null)
        {
            var moved = sourceHost.Surface.TryTakePanel(fell.PanelId);
            if (moved is not null)
            {
                target.Surface.AddExistingPanelAt(moved, fell.ScreenPoint);
                _kindPlacement[moved.Slot.PanelKind] = new PanelPlacement(target.WindowId, TabMode, "");
                CloseIfEmptySecondary(sourceHost);
                ScheduleSave();
            }

            return;
        }

        if (IsPointOverSurface(sourceHost, fell.ScreenPoint))
        {
            return; // dropped back over its own window but not on a dock zone - leave it where it was
        }

        TearOffToNewWindow(sourceHost, fell);
    }

    /// Tear a panel out into a new window positioned at the drop point. Replaces the old "new empty window"
    /// flow: windows now come into being only by dragging a panel clear of every existing window.
    private void TearOffToNewWindow(WindowHost sourceHost, DragFellThrough fell)
    {
        var transfer = sourceHost.Surface.TryTakePanel(fell.PanelId);
        if (transfer is null)
        {
            return;
        }

        var host = NewSecondaryHost(Guid.NewGuid());
        PositionAtCursor(host.Window, sourceHost.Window, fell.ScreenPoint);
        host.Surface.AddExistingPanel(transfer, DockTarget.IntoActiveGroup);
        ShowWithFade(host.Window);
        _bus.Send(new WindowOpened(host.WindowId, "CLAVIS"));
        _kindPlacement[transfer.Slot.PanelKind] = new PanelPlacement(host.WindowId, WindowMode, "");
        CloseIfEmptySecondary(sourceHost);
        ScheduleSave();
    }

    /// Place a torn-off window so its title strip sits under the cursor. The drop point is in physical
    /// pixels (from the OS cursor query); map it to device-independent units using the source window's DPI.
    private static void PositionAtCursor(Window window, Window reference, Point screenPoint)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var source = PresentationSource.FromVisual(reference);
        var point = source is null
            ? screenPoint
            : source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
        window.Left = point.X - 60;
        window.Top = point.Y - 14;
    }

    /// Paint the drop-zone hint on whichever window the cursor is over during a cross-window drag started
    /// on sourceHost. The source window keeps painting its own hint through the working same-window OLE
    /// path, so it is skipped here; every other window is shown the hint when under the cursor, cleared
    /// otherwise.
    private void UpdateCrossWindowHint(WindowHost sourceHost, Point screenPoint)
    {
        foreach (var window in _windows.Values)
        {
            if (ReferenceEquals(window, sourceHost))
            {
                continue;
            }

            if (IsPointOverSurface(window, screenPoint))
            {
                window.Surface.ShowExternalDropHint(screenPoint);
            }
            else
            {
                window.Surface.ClearExternalDropHint();
            }
        }

        // Clear of every window the drop tears the panel off into a new window - preview that outline at the
        // cursor so the gesture's outcome reads, rather than leaving a bare "drop not allowed" cursor.
        var overAnyWindow = _windows.Values.Any(window => IsPointOverWindowBounds(window, screenPoint));
        if (overAnyWindow)
        {
            _tearOffPreview.Hide();
        }
        else
        {
            _tearOffPreview.ShowAt(screenPoint, sourceHost.Window);
        }
    }

    private void ClearCrossWindowHints()
    {
        _tearOffPreview.Hide();
        foreach (var window in _windows.Values)
        {
            window.Surface.ClearExternalDropHint();
        }
    }

    // Whether a screen point falls within a window's whole bounds (chrome included), so a drag over a
    // window's title strip is not mistaken for "clear of every window" and offered as a tear-off.
    private static bool IsPointOverWindowBounds(WindowHost host, Point screenPoint)
    {
        if (host.Window.WindowState == WindowState.Minimized || !host.Window.IsVisible)
        {
            return false;
        }

        try
        {
            var local = host.Window.PointFromScreen(screenPoint);
            return local.X >= 0 && local.Y >= 0
                && local.X <= host.Window.ActualWidth && local.Y <= host.Window.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // window not yet sourced (no HWND); treat as not under the cursor
        }
    }

    private static bool IsPointOverSurface(WindowHost host, Point screenPoint)
    {
        if (host.Window.WindowState == WindowState.Minimized || !host.Window.IsVisible)
        {
            return false;
        }

        try
        {
            var local = host.Surface.PointFromScreen(screenPoint);
            return local.X >= 0 && local.Y >= 0
                && local.X <= host.Surface.ActualWidth && local.Y <= host.Surface.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // the surface has no presentation source (window not shown)
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveWorkspace()
    {
        _saveTimer.Stop();
        try
        {
            _bus.Send(new SaveState(PluginId, WorkspaceStore.Serialize(CaptureWorkspace())));
        }
        catch (Exception exception)
        {
            _bus.LogError("WpfHost", $"Saving workspace layout failed: {exception.Message}");
        }
    }

    private WorkspaceLayout CaptureWorkspace()
    {
        var windows = _windows.Values.Select(CaptureWindow).ToList();
        return new WorkspaceLayout(WorkspaceStore.CurrentVersion, windows);
    }

    private PersistedWindow CaptureWindow(WindowHost host) =>
        new(host.WindowId, host.IsPrimary, BoundsOf(host.Window), FoldState(host.Surface.Capture()))
        {
            SlideIns =
            [
                .. host.SlideInLayouts.Select(slide =>
                    new PersistedSlideIn(slide.InstanceId, slide.Kind, slide.Title, slide.Edge,
                        _panelState.GetValueOrDefault(slide.InstanceId, "")))
            ]
        };

    private LayoutNode FoldState(LayoutNode node)
    {
        var children = (node.Children ?? []).Select(FoldState).ToArray();
        var panels = (node.Panels ?? [])
            .Select(slot => new PanelSlot
            {
                PanelId = slot.PanelId,
                PanelKind = slot.PanelKind,
                Title = slot.Title,
                SavedState = _panelState.GetValueOrDefault(slot.PanelId, "")
            })
            .ToArray();

        return new LayoutNode
        {
            Kind = node.Kind,
            GroupId = node.GroupId,
            Orientation = node.Orientation ?? "",
            Sizes = node.Sizes ?? [],
            Children = children,
            Panels = panels,
            ActiveIndex = node.ActiveIndex
        };
    }

    private static IEnumerable<PanelSlot> EnumerateSlots(LayoutNode node)
    {
        if (node.Kind == DockingModel.Leaf)
        {
            foreach (var slot in node.Panels ?? [])
            {
                yield return slot;
            }
        }
        else
        {
            foreach (var child in node.Children ?? [])
            {
                foreach (var slot in EnumerateSlots(child))
                {
                    yield return slot;
                }
            }
        }
    }

    /// Like EnumerateSlots but tags each panel with whether it is the selected tab of its leaf group, so a
    /// snapshot can tell an on-screen panel from one sitting behind other tabs.
    private static IEnumerable<(PanelSlot Slot, bool IsActiveTab)> EnumerateSlotsWithVisibility(LayoutNode node)
    {
        if (node.Kind == DockingModel.Leaf)
        {
            var panels = node.Panels ?? [];
            for (var index = 0; index < panels.Length; index++)
            {
                yield return (panels[index], index == node.ActiveIndex);
            }
        }
        else
        {
            foreach (var child in node.Children ?? [])
            {
                foreach (var item in EnumerateSlotsWithVisibility(child))
                {
                    yield return item;
                }
            }
        }
    }

    // A restore placeholder that, while its panel's plugin is still compiling in the background, shows the
    // kind plus a live tail of what the kernel is doing right now (which plugin is compiling / has come up).
    // PlacePanel disposes the subscriptions and fades in the real view once the panel materialises.
    private FrameworkElement CreatePlaceholderView(Guid instanceId, string kind)
    {
        var heading = new TextBlock
        {
            Text = $"loading {kind}…",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var log = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        log.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        log.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");

        var lines = new Queue<string>();
        void Append(string line) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lines.Enqueue(line);
            while (lines.Count > PlaceholderLogLines)
            {
                lines.Dequeue();
            }

            log.Text = string.Join("\n", lines);
        });

        _placeholderSubscriptions[instanceId] =
        [
            _bus.Subscribe<PluginDiscovered>(message =>
            {
                Append($"compiling {message.PluginId}…");
                return Task.CompletedTask;
            }),
            _bus.Subscribe<PluginActivated>(message =>
            {
                Append($"ready {message.PluginId}");
                return Task.CompletedTask;
            })
        ];

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(heading);
        stack.Children.Add(log);

        var border = new Border { Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "BlackBrush");
        return border;
    }

    private void DisposePlaceholder(Guid instanceId)
    {
        if (_placeholderSubscriptions.Remove(instanceId, out var subscriptions))
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private static bool ApplyBounds(Window window, PersistedWindowState bounds)
    {
        if (IsOnScreen(bounds))
        {
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            return bounds.IsMaximized;
        }

        if (bounds.IsMaximized)
        {
            return true;
        }

        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return false;
    }

    // Pure geometry: a window is "on screen" when its centre falls within the virtual desktop, so a layout
    // saved on a monitor that is now unplugged falls back to centre-screen instead of opening off-screen.
    private static bool IsOnScreen(PersistedWindowState state)
    {
        var centerX = state.Left + state.Width / 2.0;
        var centerY = state.Top + state.Height / 2.0;

        return centerX >= SystemParameters.VirtualScreenLeft
            && centerX <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && centerY >= SystemParameters.VirtualScreenTop
            && centerY <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    private static PersistedWindowState BoundsOf(Window window)
    {
        var isMaximized = window.WindowState == WindowState.Maximized;
        var bounds = isMaximized
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);
        return new PersistedWindowState(bounds.X, bounds.Y, bounds.Width, bounds.Height, isMaximized);
    }

    private WindowHost? GetPrimary() =>
        _windows.TryGetValue(_primaryWindowId, out var host) ? host : null;

    private WindowHost? GetFocused() =>
        _windows.TryGetValue(_focusedWindowId, out var host) ? host : null;

    // A stable ring for cross-window Tab: the primary first, then the secondaries. The order only needs to
    // be consistent (not screen-accurate) for traversal to cross windows predictably.
    private IReadOnlyList<WindowHost> OrderedWindows()
    {
        var primary = GetPrimary();
        var ordered = new List<WindowHost>();
        if (primary is not null)
        {
            ordered.Add(primary);
        }

        ordered.AddRange(_windows.Values.Where(host => !ReferenceEquals(host, primary)));
        return ordered;
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _globalHotkey?.Dispose();
        _summonSignal?.Dispose();
        _tearOffPreview.Close();

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        foreach (var instanceId in _placeholderSubscriptions.Keys.ToList())
        {
            DisposePlaceholder(instanceId);
        }

        foreach (var host in _windows.Values)
        {
            try { host.Window.Close(); }
            catch { /* window may already be closed */ }
        }
    }
}
