using System;
using System.Collections.ObjectModel;
namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class ConversationViewModel : ObservableObject
{
    private          Chat?                  _chat;
    private readonly Action<string, string> _publishPermission;
    private          bool                   _isPromptAvailable;

    public ConversationViewModel(Chat? chat, Action<string, string> publishPermission)
    {
        _chat = chat;
        _publishPermission = publishPermission;
        SyncTurns();
    }

    /// The chat this view model projects. One view model per chat, so a background chat keeps its own
    /// projection - its scroll position has to be right the moment you switch to it.
    public Guid? ChatId => _chat?.ChatId;

    public void Update(Chat? chat)
    {
        _chat = chat;
        SyncTurns();
        RefreshAll();
    }

    private void SyncTurns()
    {
        var session = _chat?.LiveSession;
        var activeTurns = session?.Turns ?? [];
        CollectionSync.Reconcile(
            Turns,
            activeTurns,
            t => t.Id.ToString(),
            vm => vm.TurnId.ToString(),
            turn => new TurnViewModel(turn, _publishPermission),
            (vm, turn) => vm.Update(turn));

        // Project the session phase onto the current turn only, as its rail whisper. Non-working phases
        // map to an empty word (SessionPhase), so this both sets and clears the whisper as the phase moves.
        var currentTurnId = session?.CurrentTurnId;
        var phase = session is not null ? SessionPhase.Whisper(session.Status) : "";
        foreach (var turnViewModel in Turns)
        {
            turnViewModel.PhaseWhisper =
                currentTurnId is { } id && turnViewModel.TurnId == id ? phase : "";
        }
    }

    public ObservableCollection<TurnViewModel> Turns { get; } = [];

    public string? Model => _chat?.LiveSession?.Model;

    public string MetaLabel
    {
        get
        {
            var session = _chat?.LiveSession;
            if (session is null)
            {
                return "";
            }

            var model = session.Model ?? "";
            // While thinking, surface the live reasoning-token estimate (from the provider's thinking_tokens
            // stream) next to the status, e.g. "claude-opus · Thinking (1,240 tokens)".
            if (session.Status == SessionStatus.Thinking && session.ThinkingTokens > 0)
            {
                return $"{model} · Thinking ({session.ThinkingTokens:N0} tokens)";
            }

            return $"{model} · {session.Status}";
        }
    }

    public string ContextLabel
    {
        get
        {
            var session = _chat?.LiveSession;
            if (session is null)
            {
                return "";
            }

            var percent = session.ContextSize > 0
                ? session.ContextFilled * 100 / session.ContextSize
                : 0;
            return $"{Math.Clamp(percent, 0, 100)}%";
        }
    }

    /// True once the agent session can accept prompts, so the chat panel reveals its prompt input. Set by the
    /// plugin rather than derived from state: it latches on the first ready session and never falls back, so a
    /// restart does not take the input away mid-conversation.
    public bool IsPromptAvailable
    {
        get => _isPromptAvailable;
        set
        {
            if (_isPromptAvailable == value)
            {
                return;
            }

            _isPromptAvailable = value;
            OnPropertyChanged();
        }
    }

    /// The active session's permission mode and its short label, which the prompt input renders as its
    /// ambient accent. Set together by the plugin as the mode changes.
    public string PromptMode { get; private set; } = "";

    public string PromptModeDisplayName { get; private set; } = "";

    public void SetPromptMode(string mode, string displayName)
    {
        PromptMode = mode;
        PromptModeDisplayName = displayName;
        OnPropertyChanged(nameof(PromptMode));
    }

    /// True while a permission prompt is awaiting a decision, so the chat panel claims Left/Right/Enter for it.
    public bool IsPermissionPending => ConversationUpdate.HasPendingPermission(_chat);

    public bool IsProcessing => _chat?.LiveSession?.IsProcessing ?? false;

    public bool HasActiveTurn => _chat?.LiveSession?.IsCurrentTurnActive ?? false;

    public int QueuedCount => _chat?.LiveSession?.QueuedCount ?? 0;
}
