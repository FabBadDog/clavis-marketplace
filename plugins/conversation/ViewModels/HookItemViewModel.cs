using FabioSoft.Common;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class HookItemViewModel : ObservableObject
{
    private Hook _state;

    public HookItemViewModel(Hook state) => _state = state;

    public void Update(Hook state) { _state = state; RefreshAll(); }

    public string HookId => _state.HookId;
    public string DisplayName => _state.DisplayName;
    public bool IsHeader => _state.IsHeader;
    public bool IsActive => _state.IsActive;
    public string DurationText => Formatting.duration(_state.Duration);
    public bool ShowDuration => !_state.IsHeader;

    public string IndicatorText =>
        _state.IsHeader ? "" : _state.HasSucceeded == false ? "! " : "-> ";

    public bool IsSucceeded => _state.HasSucceeded == true;
    public bool IsFailed => _state.HasSucceeded == false;

    public double IndicatorOpacity => _state.HasSucceeded switch
    {
        null => 0.55,
        true => 0.85,
        false => 1.0
    };
}
