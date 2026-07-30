using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Conversation;

/// What a chat panel shows instead of a conversation while the workspace it belongs to is taking an agent over.
///
/// There is genuinely nothing else to show: the agent has not let go yet, so no session and no transcript exist
/// for this workspace. Leaving the panel blank would read as a broken chat, so it says what it is waiting for.
public sealed record AdoptionNotice(bool IsVisible, Guid WorkspaceId, string Headline, string Detail)
{
    public static AdoptionNotice Hidden { get; } = new(false, Guid.Empty, "", "");
}

public static class AdoptionNotices
{
    public const string Headline = "Session is still working";

    /// Why it is waiting rather than just taking over: the honest reason, because the alternative on offer
    /// (taking over now) destroys work, and nobody can weigh that without being told.
    public const string Detail =
        "Waiting for it to finish before taking it over. Taking over now would discard the turn it is running.";

    /// Whether a panel bound to a given workspace should show the notice.
    ///
    /// A panel with no workspace of its own follows the active one - that is what Guid.Empty means in a saved
    /// panel blob, and it is also every panel saved before workspaces existed.
    public static AdoptionNotice For(Guid panelWorkspaceId, IReadOnlyList<WorkspaceInfo> workspaces, Guid activeWorkspaceId)
    {
        var target = panelWorkspaceId == Guid.Empty ? activeWorkspaceId : panelWorkspaceId;
        var workspace = workspaces.FirstOrDefault(candidate => candidate.WorkspaceId == target);

        return workspace is { IsAdopting: true }
            ? new AdoptionNotice(true, workspace.WorkspaceId, Headline, Detail)
            : AdoptionNotice.Hidden;
    }
}
