namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// One live panel placement the host can find again: which window holds it, its instance, how it is placed
/// ("tab" or "slide"), and the workspace it belongs to.
public readonly record struct LivePanel(Guid WindowId, Guid PanelId, string Mode, Guid WorkspaceId);

/// Deciding whether an about-to-open panel should reuse a panel that is already live. The host used to
/// dedupe every kind application-wide; it now enforces the kind's *declared* cardinality, which separates
/// the two questions that word conflated: how many may exist, and within what boundary.
///
/// Pure over the candidate list, so the rules are testable without windows or a docking surface.
public static class LivePanels
{
    /// The live panel a new open of this kind should reuse, or null when it should open a fresh one.
    /// Candidates are every live panel of that kind (the caller enumerates the surfaces); exclude drops the
    /// just-minted instance so an open never matches itself.
    ///
    /// Many never reuses. OnePerApplication reuses any candidate, in any window and any workspace.
    /// OnePerWorkspace - the default, and what an empty cardinality means - reuses only a candidate in the
    /// same workspace, so two workspaces each keep their own. Guid.Empty is a workspace like any other here:
    /// the caller resolves "the active workspace" before asking.
    public static LivePanel? Find(
        IReadOnlyList<LivePanel> candidates, string cardinality, Guid workspaceId, Guid exclude)
    {
        if (Normalize(cardinality) == PanelCardinality.Many)
        {
            return null;
        }

        var perApplication = Normalize(cardinality) == PanelCardinality.OnePerApplication;

        foreach (var candidate in candidates)
        {
            if (candidate.PanelId != exclude && (perApplication || candidate.WorkspaceId == workspaceId))
            {
                return candidate;
            }
        }

        return null;
    }

    /// An unset cardinality reads as the default, so a registration that predates the declaration keeps
    /// behaving as it always did.
    public static string Normalize(string? cardinality) =>
        string.IsNullOrWhiteSpace(cardinality) ? PanelCardinality.OnePerWorkspace : cardinality;
}
