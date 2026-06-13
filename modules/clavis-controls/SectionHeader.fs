namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// A group header: dim, slightly spaced label text that introduces a section of a list or form, so every
/// grouped list reads with the same heading. UI font, dim, with the standard above/below spacing.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module SectionHeader =

    [<Literal>]
    let private headerSize = 9.0

    let create (text: string) : TextBlock =
        let header =
            TextBlock(
                Text = text,
                FontSize = headerSize,
                FontWeight = FontWeights.Medium,
                Margin = Thickness(0.0, 10.0, 0.0, 4.0))
        header.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush")
        header
