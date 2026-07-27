namespace FabioSoft.Nucleus.Plugins.Conversation;

/// Remembers what was last announced for each session so only transitions reach the bus - a streaming turn
/// updates conversation state many times a second, and an activity indicator changes far less often.
///
/// `now` is a parameter rather than a clock read, so the transition rules are deterministic under test.
public sealed class SessionActivityTracker
{
    private readonly Dictionary<Guid, (string Activity, string Detail, DateTimeOffset Since)> _announced = [];

    /// The message to announce for this session, or null when nothing worth announcing changed.
    public SessionActivityChanged? Next(SessionState session, DateTimeOffset now)
    {
        var activity = SessionActivityProjection.ActivityOf(session);
        var detail = SessionActivityProjection.ActivityDetailOf(session);
        var known = _announced.TryGetValue(session.Id, out var previous);

        if (known && previous.Activity == activity && previous.Detail == detail)
        {
            return null;
        }

        // Since tracks the activity, not the detail: moving from one tool to the next within the same
        // working stretch must not restart the elapsed clock an overview row renders.
        var since = known && previous.Activity == activity ? previous.Since : now;
        _announced[session.Id] = (activity, detail, since);
        return new SessionActivityChanged(session.Id, activity, detail, since);
    }

    /// Drop a session that has gone away, so a long-lived tracker does not accumulate dead entries.
    public void Forget(Guid sessionId) => _announced.Remove(sessionId);
}
