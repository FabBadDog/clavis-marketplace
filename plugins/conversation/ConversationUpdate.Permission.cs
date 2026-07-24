namespace FabioSoft.Nucleus.Plugins.Conversation;

public static partial class ConversationUpdate
{
    // The permission prompt's choices are dynamic: a leading ALLOW, one segment per provider suggestion,
    // and a trailing DENY. Left/Right move the highlight within [0, Options.Count - 1]; Enter confirms the
    // choice at the current index.

    /// True while a permission prompt is awaiting a decision - the host uses this to route Left/Right/Enter
    /// to the prompt without taking tab focus.
    public static bool HasPendingPermission(ConversationState state) => PendingPermission(state) is not null;

    /// Move the highlighted choice of the pending permission prompt by delta (Left = -1, Right = +1),
    /// wrapping around the ends so the prompt is a roundtrip (Left on ALLOW lands on DENY and vice-versa).
    /// A no-op when nothing is awaiting a decision, so stray arrow keys never mutate resolved history.
    public static (ConversationState State, ConversationEffect[] Effects) HandlePermissionNavigate(
        ConversationState state, int delta)
    {
        if (PendingPermission(state) is not { } pending)
        {
            return (state, NoEffects);
        }

        var count = pending.Permission.Options.Count;
        if (count == 0)
        {
            return (state, NoEffects);
        }

        var current = pending.Permission.SelectedIndex;
        var newIndex = (((current + delta) % count) + count) % count;
        if (newIndex == current)
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
            state, pending.Permission.RequestId, PermissionDecisionAt(pending.Permission));
    }

    /// The chosen option's id for the highlighted choice index. Clamps into the option list so a stale
    /// index falls back to allow-once rather than throwing.
    public static string PermissionDecisionAt(Permission permission)
    {
        var options = permission.Options;
        if (options.Count == 0)
        {
            return "allow";
        }

        var index = Math.Clamp(permission.SelectedIndex, 0, options.Count - 1);
        return options[index].Id;
    }

    private static PermissionItem? PendingPermission(ConversationState state) =>
        state.ActiveSession is { } session
            ? session.Turns
                .SelectMany(turn => turn.Items)
                .OfType<PermissionItem>()
                .FirstOrDefault(item => !item.Permission.IsResolved)
            : null;
}
