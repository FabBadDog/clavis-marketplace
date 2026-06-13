using System;
using System.Collections.ObjectModel;
namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class ConversationViewModel : ObservableObject
{
    private          ConversationState      _state;
    private readonly Action<string, string> _publishPermission;

    public ConversationViewModel(ConversationState state, Action<string, string> publishPermission)
    {
        _state = state;
        _publishPermission = publishPermission;
        SyncTurns();
    }

    public void Update(ConversationState state)
    {
        _state = state;
        SyncTurns();
        RefreshAll();
    }

    private void SyncTurns()
    {
        var activeTurns = _state.ActiveSession?.Turns ?? [];
        CollectionSync.Reconcile(
            Turns,
            activeTurns,
            t => t.Id.ToString(),
            vm => vm.TurnId.ToString(),
            turn => new TurnViewModel(turn, _publishPermission),
            (vm, turn) => vm.Update(turn));
    }

    public ObservableCollection<TurnViewModel> Turns { get; } = [];

    public string? Model => _state.ActiveSession?.Model;

    public string MetaLabel
    {
        get
        {
            var session = _state.ActiveSession;
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
            var session = _state.ActiveSession;
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

    public bool IsProcessing => _state.ActiveSession?.IsProcessing ?? false;

    public bool HasActiveTurn => _state.ActiveSession?.IsCurrentTurnActive ?? false;

    public int QueuedCount => _state.ActiveSession?.QueuedCount ?? 0;
}
