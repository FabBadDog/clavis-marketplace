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

    // Each kind's declared cardinality, learned from the placement messages, so a Toggle (which carries only
    // a kind) can apply the same rule an Open does without the host subscribing to registrations itself.
    private readonly Dictionary<string, string> _kindCardinality = new(StringComparer.Ordinal);

    // Which workspace each live panel instance belongs to, so a one-per-workspace kind can be found within
    // its own workspace rather than application-wide. Everything is Guid.Empty until workspaces exist, which
    // is exactly the previous application-wide behaviour.
    private readonly Dictionary<Guid, Guid> _panelWorkspace = [];

    // The persisted layout lives under this plugin's id in the Configuration plugin (the WpfHost section of state.yaml).
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

    // Whether a saved layout was actually applied, and whether the configured default panels were opened in
    // its place. Both one-shot: the defaults seed a first run only, and never override a saved layout.
    private bool _layoutApplied;
    private bool _defaultsOpened;

    // The workspace whose panels are on screen, and the layout as last read from disk. The layout is kept so
    // a capture can carry over the arrangements of workspaces that are not currently shown - otherwise
    // switching away from a workspace and saving would erase what it had.
    private Guid _activeWorkspaceId;
    private PersistedLayout? _restoredLayout;

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
