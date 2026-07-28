using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Makes a window unable to take keyboard focus, ever.
///
/// `ShowActivated = false` is **not enough**: it only covers the first show. A *click* on the window still
/// activates it, which for the workspace bar means clicking a workspace yanks the caret out of the prompt you
/// were typing in. Two things are needed - `WS_EX_NOACTIVATE` in the extended style so the window is not
/// activated by the shell, and answering `WM_MOUSEACTIVATE` with `MA_NOACTIVATE` so a click acts without
/// focusing. Without both the bar is unusable, so this is not optional polish.
///
/// The only interop in this codebase that is new for the bar; kept in one file so it is easy to find and to
/// distrust.
[ExcludeFromCodeCoverage] // Win32 interop against a live HWND; cannot be exercised without a real window
internal static class NoActivateWindow
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    /// Apply once the window has an HWND. Safe to call before it is shown; harmless if the handle is missing.
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is not HwndSource source)
            {
                return;
            }

            var style = (long)GetWindowLongPtr(source.Handle, GwlExStyle);
            SetWindowLongPtr(source.Handle, GwlExStyle, (IntPtr)(style | WsExNoActivate));
            source.AddHook(Hook);
        };
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseActivate)
        {
            return IntPtr.Zero;
        }

        // Act on the click, but do not take focus with it.
        handled = true;
        return new IntPtr(MaNoActivate);
    }
}
