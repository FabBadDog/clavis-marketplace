using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// One tab on the workspace bar, already reduced to what the strip renders.
public sealed record WorkspaceBarRow(
    Guid WorkspaceId,
    string SlotNumber,
    string Title,
    string Tooltip,
    string AccentKey,
    string ActivityBrushKey,
    bool IsBreathing,
    bool IsActive,
    bool IsFleetAgent = false);

/// The pure projection from the workspace list to bar tabs. Kept out of the view so the ordering, the truncation
/// and - most importantly - the activity mapping are testable.
public static class WorkspaceBarRows
{
    /// Beyond this a title is clipped with an ellipsis and the full text moves to the tooltip. The tab is a fixed
    /// width, so a long name must lose characters rather than push its neighbours along.
    public const int MaxTitleLength = 28;

    /// Whether a workspace earns a place on the strip. Every tab shown is one you can reach with its F-key, so a
    /// workspace holding no slot is left off - the bar is a keyboard map, and a tab you can only click makes the
    /// numbers beside it look decorative. Those workspaces are still reachable from the picker and the overview.
    ///
    /// Fleet agents are the deliberate exception. They are slotless by design (a short-lived agent somebody
    /// spawned must not consume one of eleven keys), and the tab is how you notice one is running at all and take
    /// it over - hiding them would hide the only place they appear.
    public static bool IsOnBar(Workspace workspace) => workspace.IsFleetAgent || workspace.Slot > 0;

    /// Tabs in slot order with gaps preserved: a slot is an address, so a closed workspace leaves a hole rather
    /// than shuffling everyone left and silently remapping the key you learned.
    public static IReadOnlyList<WorkspaceBarRow> Build(
        IReadOnlyList<Workspace> workspaces, Guid activeWorkspaceId) =>
    [
        .. workspaces
            .Where(IsOnBar)
            .OrderBy(workspace => workspace.Slot <= 0)
            .ThenBy(workspace => workspace.Slot)
            .Select(workspace => new WorkspaceBarRow(
                workspace.WorkspaceId,
                workspace.Slot > 0 ? workspace.Slot.ToString() : "",
                Truncate(workspace.Name),
                Tooltip(workspace),
                AccentPalette.OrDefault(workspace.AccentKey),
                ActivityBrushKey(workspace.Activity),
                // Pulsing is reserved for work in progress. "Waiting" is the more urgent state and deliberately
                // does NOT pulse - it draws the eye by colour instead, so it stays legible next to a breathing
                // neighbour rather than competing with it.
                workspace.Activity == WorkspaceActivity.Working,
                workspace.WorkspaceId == activeWorkspaceId,
                workspace.IsFleetAgent))
    ];

    /// A fleet tab says what it is. Its title is the agent's own name, which on its own gives no hint that the
    /// agent is running outside Clavis or that clicking takes it over - so the tooltip carries that, and the
    /// tab is drawn distinctly.
    private static string Tooltip(Workspace workspace) =>
        workspace.IsFleetAgent
            ? $"{workspace.Name}\nRunning outside Clavis - click to take it over"
            : workspace.Name;

    /// The dot's colour by activity. The dot carries **state** only: it is never tinted with the workspace
    /// accent, which carries identity - doing so would destroy the activity signal the dot exists for.
    public static string ActivityBrushKey(string activity) => activity switch
    {
        WorkspaceActivity.Working => "GreenBrush",
        WorkspaceActivity.Waiting => "WarnBrush",
        _ => "TextDimBrush"
    };

    private static string Truncate(string title) =>
        title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)] + "…";
}
