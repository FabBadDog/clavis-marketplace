using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// One row of the overview: everything about a workspace the panel renders, already formatted.
public sealed record WorkspaceOverviewRow(
    Guid WorkspaceId,
    string SlotLabel,
    string Name,
    string AccentKey,
    string WorkingDirectory,
    string Activity,
    string Detail,
    string Elapsed,
    bool IsActive,
    bool HasSession);

/// The pure projection from a workspace list to overview rows. Kept out of the view so the ordering, the
/// gap-preserving slot labels and the elapsed formatting are testable without WPF.
public static class WorkspaceOverviewRows
{
    /// Rows in slot order with gaps preserved, so a freed slot reads as a gap rather than shuffling its
    /// neighbours - the same order the bar uses, because a workspace's slot is its address.
    public static IReadOnlyList<WorkspaceOverviewRow> Build(
        IReadOnlyList<Workspace> workspaces, Guid activeWorkspaceId, DateTimeOffset now) =>
    [
        .. workspaces
            .OrderBy(workspace => workspace.Slot <= 0)
            .ThenBy(workspace => workspace.Slot)
            .Select(workspace => new WorkspaceOverviewRow(
                workspace.WorkspaceId,
                // A workspace past the keyboard range has no key hint, so its slot cell is blank rather than
                // showing a misleading "0".
                workspace.Slot > 0 ? $"F{workspace.Slot}" : "",
                workspace.Name,
                AccentPalette.OrDefault(workspace.AccentKey),
                workspace.WorkingDirectory,
                workspace.Activity,
                workspace.ActivityDetail,
                // Elapsed is only meaningful while something is happening; an idle workspace shows nothing
                // rather than counting up how long it has been doing nothing.
                workspace.Activity == WorkspaceActivity.Idle
                    ? ""
                    : Elapsed(now - workspace.ActivitySince),
                workspace.WorkspaceId == activeWorkspaceId,
                workspace.HasSession))
    ];

    /// Coarse, glanceable elapsed time: seconds under a minute, then minutes, then hours. A negative span (a
    /// clock adjustment, or an activity stamped a moment in the future) reads as zero rather than "-3s".
    public static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            return "0s";
        }

        if (span < TimeSpan.FromMinutes(1))
        {
            return $"{(int)span.TotalSeconds}s";
        }

        return span < TimeSpan.FromHours(1)
            ? $"{(int)span.TotalMinutes}m"
            : $"{(int)span.TotalHours}h {span.Minutes}m";
    }
}
