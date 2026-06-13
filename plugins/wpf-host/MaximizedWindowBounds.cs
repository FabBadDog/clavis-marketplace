using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// A screen rectangle in physical pixels (left/top/right/bottom), matching the Win32 RECT layout.
public readonly record struct ScreenRectangle(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// Where a maximized window should sit, in coordinates relative to its monitor's top-left corner -
/// the space WM_GETMINMAXINFO expects.
public readonly record struct MaximizedPlacement(int X, int Y, int Width, int Height);

public static class MaximizedWindowBounds
{
    // A borderless window (WindowStyle=None + AllowsTransparency) loses the OS's automatic clamp to the
    // monitor work area, so a maximized window expands over the full monitor and slides under the taskbar.
    // WM_GETMINMAXINFO coordinates are relative to the monitor origin, so the work-area origin is offset
    // by the monitor origin to keep the result correct on secondary monitors with a non-zero origin.
    public static MaximizedPlacement Compute(ScreenRectangle monitor, ScreenRectangle work) =>
        new(work.Left - monitor.Left, work.Top - monitor.Top, work.Width, work.Height);
}

[ExcludeFromCodeCoverage(Justification = "Win32 interop and window-procedure plumbing; the pure bounds math is covered in MaximizedWindowBounds.")]
internal static class WorkAreaMaximize
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static void Constrain(Window window)
    {
        // IsInitialized turns true once the XAML has loaded, but the native handle (Hwnd) only exists
        // after the window is shown (SourceInitialized). Gate on the handle, not IsInitialized: hooking
        // an as-yet-unshown window would call HwndSource.FromHwnd with a zero Hwnd, which it rejects with
        // "Hwnd of zero is not valid", aborting activation.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Hook(window);
        }
    }

    private static void Hook(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var placement = MaximizedWindowBounds.Compute(
            new ScreenRectangle(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            new ScreenRectangle(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));

        // Read the OS-supplied struct first so the track-size defaults are preserved, then override only
        // the maximized position and size with the work-area-constrained values.
        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMax.MaxPosition = new NativePoint { X = placement.X, Y = placement.Y };
        minMax.MaxSize = new NativePoint { X = placement.Width, Y = placement.Height };
        Marshal.StructureToPtr(minMax, lParam, fDeleteOld: true);

        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
