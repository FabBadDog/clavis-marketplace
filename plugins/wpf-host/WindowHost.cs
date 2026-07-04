using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// One application window: its chrome (title bar, and for the primary window the prompt input, status
/// bar, and side panel), a per-window RegionManager for region contributions, and a DockingSurface that
/// tiles panels. The primary window seeds the surface with the conversation as its first panel so the
/// existing main-content region keeps driving the chat unchanged.
internal sealed partial class WindowHost
{
    // Stable id for the conversation panel so layout restore can recognise and re-seed it.
    public static readonly Guid ConversationPanelId = new("0B6A1E00-0000-4000-8000-000000000001");

    private readonly IBus _bus;
    private readonly WpfHostConfig _config;
    private readonly KeymapInput _keymap;
    private readonly Func<bool> _isPermissionPending;
    private readonly ContentPresenter _conversationContent = new();
    private readonly ShortcutHelpOverlay _helpOverlay = new();
    private readonly Dictionary<string, SlideInHost> _slideHosts = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, SlideInEntry> _slideIns = [];
    private FrameworkElement? _conversationPanelView;
    private InputHandler? _inputHandler;
    private TextBox? _inputBox;
    private Border? _inputRow;
    private Border? _statusRow;
    private FocusVisualController? _focusVisual;
    private PanelTitleController? _panelTitle;
    private ActivePanelWatcher? _activePanel;
    private FrameworkElement? _statusDot;
    private SolidColorBrush? _borderBrush;

    // The contextual left title is a chat branch strip (the region presenter the conversation contributes
    // into) overlaid with a panel-title TextBlock; PanelTitleController cross-fades between them per active
    // panel. Built once per window.
    private static (Grid Zone, ContentPresenter Branch, TextBlock Title) BuildTitleZone()
    {
        var branch = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Opacity = 0, VerticalAlignment = VerticalAlignment.Center, FontSize = 9.5 };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var zone = new Grid();
        zone.Children.Add(branch);
        zone.Children.Add(title);
        return (zone, branch, title);
    }

    /// Drives Tab / Shift+Tab across all windows. Set by the WindowManager after every window exists, so a
    /// boundary in one window can hand focus to the next.
    public FocusTraversal? FocusTraversal { get; set; }

    private readonly record struct SlideInEntry(string Edge, FrameworkElement View, string Title, string Kind);

    public WindowHost(
        IBus bus, WpfHostConfig config, KeymapInput keymap, Func<bool> isPermissionPending,
        Guid windowId, bool isPrimary)
    {
        _bus = bus;
        _config = config;
        _keymap = keymap;
        _isPermissionPending = isPermissionPending;
        WindowId = windowId;
        IsPrimary = isPrimary;
        Regions = new RegionManager();
        Surface = new DockingSurface();

        // A window's sole panel renders chromeless (no panel tab), so the surface shows no handle for it; the
        // primary window additionally forbids closing or dragging out that last panel (IsSolePanelLocked), so
        // the main window always keeps a panel. A panel sharing the surface with others keeps its tab/handle.
        Surface.PanelCloseRequested += (_, panelId) => PanelCloseRequested?.Invoke(this, panelId);

        Window = ResourceLoader.Load<Window>("Views/MainWindow.xaml");

        // The primary window is opaque: a layered (AllowsTransparency) window is far slower on its first
        // paint, and the primary's only use for transparency was the startup splash crossfade, which is
        // dropped for a faster first paint. Set before the window is sourced (Show), since AllowsTransparency
        // is fixed once the HWND exists. Secondary windows keep transparency so they still fade in and out.
        // (Confirmed by pixel comparison this is not the cause of the title-bar line-rendering artifact -
        // kept opaque for its measured first-paint win regardless.)
        if (isPrimary)
        {
            Window.AllowsTransparency = false;
        }

        Window.Width = config.DefaultWidth;
        Window.Height = config.DefaultHeight;
        Window.MinWidth = config.MinWidth;
        Window.MinHeight = config.MinHeight;
        WorkAreaMaximize.Constrain(Window);

        var contentControl = (ContentControl)Window.Content;
        contentControl.Content = isPrimary ? BuildPrimaryLayout() : BuildSecondaryLayout();

        // Resolve key bindings at the window level so application-scope shortcuts (Ctrl+Shift+P etc.) keep
        // working as long as a Clavis window is focused - even after the focused panel is closed and focus
        // falls back to the window itself, which a handler on an inner layout element would miss.
        Window.PreviewKeyDown += OnKeyDown;

        // A bare-button click acts without stealing the focus ring (only tabbing and text/selection clicks
        // move focus). Tunnels at the window so it sees the click before the button focuses itself.
        Window.PreviewMouseLeftButtonDown += OnButtonClickPreserveFocus;

        EnableWindowLevelDrop((FrameworkElement)contentControl.Content);

        // A panel dropped into an edge slide zone is lifted off the surface and re-hosted as a slide-in.
        Surface.SlideInRequested += (_, request) => MakeSlideIn(request);

        // Slide-ins are transient: when the window loses OS focus they slide away.
        Window.Deactivated += (_, _) => HideSlideIns();

        SetupWindowActiveVisuals();

        Window.Loaded += (_, _) =>
        {
            var root = (FrameworkElement)contentControl.Content;
            root.LayoutTransform = new ScaleTransform(config.UiScaleFactor, config.UiScaleFactor);
        };
    }

    public Guid WindowId { get; }

    public bool IsPrimary { get; }

    public Window Window { get; }

    public RegionManager Regions { get; }

    public DockingSurface Surface { get; }

    public event EventHandler<Guid>? PanelCloseRequested;

    /// Raised when a docked panel is lifted into an edge slide-in, so the manager can remember that this
    /// kind was last placed as a slide-in on this edge in this window.
    public event EventHandler<(Guid InstanceId, string Kind, string Edge)>? SlideInMade;

    // A slide-in's handle drives the same cross-window drag/drop and close paths a docked panel's tab does.
    // These aggregate the four edge hosts' events so the manager wires one source per window, mirroring the
    // surface's DragMoving / DragFellThrough / DragCompleted and PanelCloseRequested.
    public event EventHandler<Point>? SlideInDragMoving;
    public event EventHandler<DragFellThrough>? SlideInDragFellThrough;
    public event EventHandler? SlideInDragCompleted;
    public event EventHandler<Guid>? SlideInCloseRequested;

    public void Focus() => _inputHandler?.Focus();

    public void FocusSurface() =>
        Surface.Dispatcher.BeginInvoke(
            () => Surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            System.Windows.Threading.DispatcherPriority.Input);

    /// The conversation panel's full view: the chat history (the main-content region presenter the
    /// Conversation plugin fills) with the status bar and prompt input docked at its bottom. The docking
    /// surface hosts this as the conversation panel, so input and status travel and close with the chat.
    public FrameworkElement ConversationPanelView => _conversationPanelView!;

    /// Shows or collapses the prompt input. It starts collapsed and slides in on the first
    /// PromptInputAvailability from the conversation owner, taking focus so typing can begin at once.
    public void SetPromptInputVisible(bool visible)
    {
        if (_inputRow is null)
        {
            return;
        }

        if (visible && _inputRow.Visibility != Visibility.Visible)
        {
            _inputRow.Visibility = Visibility.Visible;
            Motion.enter(_inputRow);
            _inputHandler?.Focus();
        }
        else if (!visible)
        {
            _inputRow.Visibility = Visibility.Collapsed;
        }
    }

    /// Shows or collapses the status row. The active panel's owner reports whether its status bar has any
    /// configured content; an empty bar is collapsed so the panel fills the whole space rather than showing a
    /// bare strip, and reappears (with an entrance) when content returns.
    public void SetStatusBarVisible(bool visible)
    {
        if (_statusRow is null)
        {
            return;
        }

        if (visible && _statusRow.Visibility != Visibility.Visible)
        {
            _statusRow.Visibility = Visibility.Visible;
            Motion.enter(_statusRow);
        }
        else if (!visible)
        {
            _statusRow.Visibility = Visibility.Collapsed;
        }
    }

    /// Add the conversation as a panel in the surface, or focus it if already present. Called when starting
    /// fresh (no saved layout) and when re-opening a closed chat; during restore the saved layout places
    /// the conversation slot instead.
    public void SeedConversation()
    {
        if (Surface.PanelIds.Contains(ConversationPanelId))
        {
            Surface.FocusPanel(ConversationPanelId);
            _inputHandler?.Focus();
            return;
        }

        Surface.AddPanel(ConversationPanelId, "conversation", "Chat", ConversationPanelView, DockTarget.IntoActiveGroup);
        _inputHandler?.Focus();
    }

    private FrameworkElement BuildPrimaryLayout()
    {
        // titleText (the overlay TextBlock) is unused in the primary window: its title-bar-left shows the
        // Conversation's strip, which the active-panel watcher drives. It still serves secondary windows.
        var (titleBarLeft, titleBranch, _) = BuildTitleZone();
        var titleBarRight = new ContentPresenter();
        var statusBar = new ContentPresenter();
        var statusBarRight = new ContentPresenter();

        Regions.DefineRegion("main-content", _conversationContent);
        Regions.DefineRegion("title-bar-left", titleBranch);
        Regions.DefineRegion("title-bar-right", titleBarRight);
        Regions.DefineRegion("status-bar", statusBar);
        Regions.DefineRegion("status-bar-right", statusBarRight);

        var inputBox = WindowChromeViews.CreateInputBox();
        _inputBox = inputBox;
        _inputHandler = new InputHandler(_bus, inputBox);

        var (titleBar, statusDot) = WindowChromeViews.CreateTitleBar(titleBarLeft, titleBarRight, () => Window.Close());
        _statusDot = statusDot;
        // The primary window's title/status bars are owned by the window but driven by the active docked
        // panel: the watcher announces the active kind and the Conversation re-templates the chrome strips it
        // contributes here. (Secondary windows have no status bar and use PanelTitleController for the title.)
        _activePanel = new ActivePanelWatcher(_bus, Window, Surface);
        var inputRow = WindowChromeViews.CreateInputRow(inputBox);
        var statusRow = WindowChromeViews.CreateStatusBar(statusBar, statusBarRight);
        _statusRow = statusRow;

        // The prompt input starts collapsed: it only appears (SetPromptInputVisible) once the
        // conversation owner reports an agent session that can accept prompts, so the user is never
        // offered an input that leads nowhere while the agent infrastructure is still coming up.
        inputRow.Visibility = Visibility.Collapsed;
        _inputRow = inputRow;

        // The conversation is a self-contained panel: the chat history fills it and the prompt input floats
        // (translucent) over its bottom edge, so the input can grow up over the chat without pushing it, and
        // it travels and closes with the chat when other panels share the window. The status bar is NOT here:
        // it is window chrome (a fixed bottom row below), so it stays put and shows for every active panel.
        inputRow.VerticalAlignment = VerticalAlignment.Bottom;

        var conversationPanel = new Grid();
        conversationPanel.Children.Add(_conversationContent);
        conversationPanel.Children.Add(inputRow);
        _conversationPanelView = conversationPanel;

        // The input's top framing line turns clavis while it is focused (its focus cue), and it grows with its
        // content up to 60% of the chat height.
        WireInputFocusLines(inputBox, inputRow);
        CapInputHeightToChat(inputBox, conversationPanel);

        // The status bar is a fixed window-chrome row pinned to the window bottom; the active docked panel
        // drives its content (window owns the bar, the active panel owns what it shows), so it is window-owned
        // and visible for every panel, not just the chat.
        // The body cell holds the docking surface plus the edge slide-ins layered over it, confined to the
        // row between the title bar and status bar. Slide-ins anchor flush with their host's own edge
        // (VerticalAlignment.Top/Stretch etc.), so hosting them at the window's own top-level cell put a
        // top/left/right slide-in's hover handle flush with the window's top edge too - inside the
        // WindowChrome caption band (CaptionHeight="28" in MainWindow.xaml), where the OS claims mouse
        // hit-testing for window-drag before it reaches the handle, unless the handle opts in via
        // WindowChrome.IsHitTestVisibleInChrome (only the window close button does). That made the handle's
        // reveal/hide flicker exactly at the hover band - reachable from below, but unreachable once the
        // cursor actually reached it. Confining the body to the row below the title bar keeps every slide-in
        // (and its handle) entirely inside ordinary client area.
        var bodyGrid = new Grid();
        bodyGrid.Children.Add(Surface);

        var chromePanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        chromePanel.Children.Add(titleBar);
        DockPanel.SetDock(statusRow, Dock.Bottom);
        chromePanel.Children.Add(statusRow);
        chromePanel.Children.Add(bodyGrid);

        // A single-cell grid layering the chrome and the help overlay. Panels are tiled by the docking
        // surface inside the chrome, so the window owns no fixed panel column.
        var layoutGrid = new Grid();
        layoutGrid.Children.Add(chromePanel);
        layoutGrid.Children.Add(_helpOverlay);

        AttachSlideHosts(bodyGrid, layoutGrid);
        _focusVisual = new FocusVisualController(Window, layoutGrid, Surface, inputBox);

        layoutGrid.Loaded += (_, _) => _inputHandler.Focus();

        return layoutGrid;
    }

    private FrameworkElement BuildSecondaryLayout()
    {
        var (titleBarLeft, titleBranch, titleText) = BuildTitleZone();
        var titleBarRight = new ContentPresenter();
        Regions.DefineRegion("title-bar-left", titleBranch);
        Regions.DefineRegion("title-bar-right", titleBarRight);

        var (titleBar, statusDot) = WindowChromeViews.CreateTitleBar(titleBarLeft, titleBarRight, CloseWithFade);
        _statusDot = statusDot;
        _panelTitle = new PanelTitleController(Window, Surface, titleBranch, titleText);

        // The body cell holds the docking surface plus the edge slide-ins layered over it, confined below
        // the title bar so a slide-in's hover handle never lands inside the window's WindowChrome caption
        // band (see the matching comment in BuildPrimaryLayout for why that mattered).
        var bodyGrid = new Grid();
        bodyGrid.Children.Add(Surface);

        var dockPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        dockPanel.Children.Add(titleBar);
        dockPanel.Children.Add(bodyGrid);

        // Focusable so the window-level key resolver still receives shortcuts (Ctrl+W, etc.) when nothing
        // else holds focus, but never a Tab stop: traversal must not land on the bare window body, only on
        // the panels' interactive controls. A panel-host window with no interactive control is skipped.
        var layers = new Grid { Focusable = true };
        KeyboardNavigation.SetIsTabStop(layers, false);
        layers.Children.Add(dockPanel);
        layers.Children.Add(_helpOverlay);

        AttachSlideHosts(bodyGrid, layers);
        _focusVisual = new FocusVisualController(Window, layers, Surface, null);

        // A secondary window opens with nothing focused. Take keyboard focus once shown so the window-level
        // PreviewKeyDown resolver sees key presses and its shortcuts (Ctrl+W to close, etc.) stay alive.
        layers.Loaded += (_, _) => layers.Focus();

        return layers;
    }

    /// The single key resolver for the window. Preview tunnels root-first, so panel bindings can win over
    /// application/system before the event reaches the focused panel. Builds the canonical gesture,
    /// resolves it against the keymap for the focused panel context, and dispatches the bound command.
    /// Intrinsic text-editing keys (no Ctrl/Win) are left for a focused text input.
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (TryHandlePermissionKeys(key, e))
        {
            return;
        }

        // Esc dismisses an open keyboard-help overlay before any binding sees the key, so the overlay closes
        // with Esc like every other transient surface (it is opened by a toggle and was otherwise un-closeable
        // by keyboard).
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && _helpOverlay.IsOpen)
        {
            _helpOverlay.Hide();
            e.Handled = true;
            return;
        }

        // Esc dismisses any open slide-in (per the slide-in rule: Esc, focus moving to another panel/window,
        // or a click outside). Handled before binding resolution so it works while a text input inside the
        // slide-in holds focus, and before Tab so it is not shadowed by traversal.
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None
            && _slideHosts.Values.Any(host => host.IsOpen))
        {
            HideSlideIns();
            e.Handled = true;
            return;
        }

        if (TryHandleTab(key, e))
        {
            return;
        }

        var gesture = KeyGestureReader.canonical(Keyboard.Modifiers, key);
        if (gesture.Length == 0)
        {
            return;
        }

        var (panelKind, panelInstanceId) = FocusedPanelContext();
        var binding = _keymap.Resolve(gesture, panelKind);
        if (binding is null)
        {
            return;
        }

        // A panel-scoped binding's SCOPE only decides when it applies (which panel must be focused); the
        // COMMAND decides how it runs. A panel-local command (the events filters, the chat's scroll) goes to
        // the focused panel instance; everything else - including a general host command like
        // CloseActivePanel bound only while a panel is focused - goes through the palette router.
        var isPanelLocal = binding.Scope == KeymapScope.Panel && _keymap.IsPanelLocalCommand(binding.Command);

        // A focused text input keeps text-producing (plain or Shift-only) gestures for editing - unless the
        // gesture is bound to a panel-local command for the focused panel (e.g. the conversation's Ctrl+Up/
        // Down scroll), which takes precedence over caret movement.
        if (IsTextInputFocused() && !KeyGestureReader.isTextSafe(Keyboard.Modifiers) && !isPanelLocal)
        {
            return;
        }

        if (isPanelLocal)
        {
            _bus.Send(new RunPanelCommand(panelInstanceId, binding.Command));
        }
        else
        {
            _bus.Send(new RunCommand(binding.Command));
        }

        e.Handled = true;
    }

    // While a permission prompt is awaiting a decision it owns the bare Left/Right (move the choice) and
    // Enter (confirm) keys - even when the chat input holds focus - but it never takes tab focus. Routed
    // here, ahead of keymap resolution and the text-input passthrough, so it beats the events panel's
    // Left/Right cycle and the input box's Enter-to-submit. The host knows only a pending bool; the
    // Conversation plugin owns the actual selection and resolution.
    private bool TryHandlePermissionKeys(Key key, KeyEventArgs e)
    {
        if (!_isPermissionPending() || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        switch (key)
        {
            case Key.Left:
                _bus.Send(new UserNavigatedPermission(-1));
                break;
            case Key.Right:
                _bus.Send(new UserNavigatedPermission(1));
                break;
            case Key.Enter:
                _bus.Send(new UserConfirmedPermission());
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    /// Register the window's top-level content as an OLE drop target. The docking surface attaches its own
    /// per-group drop targets, but those live on elements that the surface rebuilds; an owned, transparent
    /// secondary window can fail to register as a drop target at all, so a drag onto it shows the no-drop
    /// cursor before the surface ever sees it. A persistent AllowDrop element at the window root forces the
    /// HWND to register from the moment it is shown; it sets the Move effect for our panel format (so no
    /// stop sign over chrome) without handling the event, leaving the surface to drive the actual docking.
    private void EnableWindowLevelDrop(FrameworkElement root)
    {
        root.AllowDrop = true;

        root.PreviewDragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DockingSurface.DragFormat))
            {
                e.Effects = DragDropEffects.Move;
            }
        };
    }

    /// Create the four edge slide-in overlays and layer them over hostGrid (the window body - the row
    /// between the title bar and status bar, not the whole window, so a slide-in's hover handle never lands
    /// inside the title bar's WindowChrome caption band). Each overlays the whole body and parks off-screen
    /// until shown. Click-away auto-hide is wired against clickAwayRoot (the outer layout cell spanning the
    /// whole window) so a click on the title bar or status bar also dismisses an open slide-in.
    private void AttachSlideHosts(Grid hostGrid, Grid clickAwayRoot)
    {
        foreach (var edge in (string[])["left", "right", "top", "bottom"])
        {
            var host = new SlideInHost(edge);
            _slideHosts[edge] = host;
            host.DragMoving += (_, point) => SlideInDragMoving?.Invoke(this, point);
            host.DragFellThrough += (_, fell) => SlideInDragFellThrough?.Invoke(this, fell);
            host.DragCompleted += (_, _) => SlideInDragCompleted?.Invoke(this, EventArgs.Empty);
            host.CloseRequested += (_, panelId) => SlideInCloseRequested?.Invoke(this, panelId);
            hostGrid.Children.Add(host);
        }

        // Clicking anywhere that is not inside an open slide-in dismisses the open ones (their content
        // slides away when focus moves off them).
        clickAwayRoot.PreviewMouseDown += (_, e) =>
        {
            if (e.OriginalSource is Visual source
                && _slideHosts.Values.Any(host => host.IsOpen && host.IsAncestorOf(source)))
            {
                return;
            }

            HideSlideIns();
        };
    }

    /// Lift a panel off the surface and register it as a slide-in anchored to the requested edge, then show
    /// it. The host broadcasts SlideInRegistered so the palette can offer a summon command for it.
    private void MakeSlideIn(SlideInRequest request)
    {
        var transfer = Surface.TryTakePanel(request.PanelId);
        if (transfer is null)
        {
            return;
        }

        _slideIns[request.PanelId] = new SlideInEntry(request.Edge, transfer.View, transfer.Slot.Title, transfer.Slot.PanelKind);
        _bus.Send(new SlideInRegistered(request.PanelId, transfer.Slot.Title));
        SlideInMade?.Invoke(this, (request.PanelId, transfer.Slot.PanelKind, request.Edge));
        ShowSlideIn(request.PanelId);
    }

    /// Re-host an already-built panel view directly as an edge slide-in (no docked-tab intermediate), used
    /// when re-opening a panel whose remembered placement was a slide-in. Mirrors MakeSlideIn's bookkeeping.
    /// Restoring a saved layout passes show: false so the slide-in returns parked on its edge (summonable),
    /// not popped open over the window on every launch.
    public void AddSlideIn(Guid instanceId, string kind, string title, FrameworkElement view, string edge, bool show = true)
    {
        _slideIns[instanceId] = new SlideInEntry(edge, view, title, kind);
        _bus.Send(new SlideInRegistered(instanceId, title));
        if (show)
        {
            ShowSlideIn(instanceId);
        }
    }

    public bool HasSlideIn(Guid instanceId) => _slideIns.ContainsKey(instanceId);

    /// Lift a panel out of its slide-in - detaching its live view so it can be re-parented - and return the
    /// transfer, or null when this window has no such slide-in. The panel is no longer a slide-in (the manager
    /// then either re-docks the transfer or, for a close, drops it and announces PanelClosed).
    public PanelTransfer? TryTakeSlideIn(Guid instanceId)
    {
        if (!_slideIns.Remove(instanceId, out var entry))
        {
            return null;
        }

        if (_slideHosts.TryGetValue(entry.Edge, out var edgeHost) && ReferenceEquals(edgeHost.View, entry.View))
        {
            edgeHost.Hide();
        }

        // Detach the view from the slide-in's content site so a docking surface can adopt it without a
        // duplicate-parent error.
        if (entry.View.Parent is ContentControl parent)
        {
            parent.Content = null;
        }

        _bus.Send(new SlideInClosed(instanceId));
        return new PanelTransfer(new PanelSlot(instanceId, entry.Kind, entry.Title, ""), entry.View);
    }

    /// Lift a panel out of this window wherever it lives - a docked slot or an edge slide-in - so the
    /// cross-window move sites treat both uniformly.
    public PanelTransfer? TakePanel(Guid instanceId) =>
        Surface.PanelIds.Contains(instanceId) ? Surface.TryTakePanel(instanceId) : TryTakeSlideIn(instanceId);

    /// True when this window hosts the panel, docked or as a slide-in.
    public bool OwnsPanel(Guid instanceId) => Surface.PanelIds.Contains(instanceId) || HasSlideIn(instanceId);

    public IReadOnlyCollection<Guid> SlideInIds => _slideIns.Keys;

    /// The (instance, kind) pairs currently anchored as slide-ins in this window, so the manager can find a
    /// live slide-in of a given kind to reveal.
    public IEnumerable<(Guid InstanceId, string Kind)> SlideInInstances =>
        _slideIns.Select(entry => (entry.Key, entry.Value.Kind));

    /// Slide-in detail for the workspace snapshot: instance, kind, tab title, and whether it is shown.
    public IEnumerable<(Guid InstanceId, string Kind, string Title, bool IsOpen)> SlideInDetails =>
        _slideIns.Select(entry => (entry.Key, entry.Value.Kind, entry.Value.Title, IsSlideInOpen(entry.Key)));

    /// Slide-in layout for persistence: instance, kind, title, and the edge it is anchored to, so a saved
    /// workspace can re-create the slide-in on the same edge of the same window.
    public IEnumerable<(Guid InstanceId, string Kind, string Title, string Edge)> SlideInLayouts =>
        _slideIns.Select(entry => (entry.Key, entry.Value.Kind, entry.Value.Title, entry.Value.Edge));

    /// Slide a registered slide-in in from its edge. Any open slide-in on a perpendicular edge is hidden
    /// first (they would overlap in a corner); an open slide-in on the opposite edge stays, so a left/right
    /// or top/bottom pair can be visible together.
    public void ShowSlideIn(Guid instanceId)
    {
        if (!_slideIns.TryGetValue(instanceId, out var entry)
            || !_slideHosts.TryGetValue(entry.Edge, out var host))
        {
            return;
        }

        foreach (var other in _slideHosts.Values)
        {
            if (other.IsOpen && other.Edge != entry.Edge && other.Edge != Opposite(entry.Edge))
            {
                other.Hide();
            }
        }

        host.SetContent(instanceId, entry.Title, entry.View);
        host.Open();
    }

    private void HideSlideIns()
    {
        foreach (var host in _slideHosts.Values)
        {
            host.Hide();
        }
    }

    private static string Opposite(string edge) => edge switch
    {
        "left" => "right",
        "right" => "left",
        "top" => "bottom",
        _ => "top"
    };

    /// The kind and instance of the panel that currently holds keyboard focus: a docking-surface panel
    /// (kind + instance id) or a tagged region view such as the events side panel (kind, no instance).
    private (string Kind, Guid InstanceId) FocusedPanelContext()
    {
        var node = Keyboard.FocusedElement as DependencyObject;
        while (node is not null)
        {
            if (ReferenceEquals(node, Surface))
            {
                return (Surface.ActivePanelKind, Surface.ActivePanelId);
            }

            if (node is FrameworkElement element && element.Tag is string tag && tag.Length > 0)
            {
                return (tag, Guid.Empty);
            }

            node = ElementTree.ParentOf(node);
        }

        return ("", Guid.Empty);
    }

    private static bool IsTextInputFocused() =>
        Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox;

    /// Show or hide this window's keyboard-help overlay, populated with the merged bindings for the
    /// currently focused panel context.
    public void ToggleHelp()
    {
        _helpOverlay.SetRows(_keymap.BuildHelpRows(FocusedPanelContext().Kind));
        _helpOverlay.Toggle();
    }

    /// True when the named slide-in is currently shown (its edge host is open and displaying that instance's
    /// view). Lets the toggle decide between revealing and hiding it.
    public bool IsSlideInOpen(Guid instanceId) =>
        _slideIns.TryGetValue(instanceId, out var entry)
        && _slideHosts.TryGetValue(entry.Edge, out var host)
        && host.IsOpen
        && ReferenceEquals(host.View, entry.View);

    /// Hide a specific shown slide-in (no-op if it is not the one currently displayed on its edge).
    public void HideSlideIn(Guid instanceId)
    {
        if (_slideIns.TryGetValue(instanceId, out var entry)
            && _slideHosts.TryGetValue(entry.Edge, out var host)
            && ReferenceEquals(host.View, entry.View))
        {
            host.Hide();
        }
    }

    /// Dismiss any open slide-ins, reporting whether one was actually open. Used by the focused-panel close:
    /// an open slide-in is banished before the docked active panel is considered.
    public bool DismissOpenSlideIns()
    {
        var any = _slideHosts.Values.Any(host => host.IsOpen);
        HideSlideIns();
        return any;
    }

    /// True when this window's only panel is the locked one - the primary window's last remaining panel,
    /// which fills the window and cannot be closed or dragged out so the main window is never empty.
    public bool IsSolePanelLocked => IsPrimary && Surface.PanelIds.Count() <= 1;

    /// Close the focused docked panel - the one in the active tab group - unless it is the primary window's
    /// locked sole panel. Reports whether a panel was closed so the caller can finish the bookkeeping
    /// (announce PanelClosed, persist).
    public Guid CloseActiveDockedPanel()
    {
        var activeId = Surface.ActivePanelId;
        if (activeId == Guid.Empty || IsSolePanelLocked)
        {
            return Guid.Empty;
        }

        Surface.RemovePanel(activeId);
        return activeId;
    }

    // Fade the window out, then close it for real - the secondary-window counterpart of the manager's
    // CloseSecondaryWindow, so closing by the title-bar glyph matches closing by command.
    private void CloseWithFade() => Motion.fadeWindow(Window, 0.0, Window.Close);
}
