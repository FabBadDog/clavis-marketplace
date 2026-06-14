namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// The one reconciled badge set (replacing the several ad-hoc chip styles that drifted across panels). A
/// small square chip in the micro role; the form carries the meaning: `kind` = secondary outline (identity),
/// `neutral` = surface fill (a plain category), `signal` = a coloured outline (green/yellow/red), `status` =
/// a leading dot + dim label. Colour comes from theme keys so badges stay themeable. See
/// design/CLAVIS-DESIGN-LANGUAGE.md §9.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module Badge =

    let private chipPadding = Thickness(7.0, 2.0, 7.0, 2.0)

    let private caption (text: string) (foregroundKey: string) : TextBlock =
        let block = TextBlock(Text = text, VerticalAlignment = VerticalAlignment.Center)
        block.SetResourceReference(FrameworkElement.StyleProperty, "Clavis.Text.Micro")
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey)
        block

    let private outlineChip (text: string) (brushKey: string) : Border =
        let border = Border(Padding = chipPadding, BorderThickness = Thickness 1.0, SnapsToDevicePixels = true)
        border.SetResourceReference(Border.BorderBrushProperty, brushKey)
        border.Child <- caption text brushKey
        border

    /// Kind / identity badge: secondary outline + secondary text (a plugin/skill kind, a "kind" label).
    let kind (text: string) : Border = outlineChip text "SecondaryAccentBrush"

    /// Signal badge: a coloured outline + matching text. Pass the signal brush key
    /// ("GreenBrush" | "YellowBrush" | "RedBrush").
    let signal (text: string) (brushKey: string) : Border = outlineChip text brushKey

    /// Neutral category badge: surface fill + body text.
    let neutral (text: string) : Border =
        let border = Border(Padding = chipPadding, SnapsToDevicePixels = true)
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush")
        border.Child <- caption text "BodyBrush"
        border

    /// Status badge: a leading dot (circle) + dim label. Pass the dot colour key (e.g. "GreenBrush").
    let status (text: string) (dotColorKey: string) : StackPanel =
        let panel = StackPanel(Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center)
        let dot = StatusDot.sized dotColorKey 7.0
        dot.Margin <- Thickness(0.0, 0.0, 6.0, 0.0)
        panel.Children.Add dot |> ignore
        panel.Children.Add(caption text "TextDimBrush") |> ignore
        panel
