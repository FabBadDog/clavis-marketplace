using System.Globalization;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Conversation;

/// Pure projection of the active session onto the `agent.*` and `turn.*` placeholder values published by the
/// Conversation provider. Deterministic and tested; the impure plugin shell broadcasts the result.
public static class AgentValues
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static IReadOnlyDictionary<string, string> Build(SessionState? session)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (session is null)
        {
            return values;
        }

        var percent = session.ContextSize > 0
            ? Math.Clamp(session.ContextFilled * 100 / session.ContextSize, 0, 100)
            : 0;

        values["agent.model"] = session.Model ?? "";
        values["agent.mode"] = session.Mode;
        values["agent.effort"] = session.Effort;

        // Display names resolved from the provider's rich capability catalog; the raw internal ids above
        // stay available for templates (and the badge's color mapping), but the chrome shows these.
        values["agent.modelName"] = DisplayName(
            session.Models.FirstOrDefault(m => SameId(m.Id, session.Model))?.DisplayName, session.Model ?? "");
        values["agent.effortName"] = DisplayName(
            session.Efforts.FirstOrDefault(e => SameId(e.Id, session.Effort))?.DisplayName, session.Effort);
        values["agent.modeName"] = DisplayName(
            session.Modes.FirstOrDefault(m => SameId(m.Id, session.Mode))?.DisplayName, session.Mode);
        values["agent.status"] = session.Status.ToString();
        values["agent.contextUsed"] = session.ContextFilled.ToString(Invariant);
        values["agent.contextWindow"] = session.ContextSize.ToString(Invariant);
        values["agent.contextUsedShort"] = ShortCount(session.ContextFilled);
        values["agent.contextWindowShort"] = ShortCount(session.ContextSize);
        values["agent.contextPercent"] = percent.ToString(Invariant);
        values["agent.queued"] = session.QueuedCount.ToString(Invariant);
        values["agent.thinkingTokens"] = session.ThinkingTokens.ToString(Invariant);

        var turn = ActiveOrLastTurn(session);
        if (turn is not null)
        {
            values["turn.runtime"] = Duration(turn.Duration);
            values["turn.tokens"] = turn.TotalTokens.ToString(Invariant);
            values["turn.status"] = turn.Status.GetType().Name;
        }

        return values;
    }

    private static bool SameId(string id, string? other) =>
        string.Equals(id, other, StringComparison.OrdinalIgnoreCase);

    private static string DisplayName(string? resolved, string fallback) =>
        string.IsNullOrEmpty(resolved) ? fallback : resolved;

    private static Turn? ActiveOrLastTurn(SessionState session)
    {
        var id = session.CurrentTurnId ?? session.LastTurnId;
        return id is { } turnId ? session.Turns.FirstOrDefault(t => t.Id == turnId) : null;
    }

    private static string ShortCount(int value)
    {
        if (value >= 1_000_000)
        {
            return (value / 1_000_000.0).ToString("0.#", Invariant) + "M";
        }

        return value >= 1_000
            ? (value / 1_000).ToString(Invariant) + "k"
            : value.ToString(Invariant);
    }

    private static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 60
            ? duration.TotalSeconds.ToString("0.0", Invariant) + "s"
            : $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
}
