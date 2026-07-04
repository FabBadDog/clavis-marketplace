namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation

/// One edge-anchored slide-in surface: a translucent panel pinned to a window edge (left, right, top, or
/// bottom) that animates in from that edge and parks just off-screen when hidden. It hosts a single panel
/// view through its content site; the window decides which panel each edge shows and when to open or hide
/// it (the opposite-pair rule and auto-hide live in the host). Lives in the Default ALC, so it may use WPF
/// freely without rooting plugin types - the same reason DockingSurface does.
[<ExcludeFromCodeCoverage>] // WPF construction + animation
type SlideInHost(edge: string) as this =
    inherit Border()

    let slideDuration = Duration(TimeSpan.FromMilliseconds 180.0)
    let transform = TranslateTransform()
    let mutable isOpen = false

    // Left and right slide along X and size by width; top and bottom slide along Y and size by height.
    let isHorizontalEdge = edge = "left" || edge = "right"
    let widthFraction = 0.38
    let heightFraction = 0.42

    let content =
        ContentControl(
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch)

    // The panel view plus a slim hover-revealed handle (title + close + drag) layered over its top edge, so a
    // slide-in can be dragged back out to re-dock or closed - the reverse of dropping a panel into an edge.
    let layoutGrid = Grid()

    let dragMoving = Event<EventHandler<Point>, Point>()
    let dragFellThrough = Event<EventHandler<DragFellThrough>, DragFellThrough>()
    let dragCompleted = Event<EventHandler, EventArgs>()
    let closeRequested = Event<EventHandler<Guid>, Guid>()

    let mutable currentBar: Border option = None
    let mutable revealed = false

    let setRevealed value =
        match currentBar with
        | Some bar when value <> revealed ->
            revealed <- value
            bar.IsHitTestVisible <- value
            Motion.fadeTo bar (if value then 1.0 else 0.0)
        | _ -> ()

    // Same translucent dark fill the shortcut overlay uses: the window body reads faintly through, and it
    // costs nothing (no live blur).
    let tintBrush = SolidColorBrush(Color.FromArgb(0xE6uy, 0x05uy, 0x05uy, 0x0Auy))

    // How far to translate the panel off its edge to park it hidden (a little past the edge so its border
    // line clears the content too).
    let parkedOffset () =
        match edge with
        | "left" -> -(this.ActualWidth + 2.0)
        | "right" -> this.ActualWidth + 2.0
        | "top" -> -(this.ActualHeight + 2.0)
        | _ -> this.ActualHeight + 2.0

    let slideProperty =
        if isHorizontalEdge then TranslateTransform.XProperty else TranslateTransform.YProperty

    let animate target =
        let animation =
            DoubleAnimation(target, slideDuration, EasingFunction = Motion.easeOut())

        transform.BeginAnimation(slideProperty, animation)

    do
        match edge with
        | "left" ->
            this.HorizontalAlignment <- HorizontalAlignment.Left
            this.VerticalAlignment <- VerticalAlignment.Stretch
            this.BorderThickness <- Thickness(0.0, 0.0, 1.0, 0.0)
        | "right" ->
            this.HorizontalAlignment <- HorizontalAlignment.Right
            this.VerticalAlignment <- VerticalAlignment.Stretch
            this.BorderThickness <- Thickness(1.0, 0.0, 0.0, 0.0)
        | "top" ->
            this.HorizontalAlignment <- HorizontalAlignment.Stretch
            this.VerticalAlignment <- VerticalAlignment.Top
            this.BorderThickness <- Thickness(0.0, 0.0, 0.0, 1.0)
        | _ ->
            this.HorizontalAlignment <- HorizontalAlignment.Stretch
            this.VerticalAlignment <- VerticalAlignment.Bottom
            this.BorderThickness <- Thickness(0.0, 1.0, 0.0, 0.0)

        this.Background <- tintBrush
        this.RenderTransform <- transform
        layoutGrid.Children.Add(content) |> ignore
        this.Child <- layoutGrid
        this.SetResourceReference(Border.BorderBrushProperty, "ClavisBrush")

        // Parked off-screen, the panel must not be a keyboard-focus target (it stays Visibility.Visible
        // while translated away, so visibility alone would not exclude it). Disabling its subtree removes
        // its controls from tab traversal until it is slid in; Open re-enables them.
        this.IsEnabled <- false

        // Size as a fraction of the window body. Re-applied whenever the parent resizes so the slide-in
        // keeps its proportion, and parked off-screen while hidden so its measured slot never overlaps the
        // docked content.
        this.Loaded.Add(fun _ ->
            match LogicalTreeHelper.GetParent(this) with
            | :? FrameworkElement as parent ->
                let apply () =
                    if isHorizontalEdge then
                        this.Width <- parent.ActualWidth * widthFraction
                    else
                        this.Height <- parent.ActualHeight * heightFraction

                apply ()
                parent.SizeChanged.Add(fun _ -> apply ())
            | _ -> ())

        this.SizeChanged.Add(fun _ ->
            if not isOpen then
                transform.SetValue(slideProperty, parkedOffset ()))

        // Reveal the handle once the cursor reaches the slide-in's top band (or is over the handle), then keep
        // it shown until the cursor leaves, so it can be navigated to and grabbed - matching a lone docked
        // panel. Hiding on every out-of-band move latched the handle non-hit-testable and unreachable.
        this.MouseMove.Add(fun args ->
            let y = args.GetPosition(this).Y
            let overBar = match currentBar with | Some bar -> bar.IsMouseOver | None -> false
            if y <= PanelHandle.hoverBandHeight || overBar then
                setRevealed true)
        this.MouseLeave.Add(fun _ -> setRevealed false)

    /// The edge this slide-in is anchored to ("left", "right", "top", "bottom").
    member _.Edge = edge

    member _.IsOpen = isOpen

    /// Host a panel's view under the given instance id and title. Replacing the content detaches the previous
    /// view from its old parent first, so the same view can move between a docked slot and a slide-in without
    /// a duplicate-parent error, and rebuilds the hover handle bound to this panel.
    member this.SetContent(panelId: Guid, title: string, view: FrameworkElement) =
        match view.Parent with
        | :? ContentControl as owner -> owner.Content <- null
        | :? Decorator as owner -> owner.Child <- null
        | _ -> ()

        content.Content <- view

        // Rebuild the hover handle for the panel now shown: drop the previous bar, add a fresh title + close +
        // drag bound to this panel id. Its drag events drive the same cross-window drop machinery a docked
        // panel's handle does; isOwned answers "is this panel still slid-in here?", so a completed re-dock (the
        // view detached from the content site) suppresses the tear-off fall-through.
        currentBar |> Option.iter (fun bar -> layoutGrid.Children.Remove(bar))
        revealed <- false
        let bar = PanelHandle.buildBar (PanelHandle.header title (fun () -> closeRequested.Trigger(this, panelId)))
        layoutGrid.Children.Add(bar) |> ignore
        currentBar <- Some bar
        PanelHandle.attachDrag
            bar
            panelId
            (fun point -> dragMoving.Trigger(this, point))
            (fun point -> dragFellThrough.Trigger(this, DragFellThrough(panelId, point)))
            (fun () -> dragCompleted.Trigger(this, EventArgs.Empty))
            (fun () -> obj.ReferenceEquals(content.Content, view))

    member _.View = content.Content :?> FrameworkElement

    /// Fires continuously while the slide-in's handle is dragged, carrying the cursor's screen position so the
    /// host can paint the cross-window drop hint.
    [<CLIEvent>]
    member _.DragMoving = dragMoving.Publish

    /// Fires when a drag off the handle ends with no window accepting the drop, so the host resolves the target
    /// under the cursor (re-dock into another window, or tear off into a new one).
    [<CLIEvent>]
    member _.DragFellThrough = dragFellThrough.Publish

    /// Fires once when a handle drag ends, so the host can clear any cross-window drop hints.
    [<CLIEvent>]
    member _.DragCompleted = dragCompleted.Publish

    /// Fires when the handle's close cross is clicked, carrying the panel instance to dismiss.
    [<CLIEvent>]
    member _.CloseRequested = closeRequested.Publish

    member this.Open() =
        isOpen <- true
        // Re-enable the subtree so its controls rejoin tab traversal; the coordinator focuses the panel
        // only when the user tabs into it, so a slide-in never grabs focus merely by appearing.
        this.IsEnabled <- true
        this.Visibility <- Visibility.Visible
        animate 0.0

    member this.Hide() =
        if isOpen then
            isOpen <- false
            this.IsEnabled <- false
            let animation =
                DoubleAnimation(parkedOffset (), slideDuration, EasingFunction = Motion.easeOut())
            // Collapse once the panel has parked so a later layout pass cannot bring it back: a
            // translated-but-Visible element keeps reserving its slot, and widening the window re-runs
            // layout against the stale held transform, which left the panel peeking back on-screen.
            // Guarded by isOpen so a re-open during the slide-out cancels the collapse.
            animation.Completed.Add(fun _ ->
                if not isOpen then
                    this.Visibility <- Visibility.Collapsed)
            transform.BeginAnimation(slideProperty, animation)
