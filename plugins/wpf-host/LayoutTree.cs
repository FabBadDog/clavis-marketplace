using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Pure walks and folds over a captured docking tree, plus the geometry predicate behind an on-screen
/// check. No WPF, no window state, no bus - everything a caller needs arrives as an argument, so the
/// layout arithmetic the host depends on is testable without a dispatcher or a real monitor.
public static class LayoutTree
{
    /// Every panel slot in the tree, depth-first.
    public static IEnumerable<PanelSlot> EnumerateSlots(LayoutNode node)
    {
        if (node.Kind == DockingModel.Leaf)
        {
            foreach (var slot in node.Panels ?? [])
            {
                yield return slot;
            }
        }
        else
        {
            foreach (var child in node.Children ?? [])
            {
                foreach (var slot in EnumerateSlots(child))
                {
                    yield return slot;
                }
            }
        }
    }

    /// Like EnumerateSlots but tags each panel with whether it is the selected tab of its leaf group, so a
    /// snapshot can tell an on-screen panel from one sitting behind other tabs.
    public static IEnumerable<(PanelSlot Slot, bool IsActiveTab)> EnumerateSlotsWithVisibility(LayoutNode node)
    {
        if (node.Kind == DockingModel.Leaf)
        {
            var panels = node.Panels ?? [];
            for (var index = 0; index < panels.Length; index++)
            {
                yield return (panels[index], index == node.ActiveIndex);
            }
        }
        else
        {
            foreach (var child in node.Children ?? [])
            {
                foreach (var item in EnumerateSlotsWithVisibility(child))
                {
                    yield return item;
                }
            }
        }
    }

    /// Rebuild the tree with each panel slot carrying its latest saved state. The state lookup is passed in
    /// rather than read from a field, so folding is a pure function of (tree, state).
    public static LayoutNode FoldState(LayoutNode node, IReadOnlyDictionary<Guid, string> panelState)
    {
        var children = (node.Children ?? []).Select(child => FoldState(child, panelState)).ToArray();
        var panels = (node.Panels ?? [])
            .Select(slot => new PanelSlot
            {
                PanelId = slot.PanelId,
                PanelKind = slot.PanelKind,
                Title = slot.Title,
                SavedState = panelState.GetValueOrDefault(slot.PanelId, "")
            })
            .ToArray();

        return new LayoutNode
        {
            Kind = node.Kind,
            GroupId = node.GroupId,
            Orientation = node.Orientation ?? "",
            Sizes = node.Sizes ?? [],
            Children = children,
            Panels = panels,
            ActiveIndex = node.ActiveIndex
        };
    }

    /// A window is "on screen" when its centre falls within the given desktop rectangle, so a layout saved
    /// on a monitor that is now unplugged can fall back to centre-screen instead of opening off-screen. The
    /// desktop bounds are passed in so this stays independent of the machine's actual monitors.
    public static bool IsCenterWithin(
        PersistedWindowState state, double desktopLeft, double desktopTop, double desktopWidth, double desktopHeight)
    {
        var centerX = state.Left + state.Width / 2.0;
        var centerY = state.Top + state.Height / 2.0;

        return centerX >= desktopLeft
            && centerX <= desktopLeft + desktopWidth
            && centerY >= desktopTop
            && centerY <= desktopTop + desktopHeight;
    }
}
