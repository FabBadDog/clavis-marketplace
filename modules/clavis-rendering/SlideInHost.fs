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
        this.Child <- content
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

    /// The edge this slide-in is anchored to ("left", "right", "top", "bottom").
    member _.Edge = edge

    member _.IsOpen = isOpen

    /// Host a panel's view. Replacing the content detaches the previous view from its old parent first, so
    /// the same view can move between a docked slot and a slide-in without a duplicate-parent error.
    member _.SetContent(view: FrameworkElement) =
        match view.Parent with
        | :? ContentControl as owner -> owner.Content <- null
        | :? Decorator as owner -> owner.Child <- null
        | _ -> ()

        content.Content <- view

    member _.View = content.Content :?> FrameworkElement

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
            animate (parkedOffset ())
