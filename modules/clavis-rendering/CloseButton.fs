namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation

/// The one close affordance shared by panel tabs and window chrome, so every "x" reads and behaves the
/// same. A square, background-free cross that sits white at rest and, on hover, eases to clavis blue and
/// grows a touch - no background fill, ever. Public (the C# host consumes it; the codebase forbids
/// InternalsVisibleTo) and lives in the never-unloaded Default ALC, so it roots no plugin types.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module CloseButton =

    [<Literal>]
    let private restGlyphSize = 12.0

    [<Literal>]
    let private hoverScale = 1.3

    // The cross sits at near-white rest, brightening to clavis on hover. The rest colour is its own value
    // (not a frozen theme brush) because the hover animation mutates the brush's Color in place.
    let private restColor = Color.FromRgb(0xF0uy, 0xF0uy, 0xF0uy)
    let private hoverColor = Colors.clavis.Color

    /// Build a close affordance that runs onClose when clicked. Returns the hosting Border so callers can
    /// size it (the window caption gives it a fixed 30x28 hit box; a tab leaves it sized to the glyph).
    let create (onClose: Action) : Border =
        let glyph =
            TextBlock(
                Text = "✕",
                FontSize = restGlyphSize,
                Foreground = SolidColorBrush(restColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = Point(0.5, 0.5))

        let scale = ScaleTransform(1.0, 1.0)
        glyph.RenderTransform <- scale

        let button =
            Border(
                Background = Brushes.Transparent,
                Padding = Thickness(4.0, 0.0, 4.0, 0.0),
                Cursor = Cursors.Hand,
                Child = glyph)

        let animateColor (target: Color) =
            glyph.Foreground.BeginAnimation(
                SolidColorBrush.ColorProperty,
                ColorAnimation(target, Motion.Instant, EasingFunction = Motion.easeOut()))

        let animateScale (target: float) =
            let animation = DoubleAnimation(target, Motion.Instant, EasingFunction = Motion.easeOut())
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation)
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation)

        button.MouseEnter.Add(fun _ ->
            animateColor hoverColor
            animateScale hoverScale)
        button.MouseLeave.Add(fun _ ->
            animateColor restColor
            animateScale 1.0)
        button.MouseLeftButtonUp.Add(fun args ->
            onClose.Invoke()
            args.Handled <- true)

        button
