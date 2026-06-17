using FabioSoft.Common;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class ToolItemViewModel : ObservableObject
{
    private Tool _state;

    // Expansion is pure view state (which row the user opened), not domain state, so it lives here as a
    // plain bindable flag rather than in the elm model. It must survive Update (a tool result refreshing
    // the row should not collapse a panel the user opened), so it is kept separate from _state.
    private bool _isExpanded;

    public ToolItemViewModel(Tool state) => _state = state;

    public void Update(Tool state) { _state = state; RefreshAll(); }

    public string ToolUseId => _state.ToolUseId;
    public string NameText => _state.Name.ToUpperInvariant();
    public string ArgumentsText => _state.Arguments;
    public bool IsActive => _state.IsActive;
    public string DurationText => Formatting.duration(_state.Duration);
    public string StatusText => _state.StatusText;
    public bool ShowDuration => _state.ShowDuration;
    public bool ShowWarning => _state.ShowWarning;
    public string WarningText => _state.WarningText;
    public string ScopeBadgeText => _state.ScopeBadgeText;
    public BadgeViewModel ScopeBadge => new(ScopeBadgeText, "SecondaryBrush");
    public bool IsDenied => _state.IsDenied;

    // Detailed-output: the complete input/output, shown only when the row is expanded.
    public string FullArgumentsText => _state.FullArguments;
    public string FullOutputText => _state.FullOutput;

    public bool HasDetail =>
        !string.IsNullOrEmpty(_state.FullArguments) || !string.IsNullOrEmpty(_state.FullOutput);

    // The combined input + output shown in the expanded panel.
    public string DetailText
    {
        get
        {
            var input = _state.FullArguments ?? "";
            var output = _state.FullOutput ?? "";
            if (input.Length > 0 && output.Length > 0)
            {
                return $"{input}\n\n{output}";
            }

            return input + output;
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }
}
