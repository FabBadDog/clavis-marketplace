namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// Monospace metadata labels - durations, timestamps, authors, identifiers - so the small print reads the
/// same across the git log, the editor status bar, and the binding list. Dim by default; `accent` uses the
/// clavis brush for an identifier that should pop (a commit hash, a gesture).
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module MetadataText =

    let private make (text: string) (foregroundKey: string) (size: float) : TextBlock =
        let block = TextBlock(Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center)
        block.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey)
        block

    // The default (no explicit size) variants take their size from the theme's `meta` role key, so all the
    // small print across the app moves on the dual scale from one place. The `*Sized` variants keep an
    // explicit size for the rare case a caller needs to deviate.
    let private makeRole (text: string) (foregroundKey: string) : TextBlock =
        let block = TextBlock(Text = text, VerticalAlignment = VerticalAlignment.Center)
        block.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey)
        block.SetResourceReference(TextBlock.FontSizeProperty, "MetaFontSize")
        block

    let create (text: string) : TextBlock = makeRole text "TextDimBrush"

    let sized (text: string) (size: float) : TextBlock = make text "TextDimBrush" size

    let accent (text: string) : TextBlock = makeRole text "ClavisBrush"

    let accentSized (text: string) (size: float) : TextBlock = make text "ClavisBrush" size
