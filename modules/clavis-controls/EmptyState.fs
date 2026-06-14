namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// A centred empty-state: a dim uppercase headline (label role, in the faint line colour) over a body
/// sub-line. No illustration. The shared "nothing here / no selection / no results" treatment so every
/// panel's empty view reads the same. See design/CLAVIS-DESIGN-LANGUAGE.md §9.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module EmptyState =

    let create (headline: string) (detail: string) : StackPanel =
        let panel =
            StackPanel(
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center)

        let head =
            TextBlock(
                Text = headline,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = Thickness(0.0, 0.0, 0.0, 8.0))
        head.SetResourceReference(FrameworkElement.StyleProperty, "Clavis.Text.Label")
        head.SetResourceReference(TextBlock.ForegroundProperty, "LineBrush")

        let sub =
            TextBlock(
                Text = detail,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap)
        sub.SetResourceReference(FrameworkElement.StyleProperty, "Clavis.Text.Body")

        panel.Children.Add head |> ignore
        panel.Children.Add sub |> ignore
        panel
