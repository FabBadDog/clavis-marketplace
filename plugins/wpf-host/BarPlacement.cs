namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Where the workspace bar sits, in device-independent pixels.
public readonly record struct BarRect(double Left, double Top, double Width, double Height);

/// The arithmetic that puts the bar across the top of a monitor. Pure: the monitor rectangle and the DPI factor
/// are passed in, so multi-monitor and high-DPI placement are testable without a real display.
public static class BarPlacement
{
    /// The bar spans the full width of its monitor's work area, flush with its top edge.
    ///
    /// The monitor rectangle arrives in **physical pixels** (that is what the Win32 monitor APIs report) while
    /// WPF positions windows in **DIPs**, so it is divided by the DPI factor. Getting this wrong is invisible at
    /// 100% scaling and puts the bar off-screen at 150%, which is exactly the kind of bug that only shows up on
    /// someone else's machine.
    ///
    /// A non-positive DPI factor (an unplugged monitor, a display that has not reported yet) falls back to 1.0
    /// rather than dividing by zero.
    public static BarRect Compute(ScreenRectangle workArea, double barHeight, double dpiFactor)
    {
        var scale = dpiFactor > 0 ? dpiFactor : 1.0;
        var width = (workArea.Right - workArea.Left) / scale;
        var height = barHeight <= 0 ? 0 : barHeight;

        return new BarRect(workArea.Left / scale, workArea.Top / scale, width < 0 ? 0 : width, height);
    }

    /// The work area a maximized window should use once the bar has taken the top strip. Returned in the same
    /// physical-pixel space as the input, because that is what the maximize path works in.
    ///
    /// Without this a maximized Clavis window would sit *under* the always-on-top bar: the bar stays visible
    /// (it is Topmost) but it covers the window's own title bar, which is the case a user actually hits.
    public static ScreenRectangle Reserve(ScreenRectangle workArea, double barHeight, double dpiFactor)
    {
        var scale = dpiFactor > 0 ? dpiFactor : 1.0;
        var reserved = (int)System.Math.Round(barHeight * scale);
        var top = workArea.Top + reserved;

        // Never reserve so much that the work area inverts - a bar taller than the screen is a config error, not
        // a reason to hand out a negative rectangle.
        return top >= workArea.Bottom
            ? workArea
            : new ScreenRectangle(workArea.Left, top, workArea.Right, workArea.Bottom);
    }
}
