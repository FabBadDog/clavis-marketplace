namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Shapes

/// A live limits-plane view: the WPF element plus an Update the caller pushes fresh windows into. Exposed
/// as a class (not a tuple + closure) so the C# plugin consumes it without F# function/list interop.
[<Sealed>]
type LimitsPlaneView(element: FrameworkElement, update: LimitWindow seq -> unit) =
    member _.Element = element
    member _.Update(windows: LimitWindow seq) = update windows

/// The limits plane: usage on X (nothing used at the left, fully used at the right), time on Y (window
/// start at the bottom, reset at the top), and a dashed even-burn diagonal. A dot below the diagonal has
/// spent more than the elapsed window (overspend); above it is a surplus. Beneath the plane a runway strip
/// shows each window's remaining budget in real units with a wall at the first ceiling, because limits cap
/// each other and the normalized plane cannot show that 100k < 200k. Two presentations share the plot: a
/// tiny glyph for the status bar and a labelled panel with the runway and a per-window readout.
[<ExcludeFromCodeCoverage>] // WPF drawing; the plotted values come from the unit-tested LimitWindow module
[<RequireQualifiedAccess>]
module LimitsPlane =

    [<Literal>]
    let GlyphSize = 22.0

    [<Literal>]
    let private PlaneSize = 188.0

    [<Literal>]
    let private RunwayWidth = 224.0

    [<Literal>]
    let private BarHeight = 16.0

    let private freeze (brush: SolidColorBrush) =
        brush.Freeze()
        brush

    let private rgb red green blue =
        SolidColorBrush(Color.FromRgb(red, green, blue)) |> freeze

    let private faintLine = rgb 0x2Auy 0x2Auy 0x30uy
    let private mutedLine = rgb 0x55uy 0x55uy 0x5Euy

    // Surplus (above the diagonal) reads green, overspend (below) reads red. These carry the over/under
    // signal everywhere it appears: the plane's corner labels, the runway wall, and the readout percentage.
    let private surplusBrush = rgb 0x6Fuy 0xD4uy 0x9Buy
    let private overspendBrush = rgb 0xD4uy 0x70uy 0x70uy

    // Each usage window gets a fixed identity colour so the windows stay distinguishable across the plane,
    // the runway, and the readout. The over/under signal lives in the labels and the readout sign, never in
    // the dot colour. Ordered so the common two-window case (5-hour + weekly) draws a blue/gold contrast.
    let private identityBrushes =
        [| rgb 0x9Fuy 0xD5uy 0xF0uy   // blue
           rgb 0xD4uy 0xB3uy 0x6Auy   // gold
           rgb 0x5Euy 0xD4uy 0xC4uy   // teal
           rgb 0xC9uy 0xA0uy 0xDCuy |] // lavender

    let private identityColor index =
        identityBrushes[index % identityBrushes.Length]

    /// The square plot. Holds normalized dots (x, y, colour) and redraws on demand; y is already inverted
    /// (0 at the top) so spend grows upward toward the overspend corner. `maxSize` is the plot's natural
    /// size; it shrinks to fit a narrower host so the panel stays readable at any width.
    type private PlaneVisual(maxSize: float, dotRadius: float, withCorners: bool) =
        inherit FrameworkElement()

        let mutable dots: (float * float * SolidColorBrush) list = []

        let cap value =
            if Double.IsInfinity value || Double.IsNaN value then
                maxSize
            else
                min maxSize value

        member this.SetDots(value) =
            dots <- value
            this.InvalidateVisual()

        override _.MeasureOverride(availableSize) =
            let side = min (cap availableSize.Width) (cap availableSize.Height)
            Size(side, side)

        override this.OnRender(dc: DrawingContext) =
            let side = min this.ActualWidth this.ActualHeight
            let pad = if withCorners then 7.0 else 2.0
            let inner = max 0.0 (side - 2.0 * pad)

            dc.DrawRectangle(null, Pen(faintLine, 1.0), Rect(pad, pad, inner, inner))

            let diagonal = Pen(mutedLine, 1.0)
            diagonal.DashStyle <- DashStyle([| 2.0; 2.0 |], 0.0)
            dc.DrawLine(diagonal, Point(pad, pad + inner), Point(pad + inner, pad))

            for (normalX, normalY, brush) in dots do
                dc.DrawEllipse(brush, null, Point(pad + normalX * inner, pad + normalY * inner), dotRadius, dotRadius)

    let private toDot now index (window: LimitWindow) =
        let usage = LimitWindow.resolve window now
        // X = spend (left empty, right full); Y inverted so the window climbs from the bottom to the top.
        usage.SpentFraction, 1.0 - usage.TimeFraction, identityColor index

    let private monoText text size brushKey =
        let block = TextBlock(Text = text, FontSize = size)
        block.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.MonoFont)
        block.SetResourceReference(TextBlock.ForegroundProperty, (brushKey: string))
        block

    let private agentLabel text size (brush: Brush) =
        let block = TextBlock(Text = text, FontSize = size, Foreground = brush)
        block.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.AgentFont)
        block

    /// The plot wrapped in its axis labels (usage along the bottom, time up the left) with the surplus and
    /// overspend corners called out in green/red. Built once; the dots inside redraw on update.
    let private planeHost (visual: FrameworkElement) =
        let overlay = Grid()
        overlay.Children.Add(visual) |> ignore

        let surplus = agentLabel "SURPLUS" 8.0 surplusBrush
        surplus.HorizontalAlignment <- HorizontalAlignment.Left
        surplus.VerticalAlignment <- VerticalAlignment.Top
        surplus.Margin <- Thickness(11.0, 9.0, 0.0, 0.0)
        overlay.Children.Add(surplus) |> ignore

        let overspend = agentLabel "OVERSPEND" 8.0 overspendBrush
        overspend.HorizontalAlignment <- HorizontalAlignment.Right
        overspend.VerticalAlignment <- VerticalAlignment.Bottom
        overspend.Margin <- Thickness(0.0, 0.0, 11.0, 9.0)
        overlay.Children.Add(overspend) |> ignore

        let host = Grid(HorizontalAlignment = HorizontalAlignment.Center, Margin = Thickness(0.0, 4.0, 0.0, 0.0))
        host.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))
        host.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))
        host.RowDefinitions.Add(RowDefinition(Height = GridLength.Auto))
        host.RowDefinitions.Add(RowDefinition(Height = GridLength.Auto))

        let timeLabel = agentLabel "time →" 9.0 mutedLine
        timeLabel.LayoutTransform <- RotateTransform(-90.0)
        timeLabel.HorizontalAlignment <- HorizontalAlignment.Center
        timeLabel.VerticalAlignment <- VerticalAlignment.Center
        Grid.SetColumn(timeLabel, 0)
        Grid.SetRow(timeLabel, 0)
        host.Children.Add(timeLabel) |> ignore

        Grid.SetColumn(overlay, 1)
        Grid.SetRow(overlay, 0)
        host.Children.Add(overlay) |> ignore

        let usageLabel = agentLabel "usage →" 9.0 mutedLine
        usageLabel.HorizontalAlignment <- HorizontalAlignment.Center
        usageLabel.Margin <- Thickness(0.0, 3.0, 0.0, 0.0)
        Grid.SetColumn(usageLabel, 1)
        Grid.SetRow(usageLabel, 1)
        host.Children.Add(usageLabel) |> ignore

        host

    /// One runway bar: the window name and remaining/reset on a header line, then a budget bar in the
    /// window's identity colour with the ceiling wall drawn across it.
    let private runwayRow maxRemaining wallX index (window: LimitWindow) (now: DateTimeOffset) =
        let brush = identityColor index
        let remaining = LimitWindow.remaining window
        let barWidth = if maxRemaining <= 0.0 then 0.0 else remaining / maxRemaining * RunwayWidth

        let header = Grid()
        header.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1.0, GridUnitType.Star)))
        header.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))

        let name = agentLabel (window.Name.ToUpperInvariant()) 9.0 brush
        Grid.SetColumn(name, 0)
        header.Children.Add(name) |> ignore

        let detail =
            monoText
                $"{LimitWindow.formatAmount remaining} left · {LimitWindow.formatDuration (window.ResetsAt - now)}"
                8.5 "TextDimBrush"
        Grid.SetColumn(detail, 1)
        header.Children.Add(detail) |> ignore

        let barCell =
            Grid(
                Width = RunwayWidth,
                Height = BarHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = Thickness(0.0, 3.0, 0.0, 0.0))
        let bar = Rectangle(Width = barWidth, Height = BarHeight, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left)
        barCell.Children.Add(bar) |> ignore
        let wall = Line(X1 = wallX, X2 = wallX, Y1 = 0.0, Y2 = BarHeight, Stroke = overspendBrush, StrokeThickness = 1.2)
        wall.StrokeDashArray <- DoubleCollection([| 3.0; 2.0 |])
        barCell.Children.Add(wall) |> ignore

        let row = StackPanel(Margin = Thickness(0.0, 0.0, 0.0, 12.0))
        row.Children.Add(header) |> ignore
        row.Children.Add(barCell) |> ignore
        row :> UIElement

    /// The runway strip: one budget bar per window with a dashed wall at the binding ceiling and a caption
    /// naming the window that caps the rest.
    let private runwayStrip (now: DateTimeOffset) windows =
        let container = StackPanel(HorizontalAlignment = HorizontalAlignment.Center)

        match windows with
        | [] -> ()
        | _ ->
            let maxRemaining = windows |> List.map LimitWindow.remaining |> List.max
            let ceiling = LimitWindow.ceiling windows
            let wallX =
                match ceiling with
                | Some limit when maxRemaining > 0.0 -> limit.Effective / maxRemaining * RunwayWidth
                | _ -> 0.0

            windows
            |> List.iteri (fun index window -> container.Children.Add(runwayRow maxRemaining wallX index window now) |> ignore)

            match ceiling with
            | Some limit ->
                let caption =
                    monoText $"effective {LimitWindow.formatAmount limit.Effective} · capped by {limit.BindingName}" 8.5 "TextBrush"
                caption.Foreground <- overspendBrush
                caption.HorizontalAlignment <- HorizontalAlignment.Center
                container.Children.Add(caption) |> ignore
            | None -> ()

        container

    let private swatch (brush: Brush) =
        Ellipse(Width = 9.0, Height = 9.0, Fill = brush, VerticalAlignment = VerticalAlignment.Center)

    /// One readout row: identity swatch, window name with spend/reset meta, and the signed pace delta in
    /// percentage points - red when overspending, green when in surplus.
    let private readoutRow now index (window: LimitWindow) =
        let usage = LimitWindow.resolve window now
        let brush = identityColor index
        let percentDelta = int (Math.Round(usage.Delta * 100.0))
        let deltaBrush =
            if percentDelta > 0 then overspendBrush
            elif percentDelta < 0 then surplusBrush
            else mutedLine

        let row = Grid(Margin = Thickness(0.0, 7.0, 0.0, 0.0))
        row.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))
        row.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1.0, GridUnitType.Star)))
        row.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))

        let mark = swatch brush
        mark.Margin <- Thickness(0.0, 0.0, 9.0, 0.0)
        Grid.SetColumn(mark, 0)
        row.Children.Add(mark) |> ignore

        let name = TextBlock(Text = window.Name.ToUpperInvariant(), FontSize = 10.0)
        name.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.AgentFont)
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush")
        let percentSpent = int (Math.Round(usage.SpentFraction * 100.0))
        let meta =
            monoText $"{percentSpent}%% spent · resets {LimitWindow.formatDuration (window.ResetsAt - now)}" 9.0 "TextDimBrush"
        meta.Margin <- Thickness(0.0, 1.0, 0.0, 0.0)
        let left = StackPanel(VerticalAlignment = VerticalAlignment.Center)
        left.Children.Add(name) |> ignore
        left.Children.Add(meta) |> ignore
        Grid.SetColumn(left, 1)
        row.Children.Add(left) |> ignore

        let sign = if percentDelta >= 0 then "+" else "−"
        let delta = TextBlock(Text = $"{sign}{abs percentDelta}%%", FontSize = 12.0, Foreground = deltaBrush)
        delta.SetResourceReference(TextBlock.FontFamilyProperty, FontKeys.MonoFont)
        delta.VerticalAlignment <- VerticalAlignment.Center
        Grid.SetColumn(delta, 2)
        row.Children.Add(delta) |> ignore

        row :> UIElement

    /// The status-bar glyph: just the plot, dot per window, no labels. The view's Update re-evaluates the
    /// windows against the current time on each call.
    let CreateGlyph () =
        let visual = PlaneVisual(GlyphSize, 1.8, false)
        let update (windows: LimitWindow seq) =
            visual.SetDots(windows |> Seq.mapi (toDot DateTimeOffset.UtcNow) |> List.ofSeq)
        LimitsPlaneView(visual, update)

    /// The detail panel: the labelled plane, the runway strip, then a per-window readout.
    let CreatePanel () =
        let visual = PlaneVisual(PlaneSize, 4.0, true)
        let runwayContainer = StackPanel(Margin = Thickness(0.0, 18.0, 0.0, 0.0))
        let readout = StackPanel(Margin = Thickness(0.0, 12.0, 0.0, 0.0))

        let root = StackPanel(Margin = Thickness(16.0))
        root.Children.Add(planeHost visual) |> ignore
        root.Children.Add(runwayContainer) |> ignore
        root.Children.Add(readout) |> ignore

        // The plot already shrinks to fit a narrow panel; scrolling vertically keeps the runway and the
        // per-window readout reachable when the panel (e.g. a short slide-in) is not tall enough.
        let scroller =
            ScrollViewer(
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled)

        let update (windows: LimitWindow seq) =
            let now = DateTimeOffset.UtcNow
            let windowList = List.ofSeq windows
            visual.SetDots(windowList |> List.mapi (toDot now))
            runwayContainer.Children.Clear()
            runwayContainer.Children.Add(runwayStrip now windowList) |> ignore
            readout.Children.Clear()
            windowList |> List.iteri (fun index window -> readout.Children.Add(readoutRow now index window) |> ignore)

        LimitsPlaneView(scroller, update)
