namespace FabioSoft.Clavis.Rendering

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation
open System.Windows.Shapes
open FabioSoft.Clavis.Placeholders

/// Renders a placeholder template into a horizontal run of inline elements: literal/value text as
/// TextBlocks, and the `bar`/`badge`/`microstat`/`limitPlane`/`color` components as small controls. The pure
/// parse/resolve/format logic lives in clavis-placeholders (unit-tested); this is the WPF projection only.
/// The host pushes fresh values via SetValues and (for limitPlane) limit windows via SetLimitWindows.
[<ExcludeFromCodeCoverage>] // WPF construction; engine logic is unit-tested in clavis-placeholders
type PlaceholderStrip() =
    static let engine = PlaceholderEngine()

    static let microSize = 10.0
    static let iconSize = 11.0
    static let barWidth = 90.0
    static let barHeight = 3.0
    static let iconGap = Thickness(0.0, 0.0, 4.0, 0.0)
    static let barMargin = Thickness(2.0, 0.0, 2.0, 0.0)

    static let colorByName name : Brush =
        match name with
        | "accent" | "clavis" -> Colors.clavis :> Brush
        | "yellow" -> Colors.yellow :> Brush
        | "green" -> Colors.green :> Brush
        | "purple" | "periwinkle" | "secondary" -> Colors.secondary :> Brush
        | "dim" -> Colors.textDim :> Brush
        | _ -> Colors.text :> Brush

    let panel =
        StackPanel(Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center)

    let mutable template = ""
    let mutable hideUnresolvedValues = false

    // Responsive density, driven by a host container (ResponsiveZoneBar): the bar shrinks toward a floor,
    // chrome literals and stat-row icons can be dropped, and path values shortened, before the container
    // resorts to scaling the whole strip. Defaults render everything at full size.
    let mutable barScale = 1.0
    let mutable showLiterals = true
    let mutable shortenPaths = false
    let mutable showStatIcons = true

    let contentChanged = Event<unit>()

    let mutable values: IReadOnlyDictionary<string, string> =
        Dictionary<string, string>() :> IReadOnlyDictionary<string, string>
    let mutable limitWindows: LimitWindow list = []
    let planeViews = List<LimitsPlaneView>()

    // Invoked when the user clicks a rendered {limitPlane}. The host wires this to open the usage-limits
    // panel; without it the plane is inert (no default action baked into the shared renderer).
    let mutable onLimitPlaneClick: (unit -> unit) option = None

    // The previous render's per-segment content keys, so a value update can animate exactly the segments
    // whose content changed (e.g. the effort level after the agent confirms a switch).
    let mutable previousKeys: string[] = [||]

    // The previous render's bar percents, in bar order, so a changed bar can slide its fill from the old
    // value to the new one rather than doing the entrance slide the other segments use.
    let mutable previousBarPercents: float[] = [||]

    // A path value is recognised by its separators (works whatever key the user configured); shortened to
    // its root and leaf with an ellipsis between, so "~\Repos\FS\clavis" reads as "~\…\clavis".
    let looksLikePath (text: string) =
        text.Contains '\\' || text.Contains '/'

    let shortenPath (text: string) =
        let parts = text.Split([| '\\'; '/' |], StringSplitOptions.RemoveEmptyEntries)
        if parts.Length <= 2 then
            text
        else
            parts[0] + "\\…\\" + parts[parts.Length - 1]

    // A "label" literal is chrome worth dropping when space is tight (e.g. "ctx", "CLAVIS"); a literal with
    // no letters is a separator between values (e.g. the "/" in "0/200k") and is kept, so dropping chrome
    // never fuses two values into one unreadable run.
    let isLabelLiteral (text: string) =
        text |> Seq.exists Char.IsLetter

    let monoText (text: string) (brush: Brush) =
        let block =
            TextBlock(Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = microSize, Foreground = brush)
        block.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.MonoFont)
        block :> UIElement

    // Dim, not body text: status-strip literals and values ("ctx", "0/200k") are chrome, and the
    // brighter Colors.text in bold mono read as white against the conversation body.
    let valueText (text: string) =
        monoText text Colors.textDim

    // A progress bar: track + clavis fill. Returns the fill rectangle and the clamped percent alongside the
    // element so the renderer can slide the fill to a changed value (the user's rule: the bar slides its
    // value, it does not do the segments' entrance slide).
    let effectiveBarWidth () = barWidth * barScale

    let barWidthFor percent = effectiveBarWidth () * (max 0.0 (min 100.0 percent)) / 100.0

    let bar (percent: float) =
        let clamped = max 0.0 (min 100.0 percent)
        let grid =
            Grid(Width = effectiveBarWidth (), Height = barHeight, VerticalAlignment = VerticalAlignment.Center, Margin = barMargin)
        grid.Children.Add(Rectangle(Fill = Colors.track)) |> ignore
        let fill =
            Rectangle(
                Fill = Colors.clavis,
                Width = barWidthFor clamped,
                HorizontalAlignment = HorizontalAlignment.Left)
        grid.Children.Add fill |> ignore
        (grid :> UIElement), fill, clamped

    // A chip rendered through the shared BadgeTemplate - the single badge definition used across the app.
    // Empty, "default" and "none" render nothing; any other value (e.g. a model name) gets a neutral chip
    // instead of bare text, so {badge:agent.modelName} reads as a badge like every other badge.
    let badge (value: string) =
        match ModeAccent.resourceKey value with
        | None -> None
        | Some accentKey ->
            let host =
                ContentControl(
                    Content = BadgeViewModel(value.ToUpperInvariant(), accentKey),
                    VerticalAlignment = VerticalAlignment.Center)
            host.SetResourceReference(ContentControl.ContentTemplateProperty, "BadgeTemplate")
            Some(host :> UIElement)

    let microstat (iconName: string) (value: string) =
        let row = StackPanel(Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center)
        if showStatIcons then
            let icon = StatIcon.Create(iconName, iconSize, Colors.textDim)
            icon.VerticalAlignment <- VerticalAlignment.Center
            icon.Margin <- iconGap
            row.Children.Add icon |> ignore
        row.Children.Add(monoText value Colors.textDim) |> ignore
        row :> UIElement

    let limitPlane () =
        let view = LimitsPlane.CreateGlyph()
        view.Update limitWindows
        planeViews.Add view
        // A transparent Border makes the whole glyph hit-testable (the plane draws with a null fill, so
        // clicks would otherwise pass straight through) and gives the click its hand cursor.
        let host =
            Border(
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = ContentControl(Content = view.Element, VerticalAlignment = VerticalAlignment.Center))
        host.MouseLeftButtonUp.Add(fun _ -> onLimitPlaneClick |> Option.iter (fun handler -> handler ()))
        host :> UIElement

    // The bar is built directly in render (it needs its fill captured to animate the value), so it is not
    // listed here.
    let renderComponent (resolved: ResolvedComponent) =
        match resolved.Component.ToLowerInvariant() with
        | "badge" -> badge resolved.Value
        | "microstat" -> Some(microstat (defaultArg (Option.ofObj resolved.Arg) "") resolved.Value)
        | "limitplane" -> Some(limitPlane ())
        | "color" ->
            if resolved.Value = "" then
                None
            else
                Some(monoText resolved.Value (colorByName (defaultArg (Option.ofObj resolved.Arg) "")))
        | _ -> None

    // Renders the resolved segments; with animateChanges, a segment whose content differs from the
    // previous render enters animated (fade + slide via Motion.enter) so a changed value - a confirmed
    // model/effort/mode switch, say - is visibly acknowledged. The bar is the exception: it slides its fill
    // from the old percent to the new one instead of doing the entrance slide. Animation is per-segment and
    // positional: when the segment count changes (template change, badge appearing/disappearing) nothing
    // animates.
    let render (animateChanges: bool) =
        panel.Children.Clear()
        planeViews.Clear()
        let rendered = List<string * UIElement>()
        let barIndices = HashSet<int>()
        let barFills = List<Rectangle * float>()
        for segment in engine.Resolve(template, values) do
            let keyed =
                match segment with
                | :? ResolvedText as t ->
                    if t.Text = "" || (hideUnresolvedValues && t.Unresolved) then
                        None
                    elif not t.IsValue && not showLiterals && isLabelLiteral t.Text then
                        None
                    else
                        let shown =
                            if t.IsValue && shortenPaths && looksLikePath t.Text then
                                shortenPath t.Text
                            else
                                t.Text
                        Some(shown, valueText shown)
                | :? ResolvedComponent as c ->
                    if hideUnresolvedValues && c.Unresolved then
                        None
                    elif c.Component.ToLowerInvariant() = "bar" then
                        let element, fill, percent = bar (if c.Number.HasValue then c.Number.Value else 0.0)
                        barIndices.Add rendered.Count |> ignore
                        barFills.Add((fill, percent))
                        Some($"{c.Component}:{c.Value}", element)
                    else
                        renderComponent c |> Option.map (fun e -> $"{c.Component}:{c.Value}", e)
                | _ -> None
            match keyed with
            | Some (key, e) ->
                rendered.Add((key, e))
                panel.Children.Add e |> ignore
            | None -> ()

        // Non-bar segments whose content changed get the entrance slide; bars are skipped here.
        if animateChanges && previousKeys.Length = rendered.Count then
            for i in 0 .. rendered.Count - 1 do
                let key, element = rendered[i]
                match element with
                | :? FrameworkElement as fe when previousKeys[i] <> key && not (barIndices.Contains i) -> Motion.enter fe
                | _ -> ()

        // A changed bar slides its fill from the previous percent to the new one (the bar's own animation).
        if animateChanges && previousBarPercents.Length = barFills.Count then
            for i in 0 .. barFills.Count - 1 do
                let fill, newPercent = barFills[i]
                let oldPercent = previousBarPercents[i]
                if oldPercent <> newPercent then
                    fill.Width <- barWidthFor oldPercent
                    fill.BeginAnimation(
                        FrameworkElement.WidthProperty,
                        DoubleAnimation(barWidthFor oldPercent, barWidthFor newPercent, Motion.Standard, EasingFunction = Motion.easeOut()))

        previousKeys <- rendered |> Seq.map fst |> Array.ofSeq
        previousBarPercents <- barFills |> Seq.map snd |> Array.ofSeq

    member _.Element = panel

    /// When set, a token whose value key has no published value renders as nothing instead of its raw
    /// "{...}" text (and a component with an unresolved value key renders no empty chrome) - the status
    /// line's policy while provider plugins are still coming up. The template editor keeps the default
    /// verbatim rendering as authoring feedback.
    member _.HideUnresolvedValues
        with get () = hideUnresolvedValues
        and set value =
            hideUnresolvedValues <- value
            render false

    member _.SetTemplate(value: string) =
        template <- value
        render false
        contentChanged.Trigger()

    member _.SetValues(snapshot: IReadOnlyDictionary<string, string>) =
        values <- snapshot
        render true
        contentChanged.Trigger()

    /// Raised after a template or value change re-renders the strip (not on a density change), so a host
    /// container can re-evaluate how much room the new content needs.
    member _.ContentChanged = contentChanged.Publish

    /// Applies a responsive density: a bar scale (1.0 = full width), and whether to keep chrome literals,
    /// shorten path values, and show the stat-row icons. Re-renders only when something actually changes.
    member _.SetDensity(barScaleValue, literalsVisible, shortenPathValues, statIconsVisible) =
        let changed =
            barScale <> barScaleValue
            || showLiterals <> literalsVisible
            || shortenPaths <> shortenPathValues
            || showStatIcons <> statIconsVisible
        if changed then
            barScale <- barScaleValue
            showLiterals <- literalsVisible
            shortenPaths <- shortenPathValues
            showStatIcons <- statIconsVisible
            render false

    member _.SetLimitWindows(windows: LimitWindow seq) =
        limitWindows <- List.ofSeq windows
        for view in planeViews do
            view.Update limitWindows

    /// Wires the click action for a rendered {limitPlane} (the host opens the usage-limits panel). Passing
    /// null clears it.
    member _.SetLimitPlaneClick(handler: Action) =
        onLimitPlaneClick <-
            match box handler with
            | null -> None
            | _ -> Some(fun () -> handler.Invoke())
