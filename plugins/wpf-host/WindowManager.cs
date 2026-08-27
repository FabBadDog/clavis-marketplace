using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Owns every application window and routes window/panel/region bus messages to the right one. The primary
/// window carries the window chrome (title bar, status bar); secondary windows are pure panel hosts. Every
/// panel - the chat included - is placed on a docking surface as a registered kind, so the host names no
/// panel kind of its own. Region contributions keep flowing to the primary window so Conversation /
/// EventsPanel / CommandPalette work unchanged. The whole layout (windows, docking tree, per-panel state) is
/// persisted and restored.
internal sealed partial class WindowManager : IDisposable
{
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
    // The chrome window created before any workspace is known. The first WorkspaceActivated adopts it onto that
    // workspace, so the boot path is unchanged for the first one and only later workspaces mint a new window.
    private readonly Guid _bootstrapWindowId = Guid.NewGuid();

    // Set once the application is genuinely going away, so a workspace window's Closing can refuse every other
    // close. Without it Alt+F4 would still destroy a workspace window that deliberately has no close cross.
    private bool _tearingDown;
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

    // Each kind's declared cardinality, learned from the placement messages, so a Toggle (which carries only
    // a kind) can apply the same rule an Open does without the host subscribing to registrations itself.
    private readonly Dictionary<string, string> _kindCardinality = new(StringComparer.Ordinal);

    // The kinds that declared themselves unclosable, learned the same way. A workspace's chat is the one such
    // kind today: it *is* the workspace, so closing it would leave a workspace that is not one. Held as the
    // exception set rather than a per-kind flag, because everything else closes and an unknown kind must not
    // become unclosable by accident.
    private readonly HashSet<string> _unclosableKinds = new(StringComparer.Ordinal);

    // Which kind each live panel instance is, so a close request carrying only an instance id can be checked
    // against the unclosable set.
    private readonly Dictionary<Guid, string> _panelKind = [];

    // Which workspace each live panel instance belongs to, so a one-per-workspace kind can be found within
    // its own workspace rather than application-wide. Everything is Guid.Empty until workspaces exist, which
    // is exactly the previous application-wide behaviour.
    private readonly Dictionary<Guid, Guid> _panelWorkspace = [];

    // The persisted layout lives under this plugin's id in the Configuration plugin (the WpfHost section of state.yaml).
    private const string PluginId = "WpfHost";

    /// Who still has to finish before the application exits. Quitting is two-phase because some work only
    /// *starts* on the way out - handing a session back to a background agent spawns a process that must be
    /// running before Clavis stops existing, and ApplicationShutdown takes effect immediately.
    private readonly ShutdownBarrier _shutdown = new();

    private readonly DispatcherTimer _saveTimer;
    private readonly FocusTraversal _focusTraversal;
    private readonly TearOffPreview _tearOffPreview = new();
    // The bar is deliberately NOT in _windows: it must never appear in the Tab ring, in snapping neighbours,
    // as a tear-off drop target, in the captured layout, or in a snapshot. Keeping it in its own field makes
    // that structural rather than a filter every site has to remember.
    private BarWindow? _bar;

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

    // Workspaces already considered for the configured default panels. Seeding is per workspace, not per
    // launch: a workspace is one chat plus its panels, so the second one you open needs a chat exactly as much
    // as the first, and it has no saved layout to restore either. One shot per workspace, so a panel closed
    // afterwards stays closed.
    private readonly HashSet<Guid> _seededWorkspaces = [];

    // Workspaces whose saved panels have already been sent for restore. The boot restores the active
    // workspace's layout before the first WorkspaceActivated arrives, and that activation would otherwise
    // restore it a second time - two RestoreRequests per saved panel, and two instances of a kind that is
    // supposed to have one. Restoring is idempotent for the surface (Restore replaces the tree) but not for
    // the sends, so the guard is on the workspace rather than on the tree.
    private readonly HashSet<Guid> _restoredWorkspaces = [];

    // The workspace whose panels are on screen, and the layout as last read from disk. The layout is kept so
    // a capture can carry over the arrangements of workspaces that are not currently shown - otherwise
    // switching away from a workspace and saving would erase what it had.
    private Guid _activeWorkspaceId;

    /// Workspaces that stand for agents running outside Clavis. They exist so such an agent can be shown and
    /// taken over, but they are not workspaces of yours and their owner never writes them down - so nothing in
    /// the layout may be keyed to one either.
    private readonly HashSet<Guid> _transientWorkspaces = [];

    /// The last workspace that was really yours, saved as the one to restore when the active one is transient.
    /// Without it, quitting while looking at a fleet agent would record a workspace the next launch cannot find.
    private Guid _persistableWorkspaceId;
    private PersistedLayout? _restoredLayout;

    // Orphan pruning is one-shot on the first workspace list: later lists reflect workspaces being created and
    // closed during the session, which the live windows already track.
    private bool _orphansDropped;

    // The windows stay invisible until the essential plugins are ready AND the saved-layout answer has
    // been applied, then appear once - already at their restored bounds, so the boot never shows a window
    // that then jumps to its saved position. The failsafe reveals anyway when the state answer cannot
    // arrive (a failed Configuration plugin); BootstrapComplete is the final guarantee, ordered before the
    // host's no-window viability check. All access is on the dispatcher thread.
    private static readonly TimeSpan RevealFailsafe = TimeSpan.FromSeconds(2);
    private bool _revealed;
    private bool _essentialsReady;
    private DispatcherTimer? _revealFailsafe;

    private readonly record struct RestoreRequest(Guid InstanceId, string Kind, string SavedState, Guid WorkspaceId);

    private readonly record struct SlideInRestore(Guid WindowId, string Kind, string Title, string Edge);

    private readonly record struct PanelPlacement(Guid WindowId, string Mode, string Edge);

    private readonly record struct LiveInstance(WindowHost Host, Guid PanelId, string Mode);

    public WindowManager(IBus bus, WpfHostConfig config)
    {
        _bus = bus;
        _config = config;
        _focusTraversal = new FocusTraversal(OrderedWindows);
        _saveTimer = new DispatcherTimer { Interval = SaveDebounce };
        _saveTimer.Tick += (_, _) => SaveLayout();

        SubscribeToBus();
        SeedDefaultSlidePlacement();

        // The primary window is created now but revealed later (see Reveal): it stays invisible until the
        // essential plugins are up and the saved layout has been applied, then falls in from the top of
        // the screen - fully formed - as the host's splash drops out the bottom.
        var primary = CreateWorkspaceWindow(_bootstrapWindowId, Guid.Empty);

        // System-scope bindings register as OS global hotkeys on the primary window; a press runs the
        // bound command through the same RunCommand path as any other binding.
        _globalHotkey = new GlobalHotkey(
            primary.Window,
            command => _bus.Send(new RunCommand(command)),
            message => _bus.LogWarn("WpfHost", message));

        // The bar is created with the primary but shown at the reveal, alongside it.
        if (_config.ShowWorkspaceBar)
        {
            _bar = new BarWindow(_config.WorkspaceBarHeight);
        }

        // A second Clavis launch signals the host's activation event instead of booting; route it into
        // the same summon path as the global hotkey.
        _summonSignal = new SummonSignal(() => _bus.Send(new SummonClavis()));

        // Pull the current keymap and command catalog in case KeyMap / CommandPalette activated first.
        _bus.Send(new RequestKeymap());
        _bus.Send(new RequestCommands());

        // The saved layout is this plugin's runtime state (the WpfHost section of state.yaml via the
        // Configuration plugin) - disposable layout, not configuration. Request it; StateResult restores
        // bounds, the docking tree, secondary windows and panels onto the already-shown primary - the
        // window appears first and the saved layout follows a moment later.
        _bus.Send(new GetState(PluginId));

        _bus.LogInfo("WpfHost", "WPF host plugin activated; awaiting essentials before the reveal");
    }

    private void Register(WindowHost host)
    {
        _windows[host.WindowId] = host;
        host.FocusTraversal = _focusTraversal;

        host.PanelCloseRequested += (_, panelId) =>
        {
            if (_panelKind.TryGetValue(panelId, out var kind) && _unclosableKinds.Contains(kind))
            {
                return;
            }

            host.Surface.RemovePanel(panelId);
            _bus.Send(new PanelClosed(panelId));
            ScheduleSave();
        };

        // Every surface the window owns needs the same handlers, so they are wired as each is created (one per
        // workspace) rather than once for "the" surface.
        host.SurfaceCreated += (_, surface) => WireSurface(host, surface);
        foreach (var surface in host.Surfaces.ToList())
        {
            WireSurface(host, surface);
        }

        host.SlideInMade += (_, made) =>
            _kindPlacement[made.Kind] = new PanelPlacement(host.WindowId, SlideMode, made.Edge);

        // A slide-in's handle drives the same cross-window drop machinery: its drag paints the drop hint,
        // falls through to a re-dock / tear-off, and its close cross dismisses the panel. The panel is lifted
        // from the slide-in (not the surface) at the drop sites via WindowHost.TakePanel.
        host.SlideInDragMoving += (_, screenPoint) => UpdateCrossWindowHint(host, screenPoint);

        host.SlideInDragFellThrough += (_, fell) => ResolveCrossWindowDrop(host, fell);

        host.SlideInDragCompleted += (_, _) => ClearCrossWindowHints();

        host.SlideInCloseRequested += (_, panelId) => CloseSlideInPanel(host, panelId);

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

    // The handlers every docking surface in a window needs. A panel closed off a secondary window's last
    // surface leaves it empty, so the window is retired; drag events drive the cross-window move machinery.
    private void WireSurface(WindowHost host, DockingSurface surface)
    {
        // Closes over the live set, so a kind that declares itself unclosable loses its cross on the next
        // render even on surfaces that already existed.
        surface.IsKindClosable = kind => !_unclosableKinds.Contains(kind);
        surface.LayoutChanged += (_, _) => ScheduleSave();
        surface.PanelRemoved += (_, _) => CloseIfEmptySecondary(host);
        surface.ExternalPanelDropped += (_, drop) => MovePanelAcrossWindows(host, drop);
        surface.DragFellThrough += (_, fell) => ResolveCrossWindowDrop(host, fell);
        surface.DragMoving += (_, screenPoint) => UpdateCrossWindowHint(host, screenPoint);
        surface.DragCompleted += (_, _) => ClearCrossWindowHints();
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

    private WindowHost ResolveWindow(Guid windowId) =>
        _windows.TryGetValue(windowId, out var host) ? host : (GetFocused() ?? GetPrimary())!;

    /// The chrome window of the workspace on screen. There is one per workspace now, so "the primary" is no
    /// longer a fixed window but a question about which workspace is active - every caller that asks for it
    /// (region routing, the reveal, summon, the bar's placement) means "the one the user is looking at".
    ///
    /// Falls back to any chrome window, which covers the moment before the first WorkspaceActivated arrives:
    /// the bootstrap window carries Guid.Empty until it is adopted.
    private WindowHost? GetPrimary() =>
        _windows.Values.FirstOrDefault(host => host.IsPrimary && host.WorkspaceId == _activeWorkspaceId)
        ?? _windows.Values.FirstOrDefault(host => host.IsPrimary);

    /// The chrome window belonging to a given workspace, if it has been created yet.
    private WindowHost? WorkspaceWindow(Guid workspaceId) =>
        _windows.Values.FirstOrDefault(host => host.IsPrimary && host.WorkspaceId == workspaceId);

    private WindowHost? GetFocused() =>
        _windows.TryGetValue(_focusedWindowId, out var host) ? host : null;

    // A stable ring for cross-window Tab: the primary first, then the secondaries. The order only needs to
    // be consistent (not screen-accurate) for traversal to cross windows predictably.
    //
    // Scoped to the active workspace, which is the single funnel every visibility path goes through - the
    // reveal, summon, banish, and the Tab ring all read this, so a secondary window belonging to a workspace
    // you are not looking at is uniformly absent from all of them rather than each site remembering to filter.
    private IReadOnlyList<WindowHost> OrderedWindows()
    {
        var primary = GetPrimary();
        var ordered = new List<WindowHost>();
        if (primary is not null)
        {
            ordered.Add(primary);
        }

        ordered.AddRange(_windows.Values
            .Where(host => !ReferenceEquals(host, primary))
            .Where(IsInActiveWorkspace));
        return ordered;
    }

    /// True when a window belongs on screen for the active workspace. A chrome window is no longer exempt: it
    /// *is* a workspace now, so exactly one of them is on screen at a time and the others are hidden with
    /// their workspace's panel windows. An unassigned window (the bootstrap one before the first activation
    /// adopts it, or a secondary restored from a layout that predates workspaces) belongs to whatever is
    /// active, so it is never stranded invisible.
    private bool IsInActiveWorkspace(WindowHost host) =>
        host.WorkspaceId == Guid.Empty || host.WorkspaceId == _activeWorkspaceId;

    public void Dispose()
    {
        // From here a workspace window's Closing must stop refusing, or teardown would leave its windows up.
        _tearingDown = true;
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

        // The bar is closed only here. Its own Closing must never send ApplicationShutdown - it is not the
        // application's lifetime, the primary is.
        try { _bar?.Window.Close(); }
        catch { /* window may already be closed */ }
    }
}
