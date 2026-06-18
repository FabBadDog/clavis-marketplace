namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Documents
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation

/// A panel occupying one tab slot. SavedState is the panel's opaque per-instance blob; the docking
/// surface only carries the structure, so it leaves SavedState empty - the host owns the authoritative
/// state and folds it back in at persistence time.
[<CLIMutable>]
type PanelSlot =
    { PanelId: Guid
      PanelKind: string
      Title: string
      SavedState: string }

/// The serializable docking layout: a recursive tree where a leaf is a tab group and a split divides
/// space between children. Modelled as a flat record with a Kind discriminator (not an F# DU) so
/// System.Text.Json round-trips it directly. [<CLIMutable>] gives the parameterless constructor +
/// settable properties the serializer needs.
[<CLIMutable>]
type LayoutNode =
    { Kind: string
      GroupId: Guid
      Orientation: string
      Sizes: float[]
      Children: LayoutNode[]
      Panels: PanelSlot[]
      ActiveIndex: int }

type DockDirection =
    | Left
    | Right
    | Top
    | Bottom

/// Where to place a new panel relative to the existing layout.
type DockTarget =
    | IntoActiveGroup
    | IntoGroup of groupId: Guid
    | SplitGroup of groupId: Guid * direction: DockDirection * relativeSize: float

/// The outcome of a drag-to-dock hovering over a group: a tab (centre), a split (mid-edge band), or a
/// slide-in (outer-edge band). The slide band sits inside the split band on each edge, so the four edges
/// each offer split then slide as the cursor nears the border.
type DropZone =
    | DropTab
    | DropSplit of DockDirection
    | DropSlide of DockDirection

/// A panel lifted out of one docking surface so another can adopt it (cross-window drag). Carries the
/// slot metadata and the live view element, so the target surface re-hosts the same view rather than
/// re-materialising the panel. AllowNullLiteral lets the source surface return null when the panel is
/// not here, which C# reads as a nullable result.
[<AllowNullLiteral>]
type PanelTransfer(slot: PanelSlot, view: FrameworkElement) =
    member _.Slot = slot
    member _.View = view

/// Raised when a panel dragged from a different surface is dropped onto this one. The host listens and
/// moves the panel across windows (the surface alone cannot reach the panel's current owner).
type ExternalPanelDrop(panelId: Guid, target: DockTarget) =
    member _.PanelId = panelId
    member _.Target = target

/// Raised when a drag started on this surface ends without any window accepting the OLE drop (the result
/// is None). An owned, transparent secondary window never registers as a drop target, so a drag onto it
/// is rejected by the OS. The host resolves the window under the cursor by screen point and moves the
/// panel there - the fallback that makes cross-window docking work despite the OLE registration gap.
type DragFellThrough(panelId: Guid, screenPoint: Point) =
    member _.PanelId = panelId
    member _.ScreenPoint = screenPoint

/// Raised when a panel is dropped into an edge's slide zone. The host lifts the panel off the surface and
/// re-hosts it as an edge-anchored slide-in. Edge is "left", "right", "top", or "bottom".
type SlideInRequest(panelId: Guid, edge: string) =
    member _.PanelId = panelId
    member _.Edge = edge

/// The cursor's screen position in physical pixels. Captured the instant a drag ends so the host can map
/// it to whichever window sits under the pointer; WPF exposes no managed screen-cursor query.
[<RequireQualifiedAccess>]
[<ExcludeFromCodeCoverage>] // thin P/Invoke wrapper
module private NativeCursor =

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private NativePoint =
        val mutable X: int
        val mutable Y: int

    [<DllImport("user32.dll")>]
    extern bool private GetCursorPos(NativePoint& point)

    let position () =
        let mutable point = NativePoint()
        GetCursorPos(&point) |> ignore
        Point(float point.X, float point.Y)

[<RequireQualifiedAccess>]
module DockingModel =

    [<Literal>]
    let Split = "split"

    [<Literal>]
    let Leaf = "leaf"

    [<Literal>]
    let Horizontal = "horizontal"

    [<Literal>]
    let Vertical = "vertical"

    let isLeaf (node: LayoutNode) =
        node.Kind = Leaf

    let leaf groupId panels activeIndex =
        { Kind = Leaf
          GroupId = groupId
          Orientation = ""
          Sizes = [||]
          Children = [||]
          Panels = panels
          ActiveIndex = activeIndex }

    let split groupId orientation sizes children =
        { Kind = Split
          GroupId = groupId
          Orientation = orientation
          Sizes = sizes
          Children = children
          Panels = [||]
          ActiveIndex = 0 }

    let emptyLeaf groupId =
        leaf groupId [||] 0

    let rec firstLeaf node =
        if isLeaf node then
            Some node
        else
            node.Children |> Array.tryPick firstLeaf

    let rec slots node =
        if isLeaf node then
            node.Panels |> Array.toList
        else
            node.Children |> Array.toList |> List.collect slots

    let private orientationOf direction =
        match direction with
        | Left
        | Right -> Horizontal
        | Top
        | Bottom -> Vertical

    let rec private mapNode update node =
        if isLeaf node then
            update node
        else
            { node with Children = node.Children |> Array.map (mapNode update) }

    let private appendSlot groupId slot node =
        if isLeaf node && node.GroupId = groupId then
            { node with
                Panels = Array.append node.Panels [| slot |]
                ActiveIndex = node.Panels.Length }
        else
            node

    let private splitLeaf groupId direction relativeSize newGroupId slot node =
        if isLeaf node && node.GroupId = groupId then
            let inserted = leaf newGroupId [| slot |] 0
            let children, sizes =
                match direction with
                | Left
                | Top -> [| inserted; node |], [| relativeSize; 1.0 - relativeSize |]
                | Right
                | Bottom -> [| node; inserted |], [| 1.0 - relativeSize; relativeSize |]

            split (Guid.NewGuid()) (orientationOf direction) sizes children
        else
            node

    /// Add a panel to the layout. `activeGroupId` resolves IntoActiveGroup; `newGroupId` names the leaf
    /// created by a split. Returns the new tree (the input is never mutated).
    let addPanel target activeGroupId newGroupId slot root =
        match target with
        | IntoActiveGroup -> mapNode (appendSlot activeGroupId slot) root
        | IntoGroup groupId -> mapNode (appendSlot groupId slot) root
        | SplitGroup (groupId, direction, relativeSize) ->
            mapNode (splitLeaf groupId direction relativeSize newGroupId slot) root

    let setActiveIndex groupId index root =
        let update node =
            if node.GroupId = groupId then
                { node with ActiveIndex = index }
            else
                node

        mapNode update root

    let setSizes splitGroupId sizes root =
        let rec apply node =
            if isLeaf node then
                node
            elif node.GroupId = splitGroupId then
                { node with
                    Sizes = sizes
                    Children = node.Children |> Array.map apply }
            else
                { node with Children = node.Children |> Array.map apply }

        apply root

    let private removeSlot panelId node =
        if isLeaf node then
            let remaining = node.Panels |> Array.filter (fun panel -> panel.PanelId <> panelId)
            let activeIndex =
                if node.ActiveIndex >= remaining.Length then
                    max 0 (remaining.Length - 1)
                else
                    node.ActiveIndex

            { node with
                Panels = remaining
                ActiveIndex = activeIndex }
        else
            node

    /// Collapse empty leaves out of splits, and replace any single-child split with its child. A fully
    /// emptied tree collapses to a single empty leaf keeping the original root group id.
    let rec private collapse node =
        if isLeaf node then
            node
        else
            let kept =
                node.Children
                |> Array.map collapse
                |> Array.filter (fun child -> not (isLeaf child && child.Panels.Length = 0))

            match kept.Length with
            | 0 -> emptyLeaf node.GroupId
            | 1 -> kept[0]
            | count ->
                let sizes =
                    if node.Sizes.Length = count then
                        node.Sizes
                    else
                        Array.create count (1.0 / float count)

                { node with Children = kept; Sizes = sizes }

    let removePanel panelId root =
        root |> mapNode (removeSlot panelId) |> collapse

    let rec groupContaining panelId node =
        if isLeaf node then
            if node.Panels |> Array.exists (fun panel -> panel.PanelId = panelId) then
                Some node.GroupId
            else
                None
        else
            node.Children |> Array.tryPick (groupContaining panelId)

    let rec findGroup groupId node =
        if isLeaf node then
            if node.GroupId = groupId then Some node else None
        else
            node.Children |> Array.tryPick (findGroup groupId)

    let findSlot panelId node =
        slots node |> List.tryFind (fun slot -> slot.PanelId = panelId)

/// A docked, tiled panel host: renders a recursive split tree of tab groups built from plain WPF
/// (Grid + GridSplitter + TabControl, no custom dependency properties). Structure lives in an immutable
/// LayoutNode; panel views are stored by id and re-hosted on each rebuild. Lives in the Default ALC
/// (this module is never unloaded) so it may use WPF freely without rooting plugin types.
[<ExcludeFromCodeCoverage>]
type DockingSurface() as this =
    inherit ContentControl()

    let mutable model = DockingModel.emptyLeaf (Guid.NewGuid())
    let panelViews = System.Collections.Generic.Dictionary<Guid, FrameworkElement>()
    let mutable activeGroupId = model.GroupId

    // The panel that just docked in, so the next render animates only that tile's entrance - an unrelated
    // Rebuild leaves every existing tile untouched. Reset to Empty once consumed by RenderModel.
    let mutable pendingEnterPanel = Guid.Empty

    // When set (the host sets it on the primary window), the surface's sole remaining panel is locked: it
    // fills the window with no tab strip, drag handle, or close affordance, so the main window always keeps
    // a panel. Any other configuration - a secondary window, or this surface holding more than one panel -
    // renders normally, with lone panels getting a hover-revealed drag/close handle.
    let mutable lockSolePanel = false

    let layoutChanged = Event<EventHandler, EventArgs>()
    let panelRemoved = Event<EventHandler, EventArgs>()
    let activeTabChanged = Event<EventHandler<Guid>, Guid>()
    let panelCloseRequested = Event<EventHandler<Guid>, Guid>()
    let externalPanelDropped = Event<EventHandler<ExternalPanelDrop>, ExternalPanelDrop>()
    let dragFellThrough = Event<EventHandler<DragFellThrough>, DragFellThrough>()
    let dragMoving = Event<EventHandler<Point>, Point>()
    let dragCompleted = Event<EventHandler, EventArgs>()
    let slideInRequested = Event<EventHandler<SlideInRequest>, SlideInRequest>()

    let edgeName direction =
        match direction with
        | Left -> "left"
        | Right -> "right"
        | Top -> "top"
        | Bottom -> "bottom"

    // The drop targets (tab group / lone-panel host elements) of the current render, paired with their
    // group id. Rebuilt on every render and used to resolve which group sits under a screen point during a
    // cross-window drag - the path that paints the drop hint and docks the panel when the OS never routes
    // a drag-over to this (owned, transparent) window.
    let dropTargets = System.Collections.Generic.List<FrameworkElement * Guid>()

    let splitterBrush = Colors.codeBorder

    // The slim hover handle bar that gives a lone panel its drag/close affordance. Translucent dark so it
    // reads as a thin strip floating over the panel's own content rather than a chrome band.
    let handleBarBrush =
        SolidColorBrush(Color.FromArgb(0xE0uy, 0x14uy, 0x14uy, 0x1Cuy)) |> fun brush -> brush.Freeze(); brush

    // A lone panel's handle reveals only while the cursor is within this top band (or over the handle
    // itself), so the affordance appears where it lives rather than on a hover anywhere in the panel.
    let hoverBandHeight = 24.0

    // A TabControl template that shows only the selected content - no header strip. Used for lone panels so
    // a single panel carries no tab/close chrome while still hosting its view through the content site.
    let headerlessTabTemplate =
        let presenter = FrameworkElementFactory(typeof<ContentPresenter>)
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent")
        ControlTemplate(typeof<TabControl>, VisualTree = presenter)

    // A faint clavis wash for the hovered (unselected) tab, so a tab gives a hover cue without competing
    // with the selected tab's fill.
    let tabHoverBrush =
        SolidColorBrush(Color.FromArgb(0x22uy, 0x9Fuy, 0xD5uy, 0xF0uy)) |> fun brush -> brush.Freeze(); brush

    // A dark, slim tab header replacing the stock (white, tall) TabItem chrome: square, transparent when
    // idle so it sits on the window's own dark surface, with the selected tab raised to the code-surface
    // fill, brightened text, and a thin clavis underline. Lives in the never-unloaded Default ALC, so a
    // shared ControlTemplate roots no plugin types (unlike a static DependencyProperty registration).
    let tabItemTemplate =
        let border = FrameworkElementFactory(typeof<Border>)
        border.Name <- "TabBorder"
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent)
        border.SetValue(Border.BorderThicknessProperty, Thickness(0.0, 0.0, 0.0, 1.5))
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent)
        border.SetValue(Border.PaddingProperty, Thickness(11.0, 3.0, 7.0, 3.0))

        let presenter = FrameworkElementFactory(typeof<ContentPresenter>)
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header")
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center)
        border.AppendChild(presenter)

        let template = ControlTemplate(typeof<TabItem>, VisualTree = border)

        let selected = Trigger(Property = TabItem.IsSelectedProperty, Value = true)
        selected.Setters.Add(Setter(Border.BackgroundProperty, Colors.codeBg, "TabBorder"))
        selected.Setters.Add(Setter(Border.BorderBrushProperty, Colors.clavis, "TabBorder"))
        template.Triggers.Add(selected)

        let hover = Trigger(Property = UIElement.IsMouseOverProperty, Value = true)
        hover.Setters.Add(Setter(Border.BackgroundProperty, tabHoverBrush, "TabBorder"))
        template.Triggers.Add(hover)

        template

    // Title colouring lives on the TabItem itself, not inside the template: the Header content's property
    // inheritance follows its LOGICAL parent (the TabItem), so a foreground set on the template's border
    // never reaches the title TextBlock - it silently fell back to the stock TabItem black, rendering the
    // titles near-invisible on the dark chrome. A style on the TabItem is what the header actually inherits.
    let tabItemStyle =
        let style = Style(typeof<TabItem>)
        style.Setters.Add(Setter(Control.TemplateProperty, tabItemTemplate))
        style.Setters.Add(Setter(Control.ForegroundProperty, Colors.text))
        let selected = Trigger(Property = TabItem.IsSelectedProperty, Value = true)
        selected.Setters.Add(Setter(Control.ForegroundProperty, Colors.textBright))
        style.Triggers.Add(selected)
        style

    [<Literal>]
    let dragFormat = "ClavisPanelId"

    // The rendered tree lives in renderHost; dropHint floats above it (in the overlay grid) to preview a
    // drag-to-dock target zone.
    let renderHost =
        ContentControl(
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch)

    let dropHintLabel =
        TextBlock(
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.0,
            Foreground = Colors.textBright)

    let dropHint =
        Border(
            Background = SolidColorBrush(Color.FromArgb(40uy, 0x9Fuy, 0xD5uy, 0xF0uy)),
            BorderBrush = Colors.clavis,
            BorderThickness = Thickness(1.0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = dropHintLabel)

    let detachView (view: FrameworkElement) =
        match LogicalTreeHelper.GetParent(view) with
        | :? ContentControl as content -> content.Content <- null
        | :? Decorator as decorator -> decorator.Child <- null
        | _ -> ()

    // Tab and lone-panel titles read as upper-case chrome labels (matching the window's CLAVIS caption),
    // so a kind registered as "git log" shows as GIT LOG rather than a bare lower-case word.
    let buildTabHeader (slot: PanelSlot) =
        let title =
            TextBlock(
                Text = slot.Title.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = Thickness(0.0, 0.0, 7.0, 0.0))
        title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")

        let closeButton = CloseButton.create (Action(fun () -> panelCloseRequested.Trigger(this, slot.PanelId)))
        closeButton.VerticalAlignment <- VerticalAlignment.Center

        let header = StackPanel(Orientation = Orientation.Horizontal)
        header.Children.Add(title) |> ignore
        header.Children.Add(closeButton) |> ignore
        header

    // Arm a panel drag on element (a whole tab item or a lone panel's handle), so the gesture starts no
    // matter where on the tab the press lands - the previous header-only handle missed the tab's padding,
    // which is why a drag "only worked on the second try". A re-entrancy guard stops a stray move re-issued
    // mid-drag from starting a second DoDragDrop.
    let attachPanelDrag (element: FrameworkElement) (panelId: Guid) =
        // The OS shows the no-drop cursor whenever no target sets an effect - which is always when dragging
        // over a window that never registered as a drop target. Suppress it so a cross-window drag reads as
        // valid; the host paints the actual drop-zone overlay on the window under the cursor.
        element.GiveFeedback.Add(fun args ->
            if args.Effects = DragDropEffects.None then
                args.UseDefaultCursors <- false
                Mouse.SetCursor(Cursors.Hand) |> ignore
                args.Handled <- true)

        // Fires continuously through the drag (on the source, which always gets these). Each tick reports
        // the cursor's screen position so the host can drive the drop-zone overlay across windows.
        element.QueryContinueDrag.Add(fun _ -> dragMoving.Trigger(this, NativeCursor.position ()))

        let mutable dragOrigin: Point option = None
        let mutable dragging = false
        element.PreviewMouseLeftButtonDown.Add(fun args -> dragOrigin <- Some(args.GetPosition(this)))
        element.PreviewMouseLeftButtonUp.Add(fun _ -> dragOrigin <- None)
        element.PreviewMouseMove.Add(fun args ->
            match dragOrigin with
            | Some origin when args.LeftButton = MouseButtonState.Pressed && not dragging ->
                let current = args.GetPosition(this)
                let moved =
                    abs (current.X - origin.X) > SystemParameters.MinimumHorizontalDragDistance
                    || abs (current.Y - origin.Y) > SystemParameters.MinimumVerticalDragDistance
                if moved then
                    dragOrigin <- None
                    dragging <- true
                    try
                        let result =
                            DragDrop.DoDragDrop(element, DataObject(dragFormat, panelId.ToString()), DragDropEffects.Move)
                        dragCompleted.Trigger(this, EventArgs.Empty)
                        // A None result means no window accepted the OLE drop. If the panel still lives here
                        // it was not moved locally either, so fall back to a cursor-resolved cross-window
                        // move - an owned transparent target window never registers as a drop target for the
                        // OS.
                        if result = DragDropEffects.None && panelViews.ContainsKey panelId then
                            dragFellThrough.Trigger(this, DragFellThrough(panelId, NativeCursor.position ()))
                    with ex ->
                        // OLE drag is a shared-resource operation that can throw when another process holds
                        // the OLE lock; a failed drag is cosmetic. Clear any in-progress drag visuals and
                        // drop it rather than attempting the cross-window fallback on an unknown failure.
                        Trace.TraceWarning($"DockingSurface: drag operation failed: {ex.Message}")
                        dragCompleted.Trigger(this, EventArgs.Empty)
                    dragging <- false
            | _ -> ())

    let rec renderNode (node: LayoutNode) : FrameworkElement =
        if DockingModel.isLeaf node then
            renderLeaf node
        else
            renderSplit node

    // A lone panel carries no tab strip (content before chrome). When this surface locks its sole panel
    // (the primary window) and exactly one panel remains, that panel is fully chromeless and immovable; any
    // other lone panel gets a hover-revealed handle so it can still be dragged or closed. Two or more panels
    // always share a tab strip.
    and renderLeaf (node: LayoutNode) : FrameworkElement =
        let soleInSurface = (DockingModel.slots model |> List.length) = 1
        match node.Panels with
        | [| slot |] when lockSolePanel && soleInSurface -> renderLockedPanel node slot
        | [| slot |] -> renderLonePanel node slot
        | _ -> renderTabGroup node

    // The shared host for a single panel: the view filling the tile, click/focus activating its group, the
    // entrance animation, and the drop target so another panel can still dock onto it. The handle (drag and
    // close) is layered on by renderLonePanel; a locked panel gets none.
    and buildPanelHost (node: LayoutNode) (slot: PanelSlot) : Grid =
        // A transparent background makes the whole panel area hit-test-visible so the hover handle reveals
        // reliably across the panel, not only over its child content.
        let host = Grid(Background = Brushes.Transparent)

        let viewHost =
            ContentControl(
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch)
        match panelViews.TryGetValue slot.PanelId with
        | true, view -> viewHost.Content <- view
        | _ -> ()
        host.Children.Add(viewHost) |> ignore

        let groupId = node.GroupId
        let activate () =
            activeGroupId <- groupId
            activeTabChanged.Trigger(this, slot.PanelId)
        host.PreviewMouseDown.Add(fun _ -> activate ())
        host.GotKeyboardFocus.Add(fun _ -> activate ())

        if slot.PanelId = pendingEnterPanel then
            host.Loaded.Add(fun _ -> Motion.enter host)

        this.AttachDropTarget(host, groupId)
        host

    // The primary window's last panel: chromeless and immovable - no handle, drag, or close - so it always
    // fills the window and the main window is never empty.
    and renderLockedPanel (node: LayoutNode) (slot: PanelSlot) : FrameworkElement =
        buildPanelHost node slot :> FrameworkElement

    and renderLonePanel (node: LayoutNode) (slot: PanelSlot) : FrameworkElement =
        let host = buildPanelHost node slot

        let handle = buildTabHeader slot
        TextElement.SetForeground(handle, Colors.textBright)

        let handleBar =
            Border(
                Background = handleBarBrush,
                Padding = Thickness(6.0, 1.0, 4.0, 1.0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.0,
                // Not hit-testable until revealed: an invisible (Opacity 0) element still hit-tests in WPF, so
                // without this a press in the panel's content that happened to land under the parked handle
                // would arm a drag. Gated on the shown state, the drag starts only from the visible panel tab.
                IsHitTestVisible = false,
                Child = handle)
        host.Children.Add(handleBar) |> ignore
        attachPanelDrag handleBar slot.PanelId

        // Reveal the handle only near the top edge (or while the cursor is on the handle itself), tracking a
        // bool so the fade fires on transitions rather than on every mouse move. Hit-testing follows the
        // reveal so the handle is a drag target only while it is actually shown.
        let mutable shown = false
        let setShown value =
            if value <> shown then
                shown <- value
                handleBar.IsHitTestVisible <- value
                Motion.fadeTo handleBar (if value then 1.0 else 0.0)

        host.MouseMove.Add(fun args ->
            setShown (args.GetPosition(host).Y <= hoverBandHeight || handleBar.IsMouseOver))
        host.MouseLeave.Add(fun _ -> setShown false)

        host :> FrameworkElement

    and renderTabGroup (node: LayoutNode) : FrameworkElement =
        // Not a tab stop: keyboard focus traversal lands only on the panels' interactive controls, never on
        // the group chrome itself. Tabs are switched by click; the content carries the focusable widgets.
        let tabControl =
            TabControl(Background = Brushes.Transparent, BorderThickness = Thickness(0.0), IsTabStop = false)

        // A lone panel needs no tab chrome - content before chrome. Strip the header strip (and with it the
        // close button) until a second panel shares the group; the content site keeps hosting the view.
        if node.Panels.Length <= 1 then
            tabControl.Template <- headerlessTabTemplate

        for slot in node.Panels do
            // Not a tab stop: a tab header is switched by click, not Tab traversal, which lands only on the
            // panels' interactive controls.
            let item = TabItem(Header = buildTabHeader slot, Style = tabItemStyle, IsTabStop = false)
            match panelViews.TryGetValue slot.PanelId with
            | true, view -> item.Content <- view
            | _ -> ()
            // The whole tab is the drag handle, not just its inner label, so a press anywhere on the tab
            // arms the drag (the fix for "drag only works on the second try").
            attachPanelDrag item slot.PanelId
            tabControl.Items.Add(item) |> ignore

        if node.Panels.Length > 0 then
            tabControl.SelectedIndex <- min node.ActiveIndex (node.Panels.Length - 1)

        let groupId = node.GroupId
        tabControl.SelectionChanged.Add(fun args ->
            if Object.ReferenceEquals(args.OriginalSource, tabControl) then
                activeGroupId <- groupId
                model <- DockingModel.setActiveIndex groupId tabControl.SelectedIndex model
                if tabControl.SelectedIndex >= 0 && tabControl.SelectedIndex < node.Panels.Length then
                    let shown = node.Panels[tabControl.SelectedIndex]
                    match panelViews.TryGetValue shown.PanelId with
                    | true, view -> Motion.appear view
                    | _ -> ()
                    activeTabChanged.Trigger(this, shown.PanelId)
                layoutChanged.Trigger(this, EventArgs.Empty))

        if node.Panels |> Array.exists (fun slot -> slot.PanelId = pendingEnterPanel) then
            tabControl.Loaded.Add(fun _ -> Motion.enter tabControl)

        this.AttachDropTarget(tabControl, groupId)
        tabControl :> FrameworkElement

    and renderSplit (node: LayoutNode) : FrameworkElement =
        let grid = Grid()
        let isHorizontal = node.Orientation = DockingModel.Horizontal
        let childCount = node.Children.Length

        let define size =
            if isHorizontal then
                grid.ColumnDefinitions.Add(ColumnDefinition(Width = size))
            else
                grid.RowDefinitions.Add(RowDefinition(Height = size))

        let place (element: FrameworkElement) index =
            if isHorizontal then Grid.SetColumn(element, index) else Grid.SetRow(element, index)
            grid.Children.Add(element) |> ignore

        node.Children
        |> Array.iteri (fun childIndex child ->
            if childIndex > 0 then
                define (GridLength(4.0))
                let splitter =
                    GridSplitter(
                        Background = splitterBrush,
                        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch)
                splitter.Width <- if isHorizontal then 4.0 else Double.NaN
                splitter.Height <- if isHorizontal then Double.NaN else 4.0
                splitter.MouseEnter.Add(fun _ -> splitter.Background <- Colors.clavis)
                splitter.MouseLeave.Add(fun _ -> splitter.Background <- splitterBrush)
                splitter.DragCompleted.Add(fun _ -> this.CaptureSplitSizes(node.GroupId, grid, isHorizontal, childCount))
                place splitter ((childIndex * 2) - 1)

            let size = if childIndex < node.Sizes.Length then node.Sizes[childIndex] else 1.0
            define (GridLength(size, GridUnitType.Star))
            place (renderNode child) (childIndex * 2))

        grid :> FrameworkElement

    do
        this.HorizontalContentAlignment <- HorizontalAlignment.Stretch
        this.VerticalContentAlignment <- VerticalAlignment.Stretch
        this.RenderModel()
        let overlay = Grid()
        overlay.Children.Add(renderHost) |> ignore
        overlay.Children.Add(dropHint) |> ignore
        this.Content <- overlay

    member private _.CaptureSplitSizes(splitGroupId: Guid, grid: Grid, isHorizontal: bool, childCount: int) =
        let actual =
            if isHorizontal then
                [| for index in 0 .. childCount - 1 -> grid.ColumnDefinitions[index * 2].ActualWidth |]
            else
                [| for index in 0 .. childCount - 1 -> grid.RowDefinitions[index * 2].ActualHeight |]

        let total = Array.sum actual
        if total > 0.0 then
            let ratios = actual |> Array.map (fun value -> value / total)
            model <- DockingModel.setSizes splitGroupId ratios model
            layoutChanged.Trigger(this, EventArgs.Empty)

    /// Render the current model into the host, refreshing the drop-target registry the cross-window drag
    /// relies on (cleared first so stale elements from the previous render are dropped).
    member private _.RenderModel() =
        dropTargets.Clear()
        renderHost.Content <- renderNode model
        pendingEnterPanel <- Guid.Empty

    member private this.Rebuild() =
        for view in panelViews.Values do
            detachView view

        this.RenderModel()

    // The drop zone for a point (local to target): the outer band of each edge is a slide-in, the next
    // band in is a split, and the centre is a tab. X (left/right) is tested before Y (top/bottom).
    member private _.ZoneOf(target: FrameworkElement, position: Point) =
        let width = target.ActualWidth
        let height = target.ActualHeight
        let slide = 0.10
        let split = 0.25
        if position.X < width * slide then DropSlide Left
        elif position.X > width * (1.0 - slide) then DropSlide Right
        elif position.Y < height * slide then DropSlide Top
        elif position.Y > height * (1.0 - slide) then DropSlide Bottom
        elif position.X < width * split then DropSplit Left
        elif position.X > width * (1.0 - split) then DropSplit Right
        elif position.Y < height * split then DropSplit Top
        elif position.Y > height * (1.0 - split) then DropSplit Bottom
        else DropTab

    member private this.AttachDropTarget(target: FrameworkElement, groupId: Guid) =
        target.AllowDrop <- true
        dropTargets.Add(target, groupId)
        target.PreviewDragOver.Add(fun args -> this.ShowDropHint(target, args))
        target.PreviewDrop.Add(fun args -> this.PerformDrop(groupId, target, args))
        target.DragLeave.Add(fun _ -> dropHint.Visibility <- Visibility.Collapsed)

    /// Position and label the drop hint over target for the given zone (in target's own coordinates). The
    /// slide band is drawn as a thin strip at the edge; the split as a half; the tab as the full group.
    member private this.DrawDropHint(target: FrameworkElement, zone: DropZone) =
        let origin = target.TransformToVisual(this).Transform(Point(0.0, 0.0))
        let width = target.ActualWidth
        let height = target.ActualHeight
        let band = 0.18
        let left, top, hintWidth, hintHeight, label =
            match zone with
            | DropSlide Left -> origin.X, origin.Y, width * band, height, "slide ←"
            | DropSlide Right -> origin.X + width * (1.0 - band), origin.Y, width * band, height, "slide →"
            | DropSlide Top -> origin.X, origin.Y, width, height * band, "slide ↑"
            | DropSlide Bottom -> origin.X, origin.Y + height * (1.0 - band), width, height * band, "slide ↓"
            | DropSplit Left -> origin.X, origin.Y, width / 2.0, height, "split ←"
            | DropSplit Right -> origin.X + width / 2.0, origin.Y, width / 2.0, height, "split →"
            | DropSplit Top -> origin.X, origin.Y, width, height / 2.0, "split ↑"
            | DropSplit Bottom -> origin.X, origin.Y + height / 2.0, width, height / 2.0, "split ↓"
            | DropTab -> origin.X, origin.Y, width, height, "tab"

        dropHintLabel.Text <- label
        let firstShow = dropHint.Visibility <> Visibility.Visible
        dropHint.Visibility <- Visibility.Visible
        if firstShow then
            Motion.appear dropHint

        // Glide the hint between zones as the cursor crosses bands (re-issued each drag-over tick, so it
        // tracks a moving target smoothly) rather than snapping its margin/size. A To-only DoubleAnimation
        // takes the property's current value as its origin, but Width/Height default to NaN (Auto) until the
        // hint is first measured - and WPF throws animating from NaN. Pass an explicit From: the current
        // rendered size, or the target itself on the first show so it snaps in without a NaN origin.
        let glideSize (property: DependencyProperty) (current: float) (target: float) =
            let from = if Double.IsNaN current || current <= 0.0 then target else current
            dropHint.BeginAnimation(property, DoubleAnimation(from, target, Motion.Instant, EasingFunction = Motion.easeOut()))

        dropHint.BeginAnimation(FrameworkElement.MarginProperty, ThicknessAnimation(Thickness(left, top, 0.0, 0.0), Motion.Instant, EasingFunction = Motion.easeOut()))
        glideSize FrameworkElement.WidthProperty dropHint.ActualWidth hintWidth
        glideSize FrameworkElement.HeightProperty dropHint.ActualHeight hintHeight

    member private this.ShowDropHint(target: FrameworkElement, args: DragEventArgs) =
        if args.Data.GetDataPresent(dragFormat) then
            args.Effects <- DragDropEffects.Move
            args.Handled <- true
            this.DrawDropHint(target, this.ZoneOf(target, args.GetPosition(target)))

    // The drop target (and its group) under a screen point, or None when the point is over no leaf group
    // (e.g. a splitter gap, or outside this surface). Used to drive cross-window drag, where no OLE
    // drag-over reaches this window.
    member private _.DropTargetUnder(screenPoint: Point) =
        let contains (target: FrameworkElement) =
            target.IsVisible && target.ActualWidth > 0.0 && target.ActualHeight > 0.0
            && (let local = target.PointFromScreen(screenPoint)
                local.X >= 0.0 && local.Y >= 0.0 && local.X <= target.ActualWidth && local.Y <= target.ActualHeight)

        dropTargets |> Seq.tryFind (fun (target, _) -> contains target)

    // Cross-window docking does not support slide-in (a slide-in is a window-local overlay), so a slide
    // zone collapses to a split at the same edge when the drop comes from another window.
    member private _.DockableZone(zone: DropZone) =
        match zone with
        | DropSlide direction -> DropSplit direction
        | other -> other

    /// Paint the drop-zone hint for a cross-window drag whose cursor is at screenPoint over this surface.
    /// Driven by the host (the drag source's window cannot reach here through OLE).
    member this.ShowExternalDropHint(screenPoint: Point) =
        match this.DropTargetUnder screenPoint with
        | Some (target, _) ->
            this.DrawDropHint(target, this.DockableZone(this.ZoneOf(target, target.PointFromScreen screenPoint)))
        | None -> dropHint.Visibility <- Visibility.Collapsed

    member this.ClearExternalDropHint() =
        dropHint.Visibility <- Visibility.Collapsed

    /// Adopt a panel lifted from another surface, docking it at the zone under screenPoint (or as a tab in
    /// the active group when the point is over no specific group). The zone-aware cross-window counterpart
    /// of AddExistingPanel.
    member this.AddExistingPanelAt(transfer: PanelTransfer, screenPoint: Point) =
        let target =
            match this.DropTargetUnder screenPoint with
            | Some (element, groupId) ->
                match this.DockableZone(this.ZoneOf(element, element.PointFromScreen screenPoint)) with
                | DropSplit direction -> SplitGroup(groupId, direction, 0.5)
                | _ -> IntoGroup groupId
            | None -> IntoActiveGroup

        this.AddExistingPanel(transfer, target)

    member private this.PerformDrop(groupId: Guid, target: FrameworkElement, args: DragEventArgs) =
        dropHint.Visibility <- Visibility.Collapsed
        if args.Data.GetDataPresent(dragFormat) then
            match Guid.TryParse(args.Data.GetData(dragFormat) :?> string) with
            | true, panelId ->
                let zone = this.ZoneOf(target, args.GetPosition(target))
                let ownedHere = panelViews.ContainsKey panelId
                // A panel this surface owns slides into the edge or moves locally; one dragged from another
                // window is handed to the host (which adopts it here), and cannot become a slide-in.
                match ownedHere, zone with
                | true, DropSlide direction -> slideInRequested.Trigger(this, SlideInRequest(panelId, edgeName direction))
                | true, DropSplit direction -> this.MovePanel(panelId, SplitGroup(groupId, direction, 0.5))
                | true, DropTab -> this.MovePanel(panelId, IntoGroup groupId)
                | false, _ ->
                    let dockTarget =
                        match this.DockableZone zone with
                        | DropSplit direction -> SplitGroup(groupId, direction, 0.5)
                        | _ -> IntoGroup groupId
                    externalPanelDropped.Trigger(this, ExternalPanelDrop(panelId, dockTarget))
                args.Handled <- true
            | _ -> ()

    /// Move an existing panel to a new dock target, preserving its view (used by drag-to-dock).
    member this.MovePanel(panelId: Guid, target: DockTarget) =
        match panelViews.ContainsKey panelId, DockingModel.findSlot panelId model with
        | true, Some slot ->
            pendingEnterPanel <- panelId
            let withoutPanel = DockingModel.removePanel panelId model
            model <- DockingModel.addPanel target activeGroupId (Guid.NewGuid()) slot withoutPanel
            match DockingModel.groupContaining panelId model with
            | Some landedGroup -> activeGroupId <- landedGroup
            | None -> ()
            this.Rebuild()
            layoutChanged.Trigger(this, EventArgs.Empty)
        | _ -> ()

    /// Lift a panel (slot + live view) out of this surface so another surface can adopt it. Returns null
    /// when the panel is not hosted here. Used for cross-window drag.
    member this.TryTakePanel(panelId: Guid) : PanelTransfer =
        match panelViews.TryGetValue panelId, DockingModel.findSlot panelId model with
        | (true, view), Some slot ->
            detachView view
            panelViews.Remove panelId |> ignore
            model <- DockingModel.removePanel panelId model
            match DockingModel.firstLeaf model with
            | Some leafNode -> activeGroupId <- leafNode.GroupId
            | None -> ()
            this.Rebuild()
            layoutChanged.Trigger(this, EventArgs.Empty)
            PanelTransfer(slot, view)
        | _ -> null

    /// Adopt a panel lifted from another surface (cross-window drag), hosting its existing view at target.
    member this.AddExistingPanel(transfer: PanelTransfer, target: DockTarget) =
        let slot = transfer.Slot
        panelViews[slot.PanelId] <- transfer.View
        pendingEnterPanel <- slot.PanelId
        model <- DockingModel.addPanel target activeGroupId (Guid.NewGuid()) slot model
        match DockingModel.groupContaining slot.PanelId model with
        | Some groupId -> activeGroupId <- groupId
        | None -> ()
        this.Rebuild()
        layoutChanged.Trigger(this, EventArgs.Empty)

    [<CLIEvent>]
    member _.ExternalPanelDropped = externalPanelDropped.Publish

    /// Raised when a drag off this surface ends with no window accepting the drop. The host resolves the
    /// window under the cursor and completes the cross-window move (see DragFellThrough).
    [<CLIEvent>]
    member _.DragFellThrough = dragFellThrough.Publish

    /// Fires repeatedly while a drag started on this surface is in progress, carrying the cursor's screen
    /// position so the host can paint the drop-zone hint on whichever window sits under the pointer.
    [<CLIEvent>]
    member _.DragMoving = dragMoving.Publish

    /// Fires once when a drag started on this surface ends (dropped or cancelled), so the host can clear
    /// any cross-window drop hints it painted.
    [<CLIEvent>]
    member _.DragCompleted = dragCompleted.Publish

    /// Fires when a panel owned by this surface is dropped into an edge slide zone. The host lifts the
    /// panel (TryTakePanel) and re-hosts it as a slide-in on that edge.
    [<CLIEvent>]
    member _.SlideInRequested = slideInRequested.Publish

    [<CLIEvent>]
    member _.LayoutChanged = layoutChanged.Publish

    /// Fires after a panel is closed off this surface (its leave animation done and the tree reflowed). A
    /// drag-out does not raise it - the host drives cross-window moves explicitly - so it cleanly signals
    /// "a panel left for good", letting the host retire a now-empty secondary window.
    [<CLIEvent>]
    member _.PanelRemoved = panelRemoved.Publish

    [<CLIEvent>]
    member _.ActiveTabChanged = activeTabChanged.Publish

    [<CLIEvent>]
    member _.PanelCloseRequested = panelCloseRequested.Publish

    member _.ActiveGroupId = activeGroupId

    /// When true, the surface's sole remaining panel is locked: chromeless, full-window, and immovable (no
    /// drag/close handle), so the window always keeps a panel. The host sets it on the primary window.
    member this.LockSolePanel
        with get () = lockSolePanel
        and set value =
            if value <> lockSolePanel then
                lockSolePanel <- value
                this.Rebuild()

    member private _.ActivePanel =
        match DockingModel.findGroup activeGroupId model with
        | Some group when group.Panels.Length > 0 ->
            let index =
                if group.ActiveIndex >= 0 && group.ActiveIndex < group.Panels.Length then
                    group.ActiveIndex
                else
                    0

            Some group.Panels[index]
        | _ -> None

    /// The panel kind shown in the active tab group (empty when the surface has no panels). The host uses
    /// it to resolve panel-scoped key bindings against the focused panel.
    member this.ActivePanelKind =
        match this.ActivePanel with
        | Some slot -> slot.PanelKind
        | None -> ""

    /// The title of the panel in the active tab group (empty when none). The host shows it as the contextual
    /// window title for non-chat panels.
    member this.ActivePanelTitle =
        match this.ActivePanel with
        | Some slot -> slot.Title
        | None -> ""

    /// The instance id of the panel in the active tab group (Guid.Empty when none), so the host can
    /// target a panel-local command at the focused instance.
    member this.ActivePanelId =
        match this.ActivePanel with
        | Some slot -> slot.PanelId
        | None -> Guid.Empty

    /// The container element of the active tab group (its TabControl or lone-panel host), or null when the
    /// surface has no panels. The host frames it with focus brackets, so the visual marks the same group
    /// the snapshot and panel-scoped key bindings resolve against.
    member _.ActivePanelContainer : FrameworkElement =
        match dropTargets |> Seq.tryFind (fun (_, groupId) -> groupId = activeGroupId) with
        | Some (target, _) -> target
        | None -> Unchecked.defaultof<FrameworkElement>

    /// True when the surface is split into more than one tiled group. A single full-surface group needs no
    /// focus brackets (the window's own active border marks it); brackets earn their place only when several
    /// tiles share the surface and the focused one must be told apart.
    member _.IsSplit = not (DockingModel.isLeaf model)

    member _.Capture() = model

    /// Add a panel and show its view. Returns the group id the panel landed in.
    member this.AddPanel(panelId: Guid, panelKind: string, title: string, view: FrameworkElement, target: DockTarget) =
        panelViews[panelId] <- view
        pendingEnterPanel <- panelId
        let slot = { PanelId = panelId; PanelKind = panelKind; Title = title; SavedState = "" }
        let newGroupId = Guid.NewGuid()
        model <- DockingModel.addPanel target activeGroupId newGroupId slot model
        match DockingModel.groupContaining panelId model with
        | Some groupId -> activeGroupId <- groupId
        | None -> ()
        this.Rebuild()
        layoutChanged.Trigger(this, EventArgs.Empty)
        activeGroupId

    member this.RemovePanel(panelId: Guid) =
        match panelViews.TryGetValue panelId with
        | true, view -> Motion.leave view (Action(fun () -> this.CompleteRemove panelId))
        | _ -> ()

    // The model mutation + rebuild deferred until the leave animation finishes, so a closing tile fades and
    // collapses before the tree reflows around the gap it leaves.
    member private this.CompleteRemove(panelId: Guid) =
        if panelViews.Remove panelId then
            model <- DockingModel.removePanel panelId model
            match DockingModel.firstLeaf model with
            | Some leafNode -> activeGroupId <- leafNode.GroupId
            | None -> ()
            this.Rebuild()
            layoutChanged.Trigger(this, EventArgs.Empty)
            // A closing panel has left for good (unlike a drag-out, which the host drives explicitly), so
            // the host can retire a now-empty secondary window.
            panelRemoved.Trigger(this, EventArgs.Empty)

    member this.FocusPanel(panelId: Guid) =
        match DockingModel.groupContaining panelId model with
        | Some groupId ->
            activeGroupId <- groupId
            let indexInGroup =
                match DockingModel.findGroup groupId model with
                | Some groupNode ->
                    groupNode.Panels
                    |> Array.tryFindIndex (fun slot -> slot.PanelId = panelId)
                    |> Option.defaultValue 0
                | None -> 0

            model <- DockingModel.setActiveIndex groupId indexInGroup model
            this.Rebuild()
        | None -> ()

    /// Rebuild the whole surface from a saved layout. resolveView supplies a view for each slot (the
    /// host returns the conversation presenter for the conversation slot and a placeholder for panels
    /// awaiting re-materialisation). Placeholders are swapped later via ReplacePanelView.
    member this.Restore(layout: LayoutNode, resolveView: Func<Guid, string, FrameworkElement>) =
        for view in panelViews.Values do
            detachView view

        panelViews.Clear()
        model <- layout
        for slot in DockingModel.slots layout do
            panelViews[slot.PanelId] <- resolveView.Invoke(slot.PanelId, slot.PanelKind)

        match DockingModel.firstLeaf model with
        | Some leafNode -> activeGroupId <- leafNode.GroupId
        | None -> ()

        this.RenderModel()
        layoutChanged.Trigger(this, EventArgs.Empty)

    /// Swap a panel's view in place (used to replace a restore placeholder once the panel materialises).
    member this.ReplacePanelView(panelId: Guid, view: FrameworkElement) =
        match panelViews.TryGetValue panelId with
        | true, existing ->
            detachView existing
            panelViews[panelId] <- view
            this.Rebuild()
        | _ -> ()

    member _.PanelIds = panelViews.Keys |> Seq.toList

    /// The drag data-format key panels are dragged under (matches the private dragFormat literal). Exposed
    /// so the host can register a window-level drop target with the same key, forcing OLE drop-target
    /// registration on the window HWND - which an owned, transparent secondary window can otherwise miss,
    /// rejecting cross-window drops outright.
    static member DragFormat = "ClavisPanelId"
