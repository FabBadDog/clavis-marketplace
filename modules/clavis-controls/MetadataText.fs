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

    [<Literal>]
    let private defaultSize = 10.0

    let private make (text: string) (foregroundKey: string) (size: float) : TextBlock =
        let block = TextBlock(Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center)
        block.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey)
        block

    let create (text: string) : TextBlock = make text "TextDimBrush" defaultSize

    let sized (text: string) (size: float) : TextBlock = make text "TextDimBrush" size

    let accent (text: string) : TextBlock = make text "ClavisBrush" defaultSize

    let accentSized (text: string) (size: float) : TextBlock = make text "ClavisBrush" size
