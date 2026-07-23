namespace FabioSoft.Clavis.Rendering

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media

/// A small modal Yes/No prompt for destructive actions ("Delete this panel?"), styled like the rest of the
/// app (borderless window, square corners, dark fill) rather than the stock MessageBox. Escape and the
/// Cancel button both decline; only the explicit confirm button accepts, so a stray Enter never deletes
/// anything.
[<ExcludeFromCodeCoverage>] // WPF window construction
type ConfirmDialog private (owner: Window, message: string, confirmLabel: string) as this =
    inherit Window()

    let mutable confirmed = false

    let closeWith (result: bool) =
        confirmed <- result
        this.Close()

    let styledButton (styleKey: string) (content: string) (onClick: unit -> unit) : Button =
        let button = Button(Content = content, MinWidth = 72.0, Margin = Thickness(8.0, 0.0, 0.0, 0.0))
        button.SetResourceReference(FrameworkElement.StyleProperty, styleKey)
        // The button styles default to the small LabelFontSize; a dialog reads at the body role, so lift the
        // label to BodyFontSize (a local value overrides the style's setter).
        button.SetResourceReference(Control.FontSizeProperty, "BodyFontSize")
        button.Click.Add(fun _ -> onClick ())
        button

    let messageText =
        TextBlock(
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = Thickness(20.0, 20.0, 20.0, 16.0))

    let buttons =
        let panel =
            StackPanel(
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = Thickness(20.0, 0.0, 20.0, 20.0))
        panel.Children.Add(styledButton "ActionButton" "Cancel" (fun () -> closeWith false)) |> ignore
        panel.Children.Add(styledButton "ActionButtonDanger" confirmLabel (fun () -> closeWith true)) |> ignore
        panel

    let frame =
        let content = StackPanel()
        content.Children.Add(messageText) |> ignore
        content.Children.Add(buttons) |> ignore
        Border(Child = content, BorderThickness = Thickness(1.0))

    do
        messageText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush")
        messageText.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        messageText.SetResourceReference(TextBlock.FontSizeProperty, "BodyFontSize")
        frame.SetResourceReference(Border.BorderBrushProperty, "FrameBrush")
        frame.SetResourceReference(Border.BackgroundProperty, "BlackBrush")

        if not (isNull owner) then
            this.Owner <- owner

        this.WindowStyle <- WindowStyle.None
        this.AllowsTransparency <- true
        this.Background <- Brushes.Transparent
        this.ResizeMode <- ResizeMode.NoResize
        this.ShowInTaskbar <- false
        this.Topmost <- true
        this.SizeToContent <- SizeToContent.Height
        this.Width <- 360.0
        this.WindowStartupLocation <-
            if isNull owner then WindowStartupLocation.CenterScreen else WindowStartupLocation.CenterOwner
        this.Content <- frame

    override _.OnPreviewKeyDown(e) =
        if e.Key = Key.Escape then
            closeWith false
            e.Handled <- true
        else
            base.OnPreviewKeyDown(e)

    /// Shows a blocking Yes/No prompt; returns true only if the confirm button was clicked. `owner` may be
    /// null (the dialog then centers on screen instead of over a window).
    static member Confirm(owner: Window, message: string, confirmLabel: string) : bool =
        let dialog = ConfirmDialog(owner, message, confirmLabel)
        dialog.ShowDialog() |> ignore
        dialog.Confirmed

    member _.Confirmed = confirmed
