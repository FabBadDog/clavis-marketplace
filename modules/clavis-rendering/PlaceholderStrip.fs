namespace FabioSoft.Clavis.Rendering

open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Shapes
open FabioSoft.Clavis.Placeholders

/// Renders a placeholder template into a horizontal run of inline elements: literal/value text as
/// TextBlocks, and the `bar`/`badge`/`microstat`/`limitPlane`/`color` components as small controls. The pure
/// parse/resolve/format logic lives in clavis-placeholders (unit-tested); this is the WPF projection only.
/// The host pushes fresh values via SetValues and (for limitPlane) limit windows via SetLimitWindows.
[<ExcludeFromCodeCoverage>] // WPF construction; engine logic is unit-tested in clavis-placeholders
type PlaceholderStrip() =
    static let engine = PlaceholderEngine()

    static let microSize = 11.0
    static let iconSize = 11.0
    static let barWidth = 90.0
    static let barHeight = 3.0
    static let iconGap = Thickness(0.0, 0.0, 4.0, 0.0)
    static let barMargin = Thickness(2.0, 0.0, 2.0, 0.0)
    static let modeMargin = Thickness(1.0, 0.0, 1.0, 0.0)

    // The mode maps to a palette accent; default/none/empty render nothing (the user's rule); unknown → dim.
    static let modeBrush value : Brush option =
        match value with
        | "auto" -> Some(Colors.yellow :> Brush)
        | "acceptEdits" | "accept" -> Some(Colors.secondary :> Brush)
        | "plan" -> Some(Colors.green :> Brush)
        | "" | "default" | "none" -> None
        | _ -> Some(Colors.textDim :> Brush)

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
    let mutable values: IReadOnlyDictionary<string, string> =
        Dictionary<string, string>() :> IReadOnlyDictionary<string, string>
    let mutable limitWindows: LimitWindow list = []
    let planeViews = List<LimitsPlaneView>()

    // The previous render's per-segment content keys, so a value update can animate exactly the segments
    // whose content changed (e.g. the effort level after the agent confirms a switch).
    let mutable previousKeys: string[] = [||]

    let monoText (text: string) (brush: Brush) =
        let block =
            TextBlock(Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = microSize, Foreground = brush)
        block.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.MonoFont)
        block :> UIElement

    // Dim, not body text: status-strip literals and values ("ctx", "0/200k") are chrome, and the
    // brighter Colors.text in bold mono read as white against the conversation body.
    let valueText (text: string) =
        monoText text Colors.textDim

    let bar (percent: float) =
        let clamped = max 0.0 (min 100.0 percent)
        let grid =
            Grid(Width = barWidth, Height = barHeight, VerticalAlignment = VerticalAlignment.Center, Margin = barMargin)
        grid.Children.Add(Rectangle(Fill = Colors.track)) |> ignore
        let fill =
            Rectangle(
                Fill = Colors.clavis,
                Width = barWidth * clamped / 100.0,
                HorizontalAlignment = HorizontalAlignment.Left)
        grid.Children.Add fill |> ignore
        grid :> UIElement

    // Plain coloured text, no box or background (Clavis design: content before chrome; the user's rule).
    let badge (value: string) =
        match modeBrush value with
        | None -> None
        | Some brush ->
            let label =
                TextBlock(
                    Text = value.ToUpperInvariant(),
                    FontSize = microSize,
                    Foreground = brush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = modeMargin)
            label.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.AgentFont)
            Some(label :> UIElement)

    let microstat (iconName: string) (value: string) =
        let row = StackPanel(Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center)
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
        ContentControl(Content = view.Element, VerticalAlignment = VerticalAlignment.Center) :> UIElement

    let renderComponent (resolved: ResolvedComponent) =
        match resolved.Component.ToLowerInvariant() with
        | "bar" -> Some(bar (if resolved.Number.HasValue then resolved.Number.Value else 0.0))
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
    // model/effort/mode switch, say - is visibly acknowledged. Animation is per-segment and positional:
    // when the segment count changes (template change, badge appearing/disappearing) nothing animates.
    let render (animateChanges: bool) =
        panel.Children.Clear()
        planeViews.Clear()
        let rendered = List<string * UIElement>()
        for segment in engine.Resolve(template, values) do
            let keyed =
                match segment with
                | :? ResolvedText as t ->
                    if t.Text = "" || (hideUnresolvedValues && t.Unresolved) then
                        None
                    else
                        Some(t.Text, valueText t.Text)
                | :? ResolvedComponent as c ->
                    if hideUnresolvedValues && c.Unresolved then
                        None
                    else
                        renderComponent c |> Option.map (fun e -> $"{c.Component}:{c.Value}", e)
                | _ -> None
            match keyed with
            | Some (key, e) ->
                rendered.Add((key, e))
                panel.Children.Add e |> ignore
            | None -> ()

        if animateChanges && previousKeys.Length = rendered.Count then
            for i in 0 .. rendered.Count - 1 do
                let key, element = rendered[i]
                match element with
                | :? FrameworkElement as fe when previousKeys[i] <> key -> Motion.enter fe
                | _ -> ()

        previousKeys <- rendered |> Seq.map fst |> Array.ofSeq

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

    member _.SetValues(snapshot: IReadOnlyDictionary<string, string>) =
        values <- snapshot
        render true

    member _.SetLimitWindows(windows: LimitWindow seq) =
        limitWindows <- List.ofSeq windows
        for view in planeViews do
            view.Update limitWindows
