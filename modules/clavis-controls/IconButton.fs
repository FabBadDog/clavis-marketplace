namespace FabioSoft.Clavis.Controls

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation

/// A small, chrome-free glyph button: a single character (e.g. a remove "x", a chevron) with no background
/// or border, so it reads as an inline affordance, not a boxed control. The generic counterpart to
/// clavis-rendering's CloseButton, which stays the one canonical window/tab close. Dim at rest, brightening
/// on hover; runs onClick when pressed.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module IconButton =

    [<Literal>]
    let private hitWidth = 22.0

    [<Literal>]
    let private pressedScale = 0.9

    // The press shrink/restore cadence: a 250ms eased transition each way, so the glyph eases down while held
    // and eases back on release rather than snapping.
    let private pressDuration = Duration(TimeSpan.FromMilliseconds 250.0)

    let create (glyph: string) (onClick: Action) : Button =
        let button =
            Button(
                Content = glyph,
                Width = hitWidth,
                Background = Brushes.Transparent,
                BorderThickness = Thickness(0.0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = Point(0.5, 0.5))

        // Press feedback: the glyph eases down to 90% while held and eases back to full size on release, so a
        // click feels tactile without snapping.
        let scale = ScaleTransform(1.0, 1.0)
        button.RenderTransform <- scale
        let animateScale target =
            let animation = DoubleAnimation(target, pressDuration, EasingFunction = CubicEase(EasingMode = EasingMode.EaseOut))
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation)
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation)

        // Latch the press: while held, the shrunk glyph's hit area no longer covers the cursor, so WPF fires
        // MouseLeave - which must NOT spring the glyph back (the cause of it snapping to full size mid-press).
        // Only releasing the button restores it; MouseLeave just dims the colour when not pressed.
        let mutable pressed = false

        button.SetResourceReference(Control.ForegroundProperty, "TextDimBrush")
        button.MouseEnter.Add(fun _ -> button.SetResourceReference(Control.ForegroundProperty, "TextBrightBrush"))
        button.MouseLeave.Add(fun _ ->
            if not pressed then
                button.SetResourceReference(Control.ForegroundProperty, "TextDimBrush")
                animateScale 1.0)
        button.PreviewMouseLeftButtonDown.Add(fun _ ->
            pressed <- true
            animateScale pressedScale)
        button.PreviewMouseLeftButtonUp.Add(fun _ ->
            pressed <- false
            animateScale 1.0
            if not button.IsMouseOver then
                button.SetResourceReference(Control.ForegroundProperty, "TextDimBrush"))
        button.Click.Add(fun _ -> onClick.Invoke())
        button
