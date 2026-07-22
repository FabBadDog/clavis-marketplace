using System;
using System.Linq;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class PermissionViewModel : ObservableObject
{
    private          Permission             _state;
    private readonly Action<string, string> _publishPermission;

    public PermissionViewModel(Permission state, Action<string, string> publishPermission)
    {
        _state = state;
        _publishPermission = publishPermission;

        // One segment per option in order (leading ALLOW, the provider's suggestions, trailing DENY); the
        // deny option is styled destructive. The renderer adapts to whatever count of options is supplied.
        Selector = new SegmentedSelectorModel(
            state.Options
                .Select(option => new SegmentItem(option.Label, option.IsDeny ? "ErrorBrush" : "TextDimBrush"))
                .ToArray());
        // A click commits that option; the highlight (SelectedIndex) is driven from the conversation state,
        // so Left/Right navigation (resolved by the host) stays the single source of truth for the choice.
        Selector.Committed += (_, index) => _publishPermission(_state.RequestId, DecisionFor(index));
        Selector.SelectedIndex = state.SelectedIndex;
    }

    public void Update(Permission state)
    {
        _state = state;
        Selector.SelectedIndex = state.SelectedIndex;
        RefreshAll();
    }

    public Guid PermissionId => _state.PermissionId;
    public string? ReasonText => _state.ReasonText;
    public int SelectedIndex => _state.SelectedIndex;
    public bool IsResolved => _state.IsResolved;

    /// The shared single-select control model: the options, the live highlight, and click commits.
    public SegmentedSelectorModel Selector { get; }

    private string DecisionFor(int index) =>
        index >= 0 && index < _state.Options.Count ? _state.Options[index].Id : "allow";
}
