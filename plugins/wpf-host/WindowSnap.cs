namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Pure window-snapping geometry, in physical pixels. Given where a dragged window would land (the
/// cursor-driven "proposed" rectangle), it pulls the window's edges onto nearby alignment lines: the
/// edges of the other windows it sits next to, and the work-area edges of every monitor. Width and
/// height are preserved - only the position shifts.
///
/// The snap is recomputed from the cursor-proposed position on every move, which gives the "sticky"
/// detach for free: a snapped window stays pinned to a line while the cursor stays within
/// <c>snapDistance</c> of it, so breaking the snap needs a deliberate drag of more than that distance.
public static class WindowSnap
{
    /// The snapped rectangle for a window the user is dragging. <paramref name="otherWindows"/> are the
    /// rectangles of every other window (the dragged one excluded), <paramref name="workAreas"/> the
    /// usable region of each monitor. A window is only offered as an alignment target on an axis when it
    /// sits near the dragged window on the perpendicular axis, so distant windows never pull it.
    public static ScreenRectangle Compute(
        ScreenRectangle proposed,
        IReadOnlyList<ScreenRectangle> otherWindows,
        IReadOnlyList<ScreenRectangle> workAreas,
        int snapDistance)
    {
        var shiftX = BestShift(proposed.Left, proposed.Right, VerticalLines(proposed, otherWindows, workAreas, snapDistance), snapDistance);
        var shiftY = BestShift(proposed.Top, proposed.Bottom, HorizontalLines(proposed, otherWindows, workAreas, snapDistance), snapDistance);

        return new ScreenRectangle(
            proposed.Left + shiftX, proposed.Top + shiftY,
            proposed.Right + shiftX, proposed.Bottom + shiftY);
    }

    // The candidate vertical lines (x-coordinates) the dragged window's left/right edges may snap to:
    // every monitor's left/right work-area edge, plus the left/right edges of any window sharing its
    // horizontal band (so two side-by-side windows align, but one far above/below does not).
    private static List<int> VerticalLines(
        ScreenRectangle proposed, IReadOnlyList<ScreenRectangle> windows,
        IReadOnlyList<ScreenRectangle> workAreas, int snapDistance)
    {
        var lines = new List<int>();
        foreach (var area in workAreas)
        {
            lines.Add(area.Left);
            lines.Add(area.Right);
        }

        foreach (var window in windows)
        {
            if (NearVertically(proposed, window, snapDistance))
            {
                lines.Add(window.Left);
                lines.Add(window.Right);
            }
        }

        return lines;
    }

    private static List<int> HorizontalLines(
        ScreenRectangle proposed, IReadOnlyList<ScreenRectangle> windows,
        IReadOnlyList<ScreenRectangle> workAreas, int snapDistance)
    {
        var lines = new List<int>();
        foreach (var area in workAreas)
        {
            lines.Add(area.Top);
            lines.Add(area.Bottom);
        }

        foreach (var window in windows)
        {
            if (NearHorizontally(proposed, window, snapDistance))
            {
                lines.Add(window.Top);
                lines.Add(window.Bottom);
            }
        }

        return lines;
    }

    // The windows' vertical spans overlap or are within snapDistance of doing so - they sit in roughly
    // the same horizontal band, so aligning their vertical (left/right) edges is meaningful.
    private static bool NearVertically(ScreenRectangle a, ScreenRectangle b, int snapDistance) =>
        a.Top <= b.Bottom + snapDistance && b.Top <= a.Bottom + snapDistance;

    private static bool NearHorizontally(ScreenRectangle a, ScreenRectangle b, int snapDistance) =>
        a.Left <= b.Right + snapDistance && b.Left <= a.Right + snapDistance;

    // The signed shift that brings whichever of the two moving edges (lo/hi) is closest to a candidate
    // line onto that line, or 0 when none is within snapDistance. The smallest-magnitude shift wins, so
    // the nearest alignment always takes precedence and the result is deterministic.
    private static int BestShift(int lo, int hi, IReadOnlyList<int> lines, int snapDistance)
    {
        int? best = null;
        foreach (var line in lines)
        {
            best = Closer(best, line - lo, snapDistance);
            best = Closer(best, line - hi, snapDistance);
        }

        return best ?? 0;
    }

    private static int? Closer(int? best, int delta, int snapDistance)
    {
        if (Math.Abs(delta) > snapDistance)
        {
            return best;
        }

        return best is null || Math.Abs(delta) < Math.Abs(best.Value) ? delta : best;
    }
}
