namespace FabioSoft.Clavis.Rendering

open System.Diagnostics.CodeAnalysis
open System.Windows.Media

/// Theme primitives the rendering controls need. This library is a standalone module used
/// by host and plugins alike, so it carries its own brushes and font-resource keys rather than
/// depending on any one shell's theme. The font keys must match the keys the hosting application
/// registers in its resource dictionaries (e.g. WpfHost's Styles.xaml).
[<ExcludeFromCodeCoverage>]
[<RequireQualifiedAccess>]
module internal Colors =

    let private freeze brush =
        (brush: SolidColorBrush).Freeze()
        brush

    let private brushFromRgb red green blue =
        SolidColorBrush(Color.FromRgb(red, green, blue)) |> freeze

    let text       = brushFromRgb 0xC8uy 0xC8uy 0xD0uy
    let textBright = brushFromRgb 0xE8uy 0xE8uy 0xECuy
    let textDim    = brushFromRgb 0xB0uy 0xB0uy 0xBAuy
    let clavis     = brushFromRgb 0x9Fuy 0xD5uy 0xF0uy
    let codeBg     = brushFromRgb 0x14uy 0x14uy 0x1Cuy
    let codeBorder = brushFromRgb 0x28uy 0x28uy 0x34uy

    // Palette accents (the design system's secondary/state colours) and the structural bar-track shade.
    let secondary  = brushFromRgb 0xADuy 0xA6uy 0xF2uy
    let green      = brushFromRgb 0x7Buy 0xD4uy 0x9Buy
    let yellow     = brushFromRgb 0xE4uy 0xC4uy 0x7Euy
    let track      = brushFromRgb 0x1Buy 0x1Buy 0x22uy

[<RequireQualifiedAccess>]
module internal FontKeys =

    [<Literal>]
    let AgentFont = "AgentFont"

    [<Literal>]
    let MonoFont = "MonoFont"
