namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Media.Animation

/// Lays out three placeholder zones - left, center, right - across a status bar and degrades them in stages
/// so they never overlap, always keeping a small gap between zones:
///   1. shrink the bars toward a floor, then shorten path values, drop stat-row icons, and drop chrome
///      literals (the per-strip "density" measures - each only kicks in when the lighter ones do not free
///      enough room);
///   2. when no content can shrink further, scale the whole strip's text down to a floor (animated);
///   3. only as a last resort, drop the center zone, then the left - the right zone is always kept.
/// Generic over whatever the zones are configured to render, so it works for any template.
[<ExcludeFromCodeCoverage>] // WPF measure/arrange + animation; no decision logic worth isolating from WPF
type ResponsiveZoneBar(left: PlaceholderStrip, center: PlaceholderStrip, right: PlaceholderStrip) as this =
    inherit Panel()

    // The "little margin between elements" the layout must preserve; below it the zones are considered to
    // overlap and the next degradation stage engages.
    static let minGap = 14.0
    // Text scales down by at most 20% (the spec's "up to 20% of its original size").
    static let minScale = 0.8
    // The bar shrinks to roughly a third of its full width before the other measures take over.
    static let minBarScale = 0.34
    static let scaleDuration = Duration(TimeSpan.FromMilliseconds 160.0)
    // Content-degradation levels: 0 full, 1 shrink bars, 2 +shorten paths, 3 +drop stat icons, 4 +drop literals.
    static let maxLevel = 4

    let zones = [| left; center; right |]
    let elements = zones |> Array.map (fun zone -> zone.Element :> UIElement)
    let scales = zones |> Array.map (fun _ -> ScaleTransform(1.0, 1.0))

    let mutable lastAvail = nan
    let mutable contentDirty = true
    let mutable appliedScale = 1.0
    let mutable centerVisible = true
    let mutable leftVisible = true
    let mutable naturalHeight = 0.0

    let setDensityForLevel level =
        let barScale = if level >= 1 then minBarScale else 1.0
        let shortenPaths = level >= 2
        let showIcons = level < 3
        let showLiterals = level < 4
        for zone in zones do
            zone.SetDensity(barScale, showLiterals, shortenPaths, showIcons)

    // Natural (scale-independent) width of a zone: measure it unconstrained, then divide out its current
    // layout scale, so the fit decision is stable while the scale animates.
    let naturalWidth index =
        let element = elements[index]
        element.Measure(Size(infinity, infinity))
        let scaleX = scales[index].ScaleX
        if scaleX <= 0.0 then element.DesiredSize.Width else element.DesiredSize.Width / scaleX

    let animateScale (transform: ScaleTransform) target =
        let animation = DoubleAnimation(target, scaleDuration, EasingFunction = Motion.easeOut())
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation)
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation)

    // Re-start the scale animation only when the target actually changes, so a steady window (or a frame of
    // an in-flight animation) does not restart it.
    let setScaleTarget target =
        if abs (target - appliedScale) > 0.001 then
            appliedScale <- target
            for transform in scales do
                animateScale transform target

    let setVisible index visible =
        elements[index].Visibility <- (if visible then Visibility.Visible else Visibility.Collapsed)

    // Pick the lightest degradation that fits avail, committing density level, scale and zone drops.
    let decide avail =
        setVisible 0 true
        setVisible 1 true
        leftVisible <- true
        centerVisible <- true

        // Stage 1: the lightest content level (0..maxLevel) whose three zones plus two gaps fit at full scale.
        let rec findLevel level =
            setDensityForLevel level
            let total = naturalWidth 0 + naturalWidth 1 + naturalWidth 2 + 2.0 * minGap
            if total <= avail || level >= maxLevel then level, total
            else findLevel (level + 1)

        let _, totalAtFloorLevel = findLevel 0

        if totalAtFloorLevel <= avail then
            setScaleTarget 1.0
        else
            // Stage 2: content is as small as it gets; scale the text down toward the floor to close the gap.
            let contentWidth = totalAtFloorLevel - 2.0 * minGap
            let needed = if contentWidth <= 0.0 then 1.0 else (avail - 2.0 * minGap) / contentWidth
            let scale = max minScale (min 1.0 needed)
            if contentWidth * scale + 2.0 * minGap <= avail then
                setScaleTarget scale
            else
                // Stage 3: still overflowing at the floor scale - drop the center zone, then the left.
                setScaleTarget minScale
                let leftWidth = naturalWidth 0 * minScale
                let rightWidth = naturalWidth 2 * minScale
                centerVisible <- false
                setVisible 1 false
                if leftWidth + rightWidth + minGap > avail then
                    leftVisible <- false
                    setVisible 0 false

        naturalHeight <-
            elements
            |> Array.mapi (fun i element ->
                if element.Visibility = Visibility.Visible then
                    element.DesiredSize.Height / max 0.001 scales[i].ScaleY
                else
                    0.0)
            |> Array.fold max 0.0

    do
        for i in 0 .. zones.Length - 1 do
            (zones[i].Element).LayoutTransform <- scales[i]
            this.Children.Add elements[i] |> ignore
            zones[i].ContentChanged.Add(fun () ->
                contentDirty <- true
                this.InvalidateMeasure())

    override _.MeasureOverride(availableSize: Size) : Size =
        let avail = availableSize.Width
        if Double.IsInfinity avail || Double.IsNaN avail then
            // Unconstrained (e.g. measured inside an auto-sized container): full density, natural size.
            setDensityForLevel 0
            setScaleTarget 1.0
            setVisible 0 true
            setVisible 1 true
            for element in elements do
                element.Measure(Size(infinity, infinity))
            let width = elements |> Array.sumBy (fun element -> element.DesiredSize.Width)
            let height = elements |> Array.fold (fun acc element -> max acc element.DesiredSize.Height) 0.0
            Size(width + 2.0 * minGap, height)
        else
            if contentDirty || abs (avail - lastAvail) > 0.5 then
                contentDirty <- false
                lastAvail <- avail
                decide avail
            for element in elements do
                if element.Visibility = Visibility.Visible then element.Measure(Size(infinity, infinity))
                else element.Measure(Size(0.0, 0.0))
            Size(avail, naturalHeight)

    override _.ArrangeOverride(finalSize: Size) : Size =
        let height = finalSize.Height
        let widthOf index =
            if elements[index].Visibility = Visibility.Visible then elements[index].DesiredSize.Width else 0.0

        let leftWidth = widthOf 0
        let rightWidth = widthOf 2
        let centerWidth = widthOf 1

        if leftVisible then
            elements[0].Arrange(Rect(0.0, 0.0, leftWidth, height))
        else
            elements[0].Arrange(Rect(0.0, 0.0, 0.0, 0.0))

        elements[2].Arrange(Rect(max 0.0 (finalSize.Width - rightWidth), 0.0, rightWidth, height))

        if centerVisible then
            // Centered, but never overlapping the left/right zones when room is tight.
            let centered = (finalSize.Width - centerWidth) / 2.0
            let lowerBound = leftWidth + minGap
            let upperBound = finalSize.Width - rightWidth - minGap - centerWidth
            let clamped = max lowerBound (min centered upperBound)
            elements[1].Arrange(Rect(max 0.0 clamped, 0.0, centerWidth, height))
        else
            elements[1].Arrange(Rect(0.0, 0.0, 0.0, 0.0))

        finalSize
