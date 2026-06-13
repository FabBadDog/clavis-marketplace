namespace FabioSoft.Clavis.Rendering

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Media.Effects
open System.Windows.Shapes

/// A per-window, hit-test-invisible overlay that paints the Clavis focus visuals above the window content:
/// square corner brackets around the panel that holds keyboard focus, and a ring + glow around the focused
/// control. The host computes the rectangles in overlay coordinates
/// on each focus change and calls in here; this type only renders. Lives in the Default ALC, so it may use
/// WPF freely without rooting plugin types (the same reason DockingSurface and SlideInHost do).
[<ExcludeFromCodeCoverage>] // WPF rendering only
type FocusOverlay() as this =
    inherit Canvas()

    let bracketLength = 14.0
    let accent = Colors.clavis
    let glowColor = Colors.clavis.Color

    let ring =
        Border(
            BorderBrush = accent,
            BorderThickness = Thickness(1.0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = DropShadowEffect(Color = glowColor, BlurRadius = 11.0, ShadowDepth = 0.0, Opacity = 0.45))

    let corner geometry =
        Path(
            Stroke = accent,
            StrokeThickness = 1.5,
            Data = Geometry.Parse(geometry),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true)

    // Each bracket is an L drawn in local coordinates 0..bracketLength, its corner pointing into the rect.
    let topLeft = corner $"M0,{bracketLength} L0,0 L{bracketLength},0"
    let topRight = corner $"M{bracketLength},{bracketLength} L{bracketLength},0 L0,0"
    let bottomLeft = corner $"M0,0 L0,{bracketLength} L{bracketLength},{bracketLength}"
    let bottomRight = corner $"M{bracketLength},0 L{bracketLength},{bracketLength} L0,{bracketLength}"
    let corners = [| topLeft; topRight; bottomLeft; bottomRight |]

    let place (element: FrameworkElement) left top width height =
        Canvas.SetLeft(element, left)
        Canvas.SetTop(element, top)
        element.Width <- width
        element.Height <- height

    let positionCorner (element: Path) left top =
        Canvas.SetLeft(element, left)
        Canvas.SetTop(element, top)

    let setCornersVisible visibility =
        for element in corners do
            element.Visibility <- visibility

    do
        this.IsHitTestVisible <- false
        this.Focusable <- false
        this.Children.Add(ring) |> ignore
        for element in corners do
            this.Children.Add(element) |> ignore

    /// Frame the focused control with a ring + glow around its rect.
    member _.ShowControl(rect: Rect) =
        place ring (rect.X - 2.0) (rect.Y - 2.0) (rect.Width + 4.0) (rect.Height + 4.0)
        ring.Visibility <- Visibility.Visible

    /// Hide just the control ring - used for the chat input, whose focus is shown by recolouring its
    /// framing lines rather than a ring.
    member _.HideControl() =
        ring.Visibility <- Visibility.Collapsed

    /// Frame the panel that holds focus with four square corner brackets, sitting just outside its rect.
    member _.ShowPanelBrackets(rect: Rect) =
        let inset = 4.0
        let left = rect.X - inset
        let top = rect.Y - inset
        let right = rect.X + rect.Width + inset
        let bottom = rect.Y + rect.Height + inset
        positionCorner topLeft left top
        positionCorner topRight (right - bracketLength) top
        positionCorner bottomLeft left (bottom - bracketLength)
        positionCorner bottomRight (right - bracketLength) (bottom - bracketLength)
        setCornersVisible Visibility.Visible

    member _.HidePanel() = setCornersVisible Visibility.Collapsed

    /// Hide every visual - used when the window loses focus so only one window ever shows focus.
    member this.Clear() =
        ring.Visibility <- Visibility.Collapsed
        this.HidePanel()
