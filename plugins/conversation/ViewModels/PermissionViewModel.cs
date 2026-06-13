using System;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class PermissionViewModel : ObservableObject
{
    // The decision published for each option index, matching the prompt's order (and the conversation
    // state's SelectedIndex): Allow, Deny, Always allow.
    private static readonly string[] Decisions = ["allow", "deny", "allow_always"];

    private          Permission             _state;
    private readonly Action<string, string> _publishPermission;

    public PermissionViewModel(Permission state, Action<string, string> publishPermission)
    {
        _state = state;
        _publishPermission = publishPermission;

        Selector = new SegmentedSelectorModel(
        [
            new SegmentItem("ALLOW", "TextDimBrush"),
            new SegmentItem("DENY", "ErrorBrush"),
            new SegmentItem("ALWAYS ALLOW", "TextDimBrush")
        ]);
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

    /// The shared single-select control model: the three options, the live highlight, and click commits.
    public SegmentedSelectorModel Selector { get; }

    private static string DecisionFor(int index) =>
        index >= 0 && index < Decisions.Length ? Decisions[index] : Decisions[0];
}
