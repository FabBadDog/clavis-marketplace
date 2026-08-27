namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// A window a fresh panel could be placed in: its identity, the workspace it belongs to (Guid.Empty for a
/// window that belongs to none), and whether it is that workspace's chrome window rather than a tear-off.
public readonly record struct PlaceableWindow(Guid WindowId, Guid WorkspaceId, bool IsPrimary);

/// Choosing which window a freshly opened panel is placed in.
///
/// The host remembers one window per panel *kind* - where the user last put a git log, a keymap, an events
/// view - so re-opening a kind returns it to where it was. That memory is right for a kind whose subject is
/// the application, and wrong for one whose subject is a workspace: a chat belongs to its workspace, and
/// "where a chat was last placed" names some other workspace's window as often as not. Honouring the memory
/// unconditionally is what put every workspace's chat into a single window as tabs.
///
/// So the memory is honoured only while it stays inside the panel's own workspace; otherwise the panel goes
/// to that workspace's chrome window. A panel belonging to no workspace keeps the memory unconditionally,
/// which is the behaviour every kind had before workspaces owned windows.
///
/// Pure over the candidate list, so the rule is testable without windows or a docking surface.
public static class PanelPlacements
{
    /// The window a fresh panel of the given workspace should be placed in.
    ///
    /// Returns fallback when the workspace has no window yet - the caller resolves that to the focused or
    /// active window, which is where a panel opened before its workspace has a window belongs anyway.
    public static Guid PlacementWindow(
        IReadOnlyList<PlaceableWindow> windows, Guid panelWorkspace, Guid remembered, Guid fallback)
    {
        if (KeepsRemembered(windows, panelWorkspace, remembered))
        {
            return remembered;
        }

        foreach (var window in windows)
        {
            if (window.IsPrimary && window.WorkspaceId == panelWorkspace)
            {
                return window.WindowId;
            }
        }

        return fallback;
    }

    // The remembered window survives only if it still exists and does not belong to a different workspace
    // than the panel does. A panel with no workspace of its own is never re-routed.
    private static bool KeepsRemembered(
        IReadOnlyList<PlaceableWindow> windows, Guid panelWorkspace, Guid remembered)
    {
        foreach (var window in windows)
        {
            if (window.WindowId == remembered)
            {
                return panelWorkspace == Guid.Empty || window.WorkspaceId == panelWorkspace;
            }
        }

        return false;
    }
}
