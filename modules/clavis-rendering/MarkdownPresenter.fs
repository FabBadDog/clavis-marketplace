namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Documents
open System.Windows.Media
open System.Windows.Media.Animation
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

    static let textBrush       = Colors.text
    static let brightBrush     = Colors.textBright
    static let dimBrush        = Colors.textDim
    static let clavisBrush     = Colors.clavis
    static let codeBgBrush     = Colors.codeBg
    static let codeBorderBrush = Colors.codeBorder

    // Base font sizes, tuned for the conversation panel's density. Every size is multiplied by the
    // FontScale of the presenter, so a host with more room (e.g. the crash dialog) can enlarge the
    // whole tree without changing these baselines. baseCodeSize mirrors the host's CodeFontSize
    // resource (WpfHost Theme/Styles.xaml) - change both together; it cannot be a resource lookup here
    // because it scales with FontScale.
    static let baseBodySize = 10.0
    static let baseCodeSize = 7.5
    static let baseH1Size   = 13.0
    static let baseH2Size   = 11.5
    static let baseH3Size   = 10.5

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
            span.FontSize <- baseCodeSize * scale
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
                      FontSize = baseCodeSize * scale,
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

    // The text reveal: rather than the rendered response appearing all at once ("poof"), each top-level
    // block (heading, paragraph, code block, list row, table) fades up into place, staggered, so the answer
    // cascades in. A block-level line-fall - the per-character wave from the design mockups would need a
    // bespoke glyph-animating text control; this gives the same cascade feel from the elements Markdig
    // already produces. The stagger is capped so a long answer still finishes promptly.
    static let revealBlocks (panel: StackPanel) =
        let count = panel.Children.Count
        if count = 0 then
            ()
        else
            let step = min 45.0 (1200.0 / float count)
            let mutable index = 0
            for child in panel.Children do
                match child with
                | :? FrameworkElement as element ->
                    let translate = TranslateTransform(Y = 6.0)
                    element.RenderTransform <- translate
                    element.Opacity <- 0.0
                    let delay = Nullable(TimeSpan.FromMilliseconds(float index * step))
                    let fade = DoubleAnimation(0.0, 1.0, Motion.Standard, BeginTime = delay, EasingFunction = Motion.easeOut())
                    let rise = DoubleAnimation(6.0, 0.0, Motion.Standard, BeginTime = delay, EasingFunction = Motion.easeOut())
                    element.BeginAnimation(UIElement.OpacityProperty, fade)
                    translate.BeginAnimation(TranslateTransform.YProperty, rise)
                    index <- index + 1
                | _ -> ()

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
            // Run the cascade once, when the rendered panel first enters the visual tree.
            let mutable revealed = false
            panel.Loaded.Add(fun _ ->
                if not revealed then
                    revealed <- true
                    revealBlocks panel)
        this.Content <- panel
