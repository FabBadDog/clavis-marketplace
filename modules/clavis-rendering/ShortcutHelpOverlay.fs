namespace FabioSoft.Clavis.Rendering

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Media.Animation

/// One row in the keyboard-help overlay: the gesture, what it does, and the scope it belongs to.
type ShortcutHelpRow =
    { Gesture: string
      Description: string
      Scope: string }

/// The generalized keyboard-help slide-in: a bottom-anchored panel that lists shortcut rows grouped by
/// scope and animates up from the window's bottom edge. Hosted by every window and fed the merged System
/// + Application + focused-Panel bindings. Lifted from the events panel's bespoke overlay so it is shared.
[<ExcludeFromCodeCoverage>] // WPF construction + animation
type ShortcutHelpOverlay() as this =
    inherit Border()

    let slideDuration = Duration(TimeSpan.FromMilliseconds 180.0)
    let transform = TranslateTransform(0.0, 1000.0)
    let rowsHost = StackPanel()
    let mutable isOpen = false

    // A static tinted-translucent fill stands in for the former acrylic blur. The blur re-sampled the
    // backdrop every frame (CompositionTarget.Rendering), which lagged; a fixed semi-transparent dark fill
    // costs nothing and still lets the window body read faintly through the panel.
    let tintBrush = SolidColorBrush(Color.FromArgb(0xE6uy, 0x05uy, 0x05uy, 0x0Auy))

    let scopeLabel scope =
        match scope with
        | "panel" -> "PANEL"
        | "system" -> "SYSTEM"
        | _ -> "APPLICATION"

    let scopeRank scope =
        match scope with
        | "panel" -> 0
        | "system" -> 2
        | _ -> 1

    let header (text: string) =
        let block =
            TextBlock(
                Text = text,
                FontSize = 9.0,
                FontWeight = FontWeights.Medium,
                Margin = Thickness(0.0, 10.0, 0.0, 6.0))

        block.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        block.SetResourceReference(TextBlock.ForegroundProperty, "ClavisBrush")
        block

    let rowFontSize = 7.5

    let row (item: ShortcutHelpRow) =
        let gesture =
            TextBlock(
                Text = item.Gesture,
                FontSize = rowFontSize,
                MinWidth = 120.0,
                Margin = Thickness(0.0, 1.0, 12.0, 1.0))

        gesture.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")
        gesture.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush")
        DockPanel.SetDock(gesture, Dock.Left)

        let description =
            TextBlock(
                Text = item.Description,
                FontSize = rowFontSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = Thickness(0.0, 1.0, 0.0, 1.0))

        description.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush")

        let dock = DockPanel()
        dock.Children.Add(gesture) |> ignore
        dock.Children.Add(description) |> ignore
        dock

    do
        let title =
            TextBlock(
                Text = "KEYBOARD",
                FontSize = 9.0,
                FontWeight = FontWeights.Medium,
                Margin = Thickness(0.0, 0.0, 0.0, 4.0))

        title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        title.SetResourceReference(TextBlock.ForegroundProperty, "ClavisBrush")

        let stack = StackPanel(Margin = Thickness(16.0, 12.0, 16.0, 12.0))
        stack.Children.Add(title) |> ignore
        stack.Children.Add(rowsHost) |> ignore

        this.Padding <- Thickness(0.0)
        this.BorderThickness <- Thickness(0.0, 1.0, 0.0, 0.0)
        this.VerticalAlignment <- VerticalAlignment.Bottom
        this.HorizontalAlignment <- HorizontalAlignment.Stretch
        this.RenderTransform <- transform
        this.Background <- tintBrush
        this.Child <- stack
        this.SetResourceReference(Border.BorderBrushProperty, "ClavisBrush")

        // Park just below the bottom edge (clipped) until first measured / opened.
        this.SizeChanged.Add(fun _ ->
            if not isOpen then
                transform.Y <- this.ActualHeight)

    member _.IsOpen = isOpen

    member _.SetRows(rows: IEnumerable<ShortcutHelpRow>) =
        rowsHost.Children.Clear()

        let ordered =
            rows
            |> Seq.groupBy (fun r -> r.Scope)
            |> Seq.sortBy (fun (scope, _) -> scopeRank scope)

        for scope, items in ordered do
            rowsHost.Children.Add(header (scopeLabel scope)) |> ignore
            for item in items do
                rowsHost.Children.Add(row item) |> ignore

    member private _.Animate(target: float) =
        let animation = DoubleAnimation(target, slideDuration, EasingFunction = Motion.easeOut())
        transform.BeginAnimation(TranslateTransform.YProperty, animation)

    member this.Open() =
        isOpen <- true
        this.Animate(0.0)

    member this.Hide() =
        isOpen <- false
        this.Animate(this.ActualHeight)

    member this.Toggle() =
        if isOpen then
            this.Hide()
        else
            this.Open()
