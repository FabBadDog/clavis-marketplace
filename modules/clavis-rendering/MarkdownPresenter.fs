namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Documents
open System.Windows.Media
open System.Windows.Media.Animation
open System.Windows.Threading
open Markdig
open Markdig.Extensions.Tables
open Markdig.Syntax
open FabioSoft.Process

/// ContentControl that parses a markdown string and renders it as a styled WPF element tree.
[<ExcludeFromCodeCoverage>]
type MarkdownPresenter() as this =
    inherit ContentControl()

    // Plain mutable field, not a dependency property: FontScale is only ever set as a literal XAML
    // attribute (the conversation leaves it at the default), so it needs no binding-target support.
    // The codebase avoids DependencyProperty/static WPF registries; the lone exception is Markdown
    // below, which must be a DP because it is used as a binding target.
    let mutable fontScale = 1.0

    // When false, the content renders without the entrance reveal - used for the live streaming line and the
    // completed blocks under stream-driven display, where the streaming itself is the reveal and a post-hoc
    // animation would replay (and flicker) on every token. Plain mutable field set as a XAML literal.
    let mutable animate = true

    static let textBrush       = Colors.text
    static let brightBrush     = Colors.textBright
    static let dimBrush        = Colors.textDim
    static let clavisBrush     = Colors.clavis
    static let codeBgBrush     = Colors.codeBg
    static let codeBorderBrush = Colors.codeBorder

    // Base font sizes, tuned for the conversation panel's density. Every size is multiplied by the
    // FontScale of the presenter, so a host with more room (e.g. the crash dialog) can enlarge the
    // whole tree without changing these baselines.
    static let baseBodySize = 10.0
    static let baseH1Size   = 13.0
    static let baseH2Size   = 11.5
    static let baseH3Size   = 10.5

    // The code-text baseline is owned by the host's CodeFontSize resource - the single source of truth for
    // every code surface (markdown code, inline code, tool/reasoning output; see WpfHost Theme/Styles.xaml).
    // Read live (then scaled by FontScale) so the one-place size change and any theme override both flow
    // here. The literal is only a fallback for a no-Application context (tests never instantiate this).
    static let baseCodeSize () =
        match Application.Current with
        | null -> 6.0
        | app ->
            match app.TryFindResource("CodeFontSize") with
            | :? float as size -> size
            | _ -> 6.0

    static let pipeline = MarkdownPipelineBuilder().UsePipeTables().Build()

    static let rec addInlines (scale: float) (inlines: InlineCollection) (node: Markdig.Syntax.Inlines.Inline) =

        match node with
        | :? Markdig.Syntax.Inlines.LiteralInline as literal ->
            inlines.Add(Run(literal.Content.ToString()))

        | :? Markdig.Syntax.Inlines.LineBreakInline as lineBreak ->
            if lineBreak.IsHard then inlines.Add(LineBreak())
            else inlines.Add(Run(" "))

        | :? Markdig.Syntax.Inlines.CodeInline as code ->
            let span = Span()
            span.SetResourceReference(TextElement.FontFamilyProperty, FontKeys.MonoFont)
            span.Foreground <- textBrush
            span.FontSize <- baseCodeSize () * scale
            span.Inlines.Add(Run(code.Content))
            inlines.Add(span)

        | :? Markdig.Syntax.Inlines.EmphasisInline as emphasis ->
            let span = Span()
            if emphasis.DelimiterCount >= 2 then
                span.FontWeight <- FontWeights.SemiBold
                span.Foreground <- brightBrush
            else
                span.FontStyle <- FontStyles.Italic
                span.Foreground <- clavisBrush
            for child in emphasis do
                addInlines scale span.Inlines child
            inlines.Add(span)

        | :? Markdig.Syntax.Inlines.LinkInline as link ->
            let url = if isNull link.Url then "" else link.Url
            let hyperlink = Hyperlink()
            hyperlink.Foreground <- clavisBrush
            hyperlink.TextDecorations <- null
            hyperlink.MouseEnter.Add(fun _ -> hyperlink.TextDecorations <- TextDecorations.Underline)
            hyperlink.MouseLeave.Add(fun _ -> hyperlink.TextDecorations <- null)
            if url <> "" then
                try
                    hyperlink.NavigateUri <- Uri(url, UriKind.RelativeOrAbsolute)
                    hyperlink.RequestNavigate.Add(fun e ->
                        ShellLaunch.url e.Uri.AbsoluteUri
                        e.Handled <- true)
                with ex ->
                    Trace.TraceWarning($"Setting hyperlink URI '{url}' failed: {ex.Message}")
            for child in link do
                addInlines scale hyperlink.Inlines child
            inlines.Add(hyperlink)

        | :? Markdig.Syntax.Inlines.ContainerInline as container ->
            for child in container do
                addInlines scale inlines child

        | _ -> ()

    static let buildTextBlock (scale: float) (leaf: LeafBlock) (fontSize: float) (foreground: Brush) (margin: Thickness) =

        let textBlock = TextBlock(
                   FontSize = fontSize,
                   Foreground = foreground,
                   TextWrapping = TextWrapping.Wrap,
                   Margin = margin)
        textBlock.SetValue(TextBlock.LineHeightProperty, fontSize * 1.65)
        textBlock.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.AgentFont)
        if leaf.Inline <> null then
            for node in leaf.Inline do
                addInlines scale textBlock.Inlines node
        CopyMenu.add "Copy" textBlock
        textBlock

    static let makeCodeBlock (scale: float) (text: string) : FrameworkElement =

        let inner = TextBlock(
                      Text = text.TrimEnd(),
                      FontSize = baseCodeSize () * scale,
                      Foreground = textBrush,
                      TextWrapping = TextWrapping.Wrap)
        inner.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.MonoFont)
        CopyMenu.add "Copy code" inner
        Border(
            Child = inner,
            Background = codeBgBrush,
            BorderBrush = codeBorderBrush,
            BorderThickness = Thickness(1.0),
            Padding = Thickness(10.0, 8.0, 10.0, 8.0),
            Margin = Thickness(0.0, 4.0, 0.0, 6.0))
        :> FrameworkElement

    static let makeTableCell (scale: float) (tableCell: TableCell) (isHeader: bool) (columnIndex: int) (rowIndex: int) (columnCount: int) : UIElement =

        let foreground = if isHeader then brightBrush else textBrush
        let innerTextBlock = TextBlock(
                        FontSize = baseBodySize * scale,
                        Foreground = foreground,
                        TextWrapping = TextWrapping.Wrap)
        innerTextBlock.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.AgentFont)
        for child in tableCell do
            match child with
            | :? ParagraphBlock as paragraph ->
                if paragraph.Inline <> null then
                    for node in paragraph.Inline do
                        addInlines scale innerTextBlock.Inlines node
            | _ -> ()
        let borderThickness =
            if isHeader then Thickness(1.0, 1.0, 1.0, 2.0)
            else Thickness(1.0, 0.0, 1.0, 1.0)
        let cell = Border(
                       Child = innerTextBlock,
                       Padding = Thickness(8.0, 5.0, 8.0, 5.0),
                       BorderBrush = codeBorderBrush,
                       BorderThickness = borderThickness,
                       Background = (if isHeader then codeBgBrush :> Brush else Brushes.Transparent))
        Grid.SetRow(cell, rowIndex)
        Grid.SetColumn(cell, min columnIndex (columnCount - 1))
        cell :> UIElement

    static let render (scale: float) (markdown: string) (container: StackPanel) =

        if String.IsNullOrWhiteSpace markdown then () else

        let document = Markdown.Parse(markdown, pipeline)

        for block in document do
            match block with
            | :? HeadingBlock as heading ->
                let size, weight =
                    match heading.Level with
                    | 1 -> baseH1Size * scale, FontWeights.SemiBold
                    | 2 -> baseH2Size * scale, FontWeights.SemiBold
                    | _ -> baseH3Size * scale, FontWeights.Medium
                let foreground = if heading.Level = 1 then clavisBrush :> Brush else brightBrush :> Brush
                let margin =
                    if heading.Level = 1 then Thickness(0.0, 8.0, 0.0, 4.0)
                    else Thickness(0.0, 6.0, 0.0, 2.0)
                let textBlock = buildTextBlock scale heading size foreground margin
                textBlock.FontWeight <- weight
                if heading.Level = 1 then
                    // Snapshot first: setting Run.Text mutates the Inlines collection, which would
                    // invalidate a live enumerator ("Collection was modified").
                    for inlineElement in textBlock.Inlines |> Seq.cast<Inline> |> Seq.toList do
                        match inlineElement with
                        | :? Run as run -> run.Text <- run.Text.ToUpperInvariant()
                        | _ -> ()
                container.Children.Add(textBlock) |> ignore

            | :? ParagraphBlock as paragraph ->
                let textBlock = buildTextBlock scale paragraph (baseBodySize * scale) textBrush (Thickness(0.0, 0.0, 0.0, 5.0))
                container.Children.Add(textBlock) |> ignore

            | :? FencedCodeBlock as code ->
                container.Children.Add(makeCodeBlock scale (code.Lines.ToString())) |> ignore

            | :? CodeBlock as code ->
                container.Children.Add(makeCodeBlock scale (code.Lines.ToString())) |> ignore

            | :? ListBlock as list ->
                let mutable index = 1
                for item in list do
                    match item with
                    | :? ListItemBlock as listItem ->
                        let prefix = if list.IsOrdered then $"{index}." else "•"
                        index <- index + 1
                        let row = Grid(Margin = Thickness(0.0, 1.0, 0.0, 1.0))
                        row.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))
                        row.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1.0, GridUnitType.Star)))
                        let bullet = TextBlock(
                                       Text = prefix,
                                       FontSize = baseBodySize * scale,
                                       Foreground = dimBrush,
                                       VerticalAlignment = VerticalAlignment.Top,
                                       Margin = Thickness(0.0, 0.0, 6.0, 0.0),
                                       MinWidth = 14.0)
                        bullet.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.AgentFont)
                        Grid.SetColumn(bullet, 0)
                        row.Children.Add(bullet) |> ignore
                        let textColumn = StackPanel()
                        Grid.SetColumn(textColumn, 1)
                        for child in listItem do
                            match child with
                            | :? ParagraphBlock as paragraph ->
                                let textBlock = buildTextBlock scale paragraph (baseBodySize * scale) textBrush (Thickness(0.0))
                                textColumn.Children.Add(textBlock) |> ignore
                            | _ -> ()
                        row.Children.Add(textColumn) |> ignore
                        container.Children.Add(row) |> ignore
                    | _ -> ()

            | :? Table as table ->
                let columnCount = table.ColumnDefinitions.Count
                let grid = Grid(Margin = Thickness(0.0, 4.0, 0.0, 8.0))
                for _ in 0 .. columnCount - 1 do
                    grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1.0, GridUnitType.Star)))
                let mutable rowIndex = 0
                for tableRow in table do
                    match tableRow with
                    | :? TableRow as row ->
                        grid.RowDefinitions.Add(RowDefinition(Height = GridLength.Auto))
                        let mutable columnIndex = 0
                        for tableCellBlock in row do
                            match tableCellBlock with
                            | :? TableCell as cell ->
                                grid.Children.Add(makeTableCell scale cell row.IsHeader columnIndex rowIndex columnCount) |> ignore
                                columnIndex <- columnIndex + 1
                            | _ -> ()
                        rowIndex <- rowIndex + 1
                    | _ -> ()
                container.Children.Add(grid) |> ignore

            | _ -> ()

    // How far the line fall lifts the block as it reveals - a small settle, not a big drop, since the
    // top-to-bottom clip carries the "lines arriving in order" reading.
    static let lineFallRise = 8.0

    // The rendered block elements (paragraphs, code blocks, list rows, tables), in order.
    static let blockElements (panel: StackPanel) =
        panel.Children
        |> Seq.cast<UIElement>
        |> Seq.choose (function :? FrameworkElement as element -> Some element | _ -> None)
        |> Seq.toList

    // A staggered fade-in of the rendered blocks, optionally rising into place, finishing within totalMs.
    // Animates UIElement.Opacity / a TranslateTransform - composition-level properties that repaint
    // reliably. (Animating a text Run's foreground brush does NOT re-render the glyphs in this WPF build, so
    // the reveal must work at the element level rather than per-character at the brush level.)
    static let fadeBlocksIn (panel: StackPanel) (totalMs: float) (rise: float) =
        match blockElements panel with
        | [] -> ()
        | blocks ->
            let count = blocks.Length
            let fadeMs = if count <= 1 then totalMs else min totalMs (max 250.0 (totalMs * 0.6))
            let step = if count <= 1 then 0.0 else (totalMs - fadeMs) / float (count - 1)
            blocks
            |> List.iteri (fun index element ->
                let beginTime = Nullable(TimeSpan.FromMilliseconds(float index * step))
                element.Opacity <- 0.0
                element.BeginAnimation(
                    UIElement.OpacityProperty,
                    DoubleAnimation(0.0, 1.0, Duration(TimeSpan.FromMilliseconds fadeMs), BeginTime = beginTime, EasingFunction = Motion.easeOut()))
                if rise > 0.0 then
                    let translate = TranslateTransform(Y = rise)
                    element.RenderTransform <- translate
                    translate.BeginAnimation(
                        TranslateTransform.YProperty,
                        DoubleAnimation(rise, 0.0, Duration(TimeSpan.FromMilliseconds fadeMs), BeginTime = beginTime, EasingFunction = Motion.easeOut())))

    // Every TextBlock under the rendered panel, in reading order (paragraphs, list rows, table cells, code).
    static let collectTextBlocks (root: DependencyObject) =
        let blocks = ResizeArray<TextBlock>()
        let rec walk (node: DependencyObject) =
            match node with
            | :? TextBlock as textBlock -> blocks.Add textBlock
            | _ -> ()
            for index in 0 .. VisualTreeHelper.GetChildrenCount node - 1 do
                walk (VisualTreeHelper.GetChild(node, index))
        walk root
        blocks

    // The character count an inline contributes (a hard line break counts as one).
    static let rec inlineLength (node: Inline) =
        match node with
        | :? Run as run -> run.Text.Length
        | :? Span as span -> span.Inlines |> Seq.cast<Inline> |> Seq.sumBy inlineLength
        | :? LineBreak -> 1
        | _ -> 0

    // Build a staircase clip geometry for a left-to-right, line-by-line reveal at `progress` in [0,1]: lines
    // above the cursor fully shown, the cursor line revealed up to its width fraction. An empty group at
    // progress 0 clips the whole block away, so nothing shows before the wipe starts.
    static let wipeClip (width: float) (lineHeight: float) (lines: int) (progress: float) : Geometry =
        let revealedUnits = progress * float lines
        let fullLines = min lines (int (floor revealedUnits))
        let partialFraction = revealedUnits - float fullLines
        let group = GeometryGroup()
        if fullLines > 0 then
            group.Children.Add(RectangleGeometry(Rect(0.0, 0.0, width, float fullLines * lineHeight)))
        if fullLines < lines && partialFraction > 0.0 then
            group.Children.Add(RectangleGeometry(Rect(0.0, float fullLines * lineHeight, width * partialFraction, lineHeight)))
        group :> Geometry

    // Typewriter (<= 3 lines): the rendered text is drawn once at full content, then revealed left-to-right,
    // line by line, by an animated clip. The earlier per-character inline rebuild updated the text model but
    // never repainted, because a TextBlock stretched to the content-column width keeps the same arranged size
    // whether it holds two characters or two hundred, so WPF short-circuits the relayout. The clip is a
    // composition property (no layout), so it re-composites every frame - the reveal that column-width text
    // needs. Driven by CompositionTarget.Rendering, elapsed-time based so it lands at totalMs at any frame rate.
    static let charWipe (panel: StackPanel) (lineHeight: float) =
        let width = panel.ActualWidth
        let fullHeight = panel.ActualHeight
        let characters =
            collectTextBlocks panel
            |> Seq.sumBy (fun textBlock -> textBlock.Inlines |> Seq.cast<Inline> |> Seq.sumBy inlineLength)
        let totalMs = TextReveal.typewriterDuration characters
        if width <= 0.0 || fullHeight <= 0.0 || totalMs <= 0.0 then
            ()
        else
            let bandHeight = if lineHeight <= 0.0 then fullHeight else min lineHeight fullHeight
            let lines = max 1 (int (Math.Round(fullHeight / bandHeight)))
            panel.Clip <- wipeClip width bandHeight lines 0.0
            let stopwatch = Stopwatch()
            let mutable handler = Unchecked.defaultof<EventHandler>
            handler <-
                EventHandler(fun _ _ ->
                    if not stopwatch.IsRunning then
                        stopwatch.Start()
                    let elapsed = stopwatch.Elapsed.TotalMilliseconds
                    if elapsed >= totalMs then
                        CompositionTarget.remove_Rendering handler
                        panel.Clip <- null
                    else
                        panel.Clip <- wipeClip width bandHeight lines (elapsed / totalMs))
            CompositionTarget.add_Rendering handler

    // Line fall (> 3 lines): the first line fades in, then the block reveals top-to-bottom under a growing
    // clip while it settles up the last few pixels, so the lower lines appear to slide into place from behind
    // the first - all in LineFallTotalMs. Clip and transform act purely visually (no layout reflow) and are
    // released on completion so resize re-wraps normally. The first-line fade reads as a first-line fade
    // because the clip hides everything below it at the start.
    static let lineFall (panel: StackPanel) (firstLineHeight: float) =
        let width = panel.ActualWidth
        let fullHeight = panel.ActualHeight
        if width <= 0.0 || fullHeight <= 0.0 then
            fadeBlocksIn panel TextReveal.LineFallTotalMs lineFallRise
        else
            let startHeight = min firstLineHeight fullHeight
            let total = Duration(TimeSpan.FromMilliseconds TextReveal.LineFallTotalMs)
            let clip = RectangleGeometry(Rect(0.0, 0.0, width, startHeight))
            let translate = TranslateTransform(Y = lineFallRise)
            panel.Clip <- clip
            panel.RenderTransform <- translate
            panel.Opacity <- 0.0
            let reveal =
                RectAnimation(
                    Rect(0.0, 0.0, width, startHeight), Rect(0.0, 0.0, width, fullHeight),
                    total, EasingFunction = Motion.easeOut())
            reveal.Completed.Add(fun _ ->
                panel.BeginAnimation(UIElement.OpacityProperty, null)
                panel.Opacity <- 1.0
                panel.Clip <- null
                panel.RenderTransform <- null)
            let fade = DoubleAnimation(0.0, 1.0, Motion.Standard, EasingFunction = Motion.easeOut())
            let rise = DoubleAnimation(lineFallRise, 0.0, total, EasingFunction = Motion.easeOut())
            panel.BeginAnimation(UIElement.OpacityProperty, fade)
            translate.BeginAnimation(TranslateTransform.YProperty, rise)
            clip.BeginAnimation(RectangleGeometry.RectProperty, reveal)

    // Force the rendered response fully visible - the backstop after the animation window so a reveal that
    // is interrupted (e.g. the row re-laid-out mid-reveal) never leaves the response hidden. Element-level
    // only, matching how the reveal animates.
    static let forceVisible (panel: StackPanel) =
        panel.BeginAnimation(UIElement.OpacityProperty, null)
        panel.Opacity <- 1.0
        panel.Clip <- null
        panel.RenderTransform <- null
        for element in blockElements panel do
            element.BeginAnimation(UIElement.OpacityProperty, null)
            element.Opacity <- 1.0
            element.RenderTransform <- null

    // The longest a reveal can run - the slower of the line fall and the longest typewriter - plus a margin:
    // the deadline after which the response must be fully visible no matter what. Must clear the slowest
    // reveal or it would force the text visible mid-type and cut a long type-out short.
    static let revealSafetyNet =
        TimeSpan.FromMilliseconds(max TextReveal.LineFallTotalMs TextReveal.TypewriterMaxMs + 400.0)

    // The text reveal: a short block types out left-to-right (<= 3 lines, the char wipe); a longer one
    // reveals top-to-bottom (the line fall). Classified by the laid-out wrapped-line count against the body
    // line height.
    static let reveal (panel: StackPanel) (lineHeight: float) =
        try
            let lines = TextReveal.lineCount panel.ActualHeight lineHeight
            if TextReveal.isShort lines then
                charWipe panel lineHeight
            else
                lineFall panel lineHeight
        with ex ->
            Trace.TraceWarning($"Text reveal failed, showing response unanimated: {ex.Message}")
            forceVisible panel
        // Safety net: guarantee the response is fully visible once the animation window has passed - a no-op
        // after a normal reveal, the backstop if an animation is interrupted before reaching its hold value.
        let timer = DispatcherTimer(Interval = revealSafetyNet)
        timer.Tick.Add(fun _ ->
            timer.Stop()
            forceVisible panel)
        timer.Start()

    static let markdownProperty =
        DependencyProperty.Register(
            "Markdown", typeof<string>, typeof<MarkdownPresenter>,
            PropertyMetadata("", PropertyChangedCallback(fun dependency _ ->
                let presenter = dependency :?> MarkdownPresenter
                presenter.RenderContent())))

    // Display-only: a rendered response is never an interactive control, so it must not be a keyboard
    // focus target - no tab stop, no click-focus, no focus ring. This keeps Tab traversal and the focus
    // visuals on genuinely interactive controls.
    do this.Focusable <- false

    static member MarkdownProperty = markdownProperty

    member this.Markdown
        with get () = this.GetValue(markdownProperty) :?> string
        and set (value: string) = this.SetValue(markdownProperty, value)

    member _.Animate
        with get () = animate
        and set value = animate <- value

    member this.FontScale
        with get () = fontScale
        and set value =
            fontScale <- value
            this.RenderContent()

    member private this.RenderContent() =

        let panel = StackPanel()
        let markdown = this.Markdown
        if not (String.IsNullOrWhiteSpace(markdown)) then
            render fontScale markdown panel
            // Run the reveal once, when the rendered panel first enters the visual tree (so its size is
            // laid out). The body line height drives the short/long classification.
            if animate then
                let lineHeight = baseBodySize * fontScale * 1.65
                let mutable revealed = false
                panel.Loaded.Add(fun _ ->
                    if not revealed then
                        revealed <- true
                        reveal panel lineHeight)
        this.Content <- panel
