namespace FabioSoft.Clavis.Controls

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media

/// A small, chrome-free glyph button: a single character (e.g. a remove "x", a chevron) with no background
/// or border, so it reads as an inline affordance, not a boxed control. The generic counterpart to
/// clavis-rendering's CloseButton, which stays the one canonical window/tab close. Dim at rest, brightening
/// on hover; runs onClick when pressed.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module IconButton =

    [<Literal>]
    let private hitWidth = 22.0

    let create (glyph: string) (onClick: Action) : Button =
        let button =
            Button(
                Content = glyph,
                Width = hitWidth,
                Background = Brushes.Transparent,
                BorderThickness = Thickness(0.0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center)
        button.SetResourceReference(Control.ForegroundProperty, "TextDimBrush")
        button.MouseEnter.Add(fun _ -> button.SetResourceReference(Control.ForegroundProperty, "TextBrightBrush"))
        button.MouseLeave.Add(fun _ -> button.SetResourceReference(Control.ForegroundProperty, "TextDimBrush"))
        button.Click.Add(fun _ -> onClick.Invoke())
        button
