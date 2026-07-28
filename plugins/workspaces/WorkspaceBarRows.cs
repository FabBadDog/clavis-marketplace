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
    bool IsActive);

/// The pure projection from the workspace list to bar tabs. Kept out of the view so the ordering, the truncation
/// and - most importantly - the activity mapping are testable.
public static class WorkspaceBarRows
{
    /// Beyond this a title is clipped with an ellipsis and the full text moves to the tooltip. The tab is a fixed
    /// width, so a long name must lose characters rather than push its neighbours along.
    public const int MaxTitleLength = 28;

    /// Tabs in slot order with gaps preserved: a slot is an address, so a closed workspace leaves a hole rather
    /// than shuffling everyone left and silently remapping the key you learned.
    public static IReadOnlyList<WorkspaceBarRow> Build(
        IReadOnlyList<Workspace> workspaces, Guid activeWorkspaceId) =>
    [
        .. workspaces
            .OrderBy(workspace => workspace.Slot <= 0)
            .ThenBy(workspace => workspace.Slot)
            .Select(workspace => new WorkspaceBarRow(
                workspace.WorkspaceId,
                workspace.Slot > 0 ? workspace.Slot.ToString() : "",
                Truncate(workspace.Name),
                workspace.Name,
                AccentPalette.OrDefault(workspace.AccentKey),
                ActivityBrushKey(workspace.Activity),
                // Pulsing is reserved for work in progress. "Waiting" is the more urgent state and deliberately
                // does NOT pulse - it draws the eye by colour instead, so it stays legible next to a breathing
                // neighbour rather than competing with it.
                workspace.Activity == WorkspaceActivity.Working,
                workspace.WorkspaceId == activeWorkspaceId))
    ];

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
