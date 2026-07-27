namespace FabioSoft.Nucleus.Plugins.Conversation;

/// Derives a session's outward activity - idle, working, waiting on the user - from the state the
/// conversation already tracks. Deliberately a projection rather than a new SessionStatus case: status is
/// the conversation's own vocabulary and Thinking is honest for a running turn, but "a turn is running"
/// and "the agent is blocked on you" are different facts, and only the latter should pull the eye.
///
/// Pure: a function of the session alone, so the whole matrix is testable without a bus or a dispatcher.
public static class SessionActivityProjection
{
    /// A pending permission outranks a running turn - the turn is technically still active, but what
    /// matters is that it cannot proceed without an answer. Terminal statuses outrank both: an unanswered
    /// prompt on a session that has ended is not waiting for anyone.
    public static string ActivityOf(SessionState session)
    {
        if (session.Status is SessionStatus.Ended or SessionStatus.Aborted or SessionStatus.Aborting)
        {
            return SessionActivity.Idle;
        }

        if (PendingPermission(session) is not null)
        {
            return SessionActivity.Waiting;
        }

        return session.IsProcessing || session.IsCurrentTurnActive
            ? SessionActivity.Working
            : SessionActivity.Idle;
    }

    /// A short phrase for an overview row: what it is waiting on, or what it is doing. Empty when idle,
    /// because a dash reads better than a word that says nothing.
    public static string ActivityDetailOf(SessionState session)
    {
        switch (ActivityOf(session))
        {
            case SessionActivity.Waiting:
                var tool = ToolOf(session, PendingPermission(session)!.ToolUseId);
                return tool is null ? "permission" : $"permission: {tool.Name}";

            case SessionActivity.Working:
                var active = ActiveTool(session);
                return active is not null ? active.Name : PhaseWord(session.Status);

            default:
                return "";
        }
    }

    // The newest still-running tool. Newest rather than first, because a turn accumulates tool rows and the
    // last one started is the one actually occupying the agent right now.
    private static Tool? ActiveTool(SessionState session) =>
        session.Turns
            .SelectMany(turn => turn.Items)
            .OfType<ToolItem>()
            .Select(item => item.Tool)
            .Where(tool => tool.IsActive)
            .OrderByDescending(tool => tool.StartedAt)
            .FirstOrDefault();

    private static Tool? ToolOf(SessionState session, string? toolUseId) =>
        string.IsNullOrEmpty(toolUseId)
            ? null
            : session.Turns
                .SelectMany(turn => turn.Items)
                .OfType<ToolItem>()
                .Select(item => item.Tool)
                .FirstOrDefault(tool => tool.ToolUseId == toolUseId);

    private static Permission? PendingPermission(SessionState session) =>
        session.Turns
            .SelectMany(turn => turn.Items)
            .OfType<PermissionItem>()
            .Select(item => item.Permission)
            .FirstOrDefault(permission => !permission.IsResolved);

    private static string PhaseWord(SessionStatus status) => status switch
    {
        SessionStatus.Thinking => "thinking",
        SessionStatus.Retrying => "retrying",
        SessionStatus.Compacting => "compacting",
        _ => ""
    };
}
