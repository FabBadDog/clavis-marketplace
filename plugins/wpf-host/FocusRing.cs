namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Pure traversal arithmetic for Tab / Shift+Tab focus movement. Kept free of WPF types so the
/// cross-window focus rules are unit-testable; the impure shell (FocusTraversal) supplies the
/// ordered tab-stop counts and performs the actual MoveFocus / Activate / Keyboard.Focus.
public static class FocusRing
{
    /// The next tab-stop index within a window, or null when the move runs off either end - the
    /// signal for the caller to cross into another window rather than wrap inside this one.
    public static int? Advance(int stopCount, int currentIndex, bool forward)
    {
        if (stopCount <= 0)
        {
            return null;
        }

        var next = forward ? currentIndex + 1 : currentIndex - 1;
        return next >= 0 && next < stopCount ? next : null;
    }

    /// The index of the next window that holds at least one tab stop, scanning cyclically from the
    /// current window in the tab direction. Returns the current index when no other window can take
    /// focus, so traversal wraps in place rather than stranding focus on an empty window.
    public static int NextWindowWithStops(bool[] windowsHaveStops, int currentWindowIndex, bool forward)
    {
        var count = windowsHaveStops.Length;
        if (count == 0)
        {
            return currentWindowIndex;
        }

        var step = forward ? 1 : -1;
        for (var offset = 1; offset <= count; offset++)
        {
            var index = (((currentWindowIndex + step * offset) % count) + count) % count;
            if (windowsHaveStops[index])
            {
                return index;
            }
        }

        return currentWindowIndex;
    }
}
