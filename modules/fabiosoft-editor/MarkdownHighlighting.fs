namespace FabioSoft.Editor

open System
open System.Diagnostics.CodeAnalysis
open System.Text.RegularExpressions
open System.Windows
open System.Windows.Media
open ICSharpCode.AvalonEdit.Rendering

/// What a styled run of markdown source represents. The colorizer maps each token to the concrete
/// foreground / typeface / size that mirrors how Clavis renders markdown elsewhere (MarkdownPresenter).
type MarkdownToken =
    | Body
    | Heading of level: int
    | Marker
    | Bold
    | Italic
    | InlineCode
    | Link
    | ListBullet
    | CodeFence
    | CodeText

/// A styled run within a single source line, offsets relative to the line start.
type MarkdownSpan = { Start: int; Length: int; Token: MarkdownToken }

/// One source line's styling: a base run covering the whole line, plus decorations layered over it.
type MarkdownLineStyle = { Base: MarkdownToken; Decorations: MarkdownSpan list }

/// Pure markdown-source classification: given a line (and whether it sits inside a fenced code block),
/// decide how each part should be styled. The colorizer below turns these tokens into real brushes and
/// typefaces; keeping the decision logic here makes it testable without WPF.
[<RequireQualifiedAccess>]
module MarkdownSyntax =

    [<Literal>]
    let private fence = "```"

    let isFenceDelimiter (text: string) =
        text.TrimStart().StartsWith(fence)

    let isInsideFence (precedingLines: string seq) =
        (precedingLines |> Seq.filter isFenceDelimiter |> Seq.length) % 2 = 1

    let private headingPattern = Regex(@"^(#{1,6})(\s+)(\S.*)$")
    let private listPattern = Regex(@"^(\s*)([-*+]|\d+\.)(\s+)(.*)$")
    let private quotePattern = Regex(@"^(\s*>)(\s?)(.*)$")

    /// Find inline constructs (code, bold, italic, links) in `text`, returning marker + content spans
    /// shifted by `offset` (the column where `text` begins within the whole line).
    let inlineDecorations (offset: int) (text: string) : MarkdownSpan list =

        let span start length token = { Start = offset + start; Length = length; Token = token }
        let length = text.Length

        let codeAt index =
            let close = text.IndexOf('`', index + 1)
            if close > index then
                Some(close + 1, [ span index 1 Marker; span (index + 1) (close - index - 1) InlineCode; span close 1 Marker ])
            else
                None

        let emphasisAt index (delimiter: char) =
            let isDouble = index + 1 < length && text[index + 1] = delimiter
            let marker = String(delimiter, (if isDouble then 2 else 1))
            let contentStart = index + marker.Length
            let close = text.IndexOf(marker, contentStart)
            if close >= contentStart then
                let token = if isDouble then Bold else Italic
                Some(
                    close + marker.Length,
                    [ span index marker.Length Marker
                      span contentStart (close - contentStart) token
                      span close marker.Length Marker ])
            else
                None

        let linkAt index =
            let closeText = text.IndexOf(']', index + 1)
            if closeText > index && closeText + 1 < length && text[closeText + 1] = '(' then
                let closeUrl = text.IndexOf(')', closeText + 2)
                if closeUrl > closeText then
                    Some(
                        closeUrl + 1,
                        [ span index 1 Marker
                          span (index + 1) (closeText - index - 1) Link
                          span closeText (closeUrl - closeText + 1) Marker ])
                else
                    None
            else
                None

        let rec scan index acc =
            if index >= length then
                List.rev acc
            else
                let matched =
                    match text[index] with
                    | '`' -> codeAt index
                    | '*' | '_' -> emphasisAt index text[index]
                    | '[' -> linkAt index
                    | _ -> None
                match matched with
                | Some(next, spans) -> scan next (List.rev spans @ acc)
                | None -> scan (index + 1) acc

        scan 0 []

    let styleLine (insideFence: bool) (text: string) : MarkdownLineStyle =

        let fenceLine = { Base = CodeFence; Decorations = [] }

        if insideFence then
            if isFenceDelimiter text then fenceLine
            else { Base = CodeText; Decorations = [] }
        elif isFenceDelimiter text then
            fenceLine
        else
            let heading = headingPattern.Match text
            let list = listPattern.Match text
            let quote = quotePattern.Match text
            if heading.Success then
                let hashes = heading.Groups[1].Length
                let contentStart = hashes + heading.Groups[2].Length
                { Base = Heading hashes
                  Decorations =
                    { Start = 0; Length = hashes; Token = Marker } :: inlineDecorations contentStart heading.Groups[3].Value }
            elif list.Success then
                let bulletStart = list.Groups[1].Length
                let bulletLength = list.Groups[2].Length
                let contentStart = bulletStart + bulletLength + list.Groups[3].Length
                { Base = Body
                  Decorations =
                    { Start = bulletStart; Length = bulletLength; Token = ListBullet }
                    :: inlineDecorations contentStart list.Groups[4].Value }
            elif quote.Success then
                let markerLength = quote.Groups[1].Length
                let contentStart = markerLength + quote.Groups[2].Length
                { Base = Body
                  Decorations =
                    { Start = 0; Length = markerLength; Token = Marker } :: inlineDecorations contentStart quote.Groups[3].Value }
            else
                { Base = Body; Decorations = inlineDecorations 0 text }

/// The brushes and heading size ratios the colorizer paints with. These mirror clavis-rendering's
/// Colors and MarkdownPresenter's base sizes (H1 13 / H2 11.5 / H3 10.5 against a body of 10), but are
/// baked here so FabioSoft.Editor stays an application-neutral library with no Clavis dependency - the
/// same reason EditorTheme bakes its own colours. Sizes are ratios so the body anchors to the editor's
/// own font size rather than the conversation panel's dense baseline.
[<ExcludeFromCodeCoverage>]
[<RequireQualifiedAccess>]
module internal MarkdownTheme =

    let private freeze (brush: SolidColorBrush) =
        brush.Freeze()
        brush

    let private rgb red green blue =
        SolidColorBrush(Color.FromRgb(red, green, blue)) |> freeze

    let text   = rgb 0xC8uy 0xC8uy 0xD0uy
    let bright = rgb 0xE8uy 0xE8uy 0xECuy
    let dim    = rgb 0x9Auy 0x9Auy 0xA4uy
    let clavis = rgb 0x9Fuy 0xD5uy 0xF0uy
    let codeBg = rgb 0x14uy 0x14uy 0x1Cuy

    [<Literal>]
    let H1Ratio = 1.3

    [<Literal>]
    let H2Ratio = 1.15

    [<Literal>]
    let H3Ratio = 1.05

/// Renders markdown *source* so it reads like the rendered output: Inter (AgentFont) for prose, JetBrains
/// Mono (MonoFont) for code, clavis-blue H1, bright H2/H3, dim markers - matching MarkdownPresenter. Block
/// tokens set the family + size + foreground; inline tokens only adjust weight/style/colour so they inherit
/// the heading or body size around them. Lives in the Default-ALC editor module beside CodeEditor; gated by
/// isActive so it no-ops on non-markdown files.
[<ExcludeFromCodeCoverage>]
type internal MarkdownColorizer
    (isActive: unit -> bool, bodySize: unit -> float, agentFamily: unit -> FontFamily, monoFamily: unit -> FontFamily) =
    inherit DocumentColorizingTransformer()

    let typeface (family: FontFamily) weight style =
        Typeface(family, style, weight, FontStretches.Normal)

    let headingSize body level =
        match level with
        | 1 -> body * MarkdownTheme.H1Ratio
        | 2 -> body * MarkdownTheme.H2Ratio
        | _ -> body * MarkdownTheme.H3Ratio

    let applyBase (element: VisualLineElement) token body agent mono =
        let props = element.TextRunProperties
        match token with
        | Heading level ->
            let weight = if level <= 2 then FontWeights.SemiBold else FontWeights.Medium
            let foreground = if level = 1 then MarkdownTheme.clavis else MarkdownTheme.bright
            props.SetTypeface(typeface agent weight FontStyles.Normal)
            props.SetFontRenderingEmSize(headingSize body level)
            props.SetForegroundBrush(foreground)
        | CodeFence ->
            props.SetTypeface(typeface mono FontWeights.Normal FontStyles.Normal)
            props.SetFontRenderingEmSize(body)
            props.SetForegroundBrush(MarkdownTheme.dim)
        | CodeText ->
            props.SetTypeface(typeface mono FontWeights.Normal FontStyles.Normal)
            props.SetFontRenderingEmSize(body)
            props.SetForegroundBrush(MarkdownTheme.text)
        | _ ->
            props.SetTypeface(typeface agent FontWeights.Normal FontStyles.Normal)
            props.SetFontRenderingEmSize(body)
            props.SetForegroundBrush(MarkdownTheme.text)

    let applyDecoration (element: VisualLineElement) token agent mono =
        let props = element.TextRunProperties
        match token with
        | Marker
        | ListBullet -> props.SetForegroundBrush(MarkdownTheme.dim)
        | Bold ->
            props.SetTypeface(typeface agent FontWeights.SemiBold FontStyles.Normal)
            props.SetForegroundBrush(MarkdownTheme.bright)
        | Italic ->
            props.SetTypeface(typeface agent FontWeights.Normal FontStyles.Italic)
            props.SetForegroundBrush(MarkdownTheme.clavis)
        | InlineCode ->
            props.SetTypeface(typeface mono FontWeights.Normal FontStyles.Normal)
            props.SetForegroundBrush(MarkdownTheme.text)
            props.SetBackgroundBrush(MarkdownTheme.codeBg)
        | Link -> props.SetForegroundBrush(MarkdownTheme.clavis)
        | _ -> ()

    override this.ColorizeLine(line) =
        if not (isActive ()) then
            ()
        else
            let document = this.CurrentContext.Document
            // Fence state needs the lines above; scanning each time is O(line) and fine for source-file sizes.
            let precedingLines =
                seq { for number in 1 .. line.LineNumber - 1 -> document.GetText(document.GetLineByNumber number) }
            let insideFence = MarkdownSyntax.isInsideFence precedingLines
            let style = MarkdownSyntax.styleLine insideFence (document.GetText line)
            let body = bodySize ()
            let agent = agentFamily ()
            let mono = monoFamily ()
            this.ChangeLinePart(line.Offset, line.EndOffset, (fun element -> applyBase element style.Base body agent mono))
            for decoration in style.Decorations do
                let start = line.Offset + decoration.Start
                let finish = min line.EndOffset (start + decoration.Length)
                if finish > start then
                    this.ChangeLinePart(start, finish, (fun element -> applyDecoration element decoration.Token agent mono))
