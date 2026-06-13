using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Magnetic window snapping while a window is dragged. Hooks the window's move loop (WM_MOVING) and runs
/// the proposed rectangle through the pure WindowSnap geometry, so the window pulls into alignment with
/// the other windows' edges and each monitor's work area, and resists detaching until dragged more than
/// the snap distance. WM_MOVING fires only during an interactive caption drag, so the host's programmatic
/// window animations (fall-in, rise-out, summon) are never snapped. Physical pixels throughout - the
/// move-loop rectangle and GetWindowRect agree - so it is DPI-correct across monitors.
[ExcludeFromCodeCoverage(Justification = "Win32 move-loop interop; the snapping math is covered in WindowSnap.")]
internal sealed class WindowSnapBehavior
{
    // The pull-in threshold, and the distance a drag must exceed to break an active snap. Physical pixels.
    private const int SnapDistance = 12;
    private const int WmMoving = 0x0216;

    private readonly Func<IReadOnlyList<ScreenRectangle>> _otherWindows;

    private WindowSnapBehavior(Func<IReadOnlyList<ScreenRectangle>> otherWindows) => _otherWindows = otherWindows;

    /// Attach snapping to a window. <paramref name="otherWindows"/> yields the current physical
    /// rectangles of every other window, evaluated fresh on each move so it always reflects the live
    /// layout.
    public static void Attach(Window window, Func<IReadOnlyList<ScreenRectangle>> otherWindows)
    {
        var behavior = new WindowSnapBehavior(otherWindows);

        // The native handle exists only once the window is sourced (shown); hooking a zero handle throws.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            behavior.Hook(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => behavior.Hook(window);
        }
    }

    private void Hook(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmMoving)
        {
            return IntPtr.Zero;
        }

        var proposed = Marshal.PtrToStructure<NativeRect>(lParam);
        var snapped = WindowSnap.Compute(
            new ScreenRectangle(proposed.Left, proposed.Top, proposed.Right, proposed.Bottom),
            _otherWindows(),
            WorkAreas(),
            SnapDistance);

        // A move never resizes, so width/height carry over; only the edges shift to the snapped position.
        proposed.Left = snapped.Left;
        proposed.Top = snapped.Top;
        proposed.Right = snapped.Right;
        proposed.Bottom = snapped.Bottom;
        Marshal.StructureToPtr(proposed, lParam, fDeleteOld: false);

        handled = true;
        return new IntPtr(1);
    }

    /// The current physical-pixel rectangle of a window, or null when it has no handle yet, is hidden, or
    /// is minimized - none of which should act as a snap target.
    public static ScreenRectangle? RectOf(Window window)
    {
        if (!window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            return null;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return null;
        }

        return new ScreenRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    // Every monitor's work area (the desktop minus the taskbar and any docked bars), in physical pixels,
    // so a window can snap to the usable edge of any display.
    private static List<ScreenRectangle> WorkAreas()
    {
        var areas = new List<ScreenRectangle>();

        bool Collect(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                areas.Add(new ScreenRectangle(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
            }

            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Collect, IntPtr.Zero);
        return areas;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
