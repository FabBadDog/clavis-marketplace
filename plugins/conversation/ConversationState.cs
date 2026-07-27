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

/// One chat: its own agent session history with a single live tail, its own working directory, and the
/// workspace it belongs to. This is the level that used to be missing - `ConversationState.Sessions` was
/// doing two jobs at once, a session *history* (a restart ends the old session and appends a new one) and the
/// would-be multi-chat axis. Separating them is what lets several chats coexist without N locks or N timers.
///
/// `SessionState` is deliberately untouched by this: it was already fully per-session.
public sealed record Chat
{
    public Guid ChatId { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; init; }
    public string WorkingDirectory { get; init; } = "";

    /// Every session this chat has had, oldest first, with the live one last. A restart ends the previous
    /// session and appends its replacement, so the history stays readable rather than being discarded.
    public IReadOnlyList<SessionState> Sessions { get; init; } = [];

    public Guid LiveSessionId { get; init; }

    public SessionState? LiveSession => Sessions.FirstOrDefault(session => session.Id == LiveSessionId);

    public bool Holds(Guid sessionId) => Sessions.Any(session => session.Id == sessionId);

    public Chat WithLiveSession(Func<SessionState, SessionState> updater) =>
        LiveSession is { } live ? WithSessionById(live.Id, updater) : this;

    public Chat WithSessionById(Guid sessionId, Func<SessionState, SessionState> updater)
    {
        if (!Holds(sessionId))
        {
            return this;
        }

        return this with
        {
            Sessions = [.. Sessions.Select(session => session.Id == sessionId ? updater(session) : session)]
        };
    }

    /// End the live session and make the given one live, keeping the ended session in the history. The pure
    /// half of a full restart - dispatching and disposing stay in ConversationUpdate as effects.
    public Chat Restarted(SessionState replacement)
    {
        var ended = WithLiveSession(session => session with { Status = SessionStatus.Ended });
        return ended with
        {
            Sessions = [.. ended.Sessions, replacement],
            LiveSessionId = replacement.Id
        };
    }

    public static Chat Create(Guid workspaceId, string workingDirectory) =>
        Create(workspaceId, workingDirectory, Guid.NewGuid());

    /// Create a chat around a session id someone else minted - which is the normal path: Workspaces owns
    /// session creation, because the working directory is per workspace, and tells the conversation which
    /// session its new chat is for.
    public static Chat Create(Guid workspaceId, string workingDirectory, Guid sessionId)
    {
        var session = SessionState.Create() with { Id = sessionId };
        return new Chat
        {
            WorkspaceId = workspaceId,
            WorkingDirectory = workingDirectory,
            Sessions = [session],
            LiveSessionId = session.Id
        };
    }
}

/// Every chat in one aggregate, with one pure update and one lock. N independent states would mean N locks,
/// N tick timers and no cheap cross-chat answers ("is anything running?"), which is exactly what the bar and
/// the activity stream need.
public sealed record ConversationState
{
    public IReadOnlyList<Chat> Chats { get; init; } = [];

    /// The chat the user is currently looking at. Null with no chats at all.
    public Guid? VisibleChatId { get; init; }

    public Chat? VisibleChat =>
        VisibleChatId is { } id ? Chats.FirstOrDefault(chat => chat.ChatId == id) : null;

    /// The live session of the visible chat - what every user-driven handler acts on, and what the chrome
    /// and placeholders project from.
    public SessionState? ActiveSession => VisibleChat?.LiveSession;

    public Guid? ActiveSessionId => ActiveSession?.Id;

    /// Every session of every chat. The activity stream walks this, not just the visible chat: the whole
    /// point is that a chat you are not looking at can still say it needs you.
    public IEnumerable<SessionState> AllSessions => Chats.SelectMany(chat => chat.Sessions);

    public bool Holds(Guid sessionId) => Chats.Any(chat => chat.Holds(sessionId));

    public SessionState? SessionById(Guid sessionId) =>
        Chats.SelectMany(chat => chat.Sessions).FirstOrDefault(session => session.Id == sessionId);

    /// Update one chat, leaving every other chat reference-identical so the view projection can skip them.
    public ConversationState WithChat(Guid chatId, Func<Chat, Chat> updater)
    {
        if (Chats.All(chat => chat.ChatId != chatId))
        {
            return this;
        }

        return this with
        {
            Chats = [.. Chats.Select(chat => chat.ChatId == chatId ? updater(chat) : chat)]
        };
    }

    /// Look at a different chat. No chat record changes, so every projection is skipped - switching is only a
    /// change of which chat the chrome and the user-driven handlers address.
    public ConversationState WithVisibleChatId(Guid? chatId) => this with { VisibleChatId = chatId };

    public ConversationState WithVisibleChat(Func<Chat, Chat> updater) =>
        VisibleChat is { } chat ? WithChat(chat.ChatId, updater) : this;

    public ConversationState WithActiveSession(Func<SessionState, SessionState> updater) =>
        WithVisibleChat(chat => chat.WithLiveSession(updater));

    /// Update a session wherever it lives, so a stream event routes to the right chat by its session id
    /// alone. Chats that do not hold it stay reference-identical.
    public ConversationState WithSessionById(Guid sessionId, Func<SessionState, SessionState> updater)
    {
        var owner = Chats.FirstOrDefault(chat => chat.Holds(sessionId));
        return owner is null ? this : WithChat(owner.ChatId, chat => chat.WithSessionById(sessionId, updater));
    }

    /// No chats at all. Once workspaces own chat creation this is the honest starting point; until then
    /// Init seeds the single chat the plugin has always created.
    public static ConversationState Empty => new();

    public static ConversationState Init() => Init(Guid.Empty, "");

    public static ConversationState Init(Guid workspaceId, string workingDirectory)
    {
        var chat = Chat.Create(workspaceId, workingDirectory);
        return new ConversationState { Chats = [chat], VisibleChatId = chat.ChatId };
    }
}
