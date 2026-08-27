namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Region contributions across N workspace windows.
///
/// A contributor announces its chrome once, when it activates - long before the second workspace has a window
/// to put it in. So the host remembers every contribution and (re-)applies the applicable ones whenever the
/// set of workspace windows or the active workspace changes. That is what lets `RegionManager` stay a
/// per-window thing that never learns windows come and go.
///
/// Two kinds of contribution, and the difference is about state, not about WPF:
///
/// - **Workspace-scoped** (`WorkspaceId` set) goes to that workspace's window and stays there. A contributor
///   sends one per workspace, each bound to that workspace's own view model.
/// - **Unscoped** (`Guid.Empty`) belongs to whichever workspace is on screen, and is *moved* there on a
///   switch. It has to be moved rather than copied because today's contributors hand back one long-lived
///   element from their factory (`() => titleLeft.Element`), and a WPF element has one parent. Moving is
///   honest while exactly one workspace is visible: the element is only ever needed in one place at a time.
///   A contributor that grows per-workspace state stops being unscoped and the move stops applying to it.
internal sealed partial class WindowManager
{
    // Every non-bar contribution still standing, newest per (region, plugin, workspace). A list rather than a
    // dictionary because replay order is arrival order, which is what RegionManager's stable priority sort
    // relies on to keep equal-priority winners from flipping between rebuilds.
    private readonly List<UiRegionContribution> _contributions = [];

    private void RememberContribution(UiRegionContribution contribution)
    {
        _contributions.RemoveAll(entry =>
            entry.RegionId == contribution.RegionId
            && entry.PluginId == contribution.PluginId
            && entry.WorkspaceId == contribution.WorkspaceId);

        _contributions.Add(contribution);
    }

    /// The window a contribution belongs in right now: the workspace it names, or the workspace on screen when
    /// it names none. Null while that workspace has no window yet - ApplyContributions replays it when one
    /// appears.
    private WindowHost? ContributionTarget(UiRegionContribution contribution) =>
        contribution.WorkspaceId == Guid.Empty
            ? GetPrimary()
            : WorkspaceWindow(contribution.WorkspaceId);

    /// Put a contribution in its window, taking it out of any other window that still holds it. The removal is
    /// what makes the unscoped case work at all: setting a presenter's Content to an element another presenter
    /// still owns is the one thing WPF refuses outright.
    private void PlaceContribution(UiRegionContribution contribution)
    {
        var target = ContributionTarget(contribution);
        foreach (var host in _windows.Values.Where(host => host.IsPrimary && !ReferenceEquals(host, target)))
        {
            host.Regions.RemoveContribution(new UiRegionRemoved(contribution.RegionId, contribution.PluginId));
        }

        target?.Regions.AddContribution(contribution);
    }

    /// Re-apply every remembered contribution. Called when a workspace window is created, when one is adopted
    /// onto a workspace, and after a workspace switch - the three moments at which "which window does this
    /// belong in" can have changed.
    private void ApplyContributions()
    {
        foreach (var contribution in _contributions.ToList())
        {
            PlaceContribution(contribution);
        }
    }
}
