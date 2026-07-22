using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Conversation;

public enum TurnKind { InitTurn, Interaction }

public enum SessionStatus
{
    Idle,
    Ready,
    Thinking,
    Retrying,
    Compacting,
    Aborting,
    Aborted,
    Ended
}

public abstract record TurnStatus;
public sealed record Queued : TurnStatus;
public sealed record Running : TurnStatus;
public sealed record Succeeded : TurnStatus;
public sealed record Failed(string ErrorMessage) : TurnStatus;
public sealed record Aborted : TurnStatus;

public sealed record Phase
{
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; }
    public bool HasSucceeded { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
}

public sealed record Hook
{
    public string HookId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsHeader { get; init; }
    public bool IsActive { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
    public bool? HasSucceeded { get; init; }
}

public sealed record Tool
{
    public string ToolUseId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Arguments { get; init; } = "";
    // The complete, untruncated input and output for the expand-to-detail view (detailed-output mode).
    public string FullArguments { get; init; } = "";
    public string Output { get; init; } = "";
    public string FullOutput { get; init; } = "";
    public bool IsActive { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
    public string StatusText { get; init; } = "";
    public bool ShowDuration { get; init; } = true;
    public bool ShowWarning { get; init; }
    public string WarningText { get; init; } = "";
    public string ScopeBadgeText { get; init; } = "";
    public bool IsDenied { get; init; }
}

// One choice in a permission prompt: Id is echoed back to identify the pick ("allow", "deny", or a
// provider suggestion id), Label is the display text, IsDeny drives the destructive (error) styling.
public sealed record PermissionOption(string Id, string Label, bool IsDeny);

public sealed record Permission
{
    public Guid PermissionId { get; init; } = Guid.NewGuid();
    public string? ReasonText { get; init; }
    public int SelectedIndex { get; init; }
    public bool IsResolved { get; init; }
    public string? MatchedRulePattern { get; init; }
    public string? MatchedRuleScope { get; init; }
    public string? ToolUseId { get; init; }
    public string RequestId { get; init; } = "";
    // The ordered choices shown to the user: a leading ALLOW, one segment per provider suggestion, a
    // trailing DENY. Always has at least ALLOW and DENY; SelectedIndex points into this list.
    public IReadOnlyList<PermissionOption> Options { get; init; } = [];
}

public abstract record TurnItem;
public sealed record PhaseItem(Phase Phase) : TurnItem;
public sealed record HookItem(Hook Hook) : TurnItem;
public sealed record ToolItem(Tool Tool) : TurnItem;
public sealed record PermissionItem(Permission Permission) : TurnItem;

// An assistant text block, rendered as markdown interleaved with the tool/hook rows in arrival order
// (detailed-output mode shows every block, not just the final answer). Stable id for collection keying.
public sealed record TextItem(string Markdown) : TurnItem
{
    public Guid TextId { get; init; } = Guid.NewGuid();
}

// A reasoning (thinking) block, shown dimmed and collapsed-by-default with an expand toggle.
public sealed record ThinkingItem(string Text) : TurnItem
{
    public Guid ThinkingId { get; init; } = Guid.NewGuid();
}
public sealed record ErrorItem(string Message) : TurnItem
{
    // A stable per-item id so collection reconciliation keys errors uniquely. Keying by
    // Message.GetHashCode() collapses two identical error messages into one row (dropping the second);
    // ErrorItems are created once and carried immutably, so this id stays stable across updates.
    public Guid ErrorId { get; init; } = Guid.NewGuid();
}

public sealed record QueuedTurn(Guid Id, string Prompt);

public sealed record Turn
{
    public Guid                    Id              { get; init; } = Guid.NewGuid();
    public TurnKind                Kind            { get; init; } = TurnKind.Interaction;
    public string                  Prompt          { get; init; } = "";
    public int                     EstimatedTokens { get; init; }
    public int                     TotalTokens     { get; init; }
    public TurnStatus              Status          { get; init; } = new Running();
    public DateTime                StartedAt       { get; init; } = DateTime.UtcNow;
    public TimeSpan                Duration        { get; init; }
    public string                  StatusText      { get; init; } = "";
    public string                  Response        { get; init; } = "";
    public IReadOnlyList<TurnItem> Items           { get; init; } = [];

    public Turn WithStatus(TurnStatus status) => this with { Status = status };
    public Turn WithItems(IReadOnlyList<TurnItem> items) => this with { Items = items };
    public Turn WithStatusText(string text) => this with { StatusText = text };
    public Turn WithResponse(string response) => this with { Response = response };
    public Turn WithDuration(TimeSpan duration) => this with { Duration = duration };
    public Turn WithTotalTokens(int tokens) => this with { TotalTokens = tokens };
    public Turn WithStartedAt(DateTime startedAt) => this with { StartedAt = startedAt };
}

public sealed record InitState
{
    public bool FirstEventReceived { get; init; }
    public bool HookHeaderShown { get; init; }
    public int PendingSessionStartHooks { get; init; }

    public static InitState Default => new();
}

public sealed record SessionState
{
    private const int ContextTokenBudget = 200_000;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Model { get; init; }
    // Current permission mode (e.g. "default", "plan") and reasoning effort (e.g. "high") by internal id,
    // plus the rich choice catalogs the provider offers - reported by the bridge via AgentCapabilities
    // and updated by the Agent*Changed confirmations. Empty until reported.
    public string Mode { get; init; } = "";
    public string Effort { get; init; } = "";
    public IReadOnlyList<AgentModelInfo> Models { get; init; } = [];
    public IReadOnlyList<AgentModeInfo> Modes { get; init; } = [];
    public IReadOnlyList<AgentEffortInfo> Efforts { get; init; } = [];
    public SessionStatus Status { get; init; } = SessionStatus.Idle;
    public int ContextSize { get; init; } = ContextTokenBudget;
    public int ContextFilled { get; init; }
    // Running estimate of reasoning tokens for the current thinking burst; 0 when not thinking.
    public int ThinkingTokens { get; init; }
    public InitState? InitState { get; init; } = InitState.Default;
    public IReadOnlySet<string> KnownToolUseIds { get; init; } = new HashSet<string>();
    public IReadOnlyList<Turn> Turns { get; init; } = [];
    public Guid? CurrentTurnId { get; init; }
    public Guid? LastTurnId { get; init; }
    public IReadOnlyList<QueuedTurn> QueuedTurnIds { get; init; } = [];

    public bool IsInitActive => InitState is not null;

    public bool IsCurrentTurnActive =>
        CurrentTurnId is { } id &&
        Turns.Any(t => t.Id == id && t.Status is Running);

    public bool IsProcessing =>
        Status is SessionStatus.Thinking
            or SessionStatus.Retrying
            or SessionStatus.Compacting;

    public int QueuedCount => QueuedTurnIds.Count;

    public Guid? InitTurnId =>
        Turns.Where(t => t.Kind == TurnKind.InitTurn).Select(t => (Guid?)t.Id).FirstOrDefault();

    public SessionState WithStatus(SessionStatus status) => this with { Status = status };
    public SessionState WithModel(string? model) => this with { Model = model };
    public SessionState WithInitState(InitState? state) => this with { InitState = state };
    public SessionState WithTurns(IReadOnlyList<Turn> turns) => this with { Turns = turns };
    public SessionState WithCurrentTurnId(Guid? id) => this with { CurrentTurnId = id };
    public SessionState WithLastTurnId(Guid? id) => this with { LastTurnId = id };
    public SessionState WithQueuedTurnIds(IReadOnlyList<QueuedTurn> ids) => this with { QueuedTurnIds = ids };
    public SessionState WithContextFilled(int filled) => this with { ContextFilled = filled };
    public SessionState WithKnownToolUseIds(IReadOnlySet<string> ids) => this with { KnownToolUseIds = ids };

    public static SessionState Create()
    {
        var initTurn = new Turn
        {
            Kind = TurnKind.InitTurn,
            Items = [new PhaseItem(new Phase
            {
                DisplayName = "Starting Claude",
                IsActive = true
            })]
        };

        return new SessionState { Turns = [initTurn] };
    }
}

public sealed record ConversationState
{
    public IReadOnlyList<SessionState> Sessions { get; init; } = [];
    public Guid? ActiveSessionId { get; init; }

    public SessionState? ActiveSession =>
        ActiveSessionId is { } id
            ? Sessions.FirstOrDefault(s => s.Id == id)
            : null;

    public ConversationState WithActiveSession(Func<SessionState, SessionState> updater)
    {
        if (ActiveSession is not { } session)
        {
            return this;
        }

        var updated = updater(session);
        return this with
        {
            Sessions = Sessions.Select(s => s.Id == session.Id ? updated : s).ToList()
        };
    }

    public ConversationState WithSessionById(Guid sessionId, Func<SessionState, SessionState> updater)
    {
        if (Sessions.All(s => s.Id != sessionId))
        {
            return this;
        }

        return this with
        {
            Sessions = Sessions.Select(s => s.Id == sessionId ? updater(s) : s).ToList()
        };
    }

    public static ConversationState Init()
    {
        var session = SessionState.Create();
        return new ConversationState
        {
            Sessions = [session],
            ActiveSessionId = session.Id
        };
    }
}
