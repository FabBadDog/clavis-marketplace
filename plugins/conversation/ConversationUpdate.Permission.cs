namespace FabioSoft.Nucleus.Plugins.Conversation;

public static partial class ConversationUpdate
{
    // The permission prompt offers three choices in this order: ALLOW (0), DENY (1), ALWAYS ALLOW (2).
    // Left/Right move the highlight within this range; Enter confirms the choice at the current index.
    private const int LastPermissionIndex = 2;

    /// True while a permission prompt is awaiting a decision - the host uses this to route Left/Right/Enter
    /// to the prompt without taking tab focus.
    public static bool HasPendingPermission(ConversationState state) => PendingPermission(state) is not null;

    /// Move the highlighted choice of the pending permission prompt by delta (Left = -1, Right = +1),
    /// clamped to the available options. A no-op when nothing is awaiting a decision, so stray arrow
    /// keys never mutate resolved history.
    public static (ConversationState State, ConversationEffect[] Effects) HandlePermissionNavigate(
        ConversationState state, int delta)
    {
        if (PendingPermission(state) is not { } pending)
        {
            return (state, NoEffects);
        }

        var newIndex = Math.Clamp(pending.Permission.SelectedIndex + delta, 0, LastPermissionIndex);
        if (newIndex == pending.Permission.SelectedIndex)
        {
            return (state, NoEffects);
        }

        var targetId = pending.Permission.PermissionId;
        var turns = state.ActiveSession!.Turns.Select(turn =>
            turn.WithItems(turn.Items.Select(item =>
                item is PermissionItem pi && pi.Permission.PermissionId == targetId
                    ? new PermissionItem(pi.Permission with { SelectedIndex = newIndex })
                    : item).ToList())).ToList();

        return (state.WithActiveSession(session => session with { Turns = turns }), NoEffects);
    }

    /// Confirm the pending permission prompt at its currently highlighted choice (the keyboard Enter
    /// path), reusing the same resolution as a button click. A no-op when nothing is awaiting a decision.
    public static (ConversationState State, ConversationEffect[] Effects) HandlePermissionConfirm(
        ConversationState state)
    {
        if (PendingPermission(state) is not { } pending)
        {
            return (state, NoEffects);
        }

        return HandlePermissionDecided(
            state, pending.Permission.RequestId, PermissionDecisionAt(pending.Permission.SelectedIndex));
    }

    /// The neutral decision string for a highlighted choice index, matching the permission buttons'
    /// ALLOW / DENY / ALWAYS ALLOW order.
    public static string PermissionDecisionAt(int index) => index switch
    {
        1 => "deny",
        2 => "allow_always",
        _ => "allow"
    };

    private static PermissionItem? PendingPermission(ConversationState state) =>
        state.ActiveSession is { } session
            ? session.Turns
                .SelectMany(turn => turn.Items)
                .OfType<PermissionItem>()
                .FirstOrDefault(item => !item.Permission.IsResolved)
            : null;
}
