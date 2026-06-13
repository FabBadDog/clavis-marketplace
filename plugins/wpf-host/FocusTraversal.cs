using System.Diagnostics.CodeAnalysis;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// Cross-window Tab / Shift+Tab coordination. A window intercepts Tab and asks the coordinator to move
// focus: it first tries to move within the originating window, and on reaching that window's boundary it
// crosses to the next window that has focusable controls, wrapping. Windows (and the panels inside them)
// with nothing interactive are skipped, so focus is never stranded where no keyboard action is possible.
[ExcludeFromCodeCoverage] // orchestrates WPF keyboard focus across windows; the selection math is FocusRing
internal sealed class FocusTraversal(Func<IReadOnlyList<WindowHost>> orderedWindows)
{
    public void Traverse(WindowHost current, bool forward)
    {
        if (current.TryMoveFocusWithin(forward))
        {
            return; // moved to a neighbouring stop inside the same window
        }

        var windows = orderedWindows();
        var index = IndexOf(windows, current);
        if (index < 0)
        {
            return;
        }

        var hasStops = new bool[windows.Count];
        for (var i = 0; i < windows.Count; i++)
        {
            hasStops[i] = windows[i].HasFocusableStops;
        }

        var targetIndex = FocusRing.NextWindowWithStops(hasStops, index, forward);
        if (targetIndex == index)
        {
            current.WrapFocus(forward); // only this window can hold focus - wrap in place
        }
        else
        {
            windows[targetIndex].EnterFromAdjacentWindow(forward);
        }
    }

    private static int IndexOf(IReadOnlyList<WindowHost> windows, WindowHost host)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (ReferenceEquals(windows[i], host))
            {
                return i;
            }
        }

        return -1;
    }
}
