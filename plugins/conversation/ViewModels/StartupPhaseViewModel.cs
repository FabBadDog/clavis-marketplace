using FabioSoft.Common;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class StartupPhaseViewModel : ObservableObject
{
    private Phase _state;

    public StartupPhaseViewModel(Phase state) => _state = state;

    public void Update(Phase state) { _state = state; RefreshAll(); }

    public string DisplayName => _state.DisplayName;
    public string DetailText => _state.Detail;
    public bool IsActive => _state.IsActive;
    public bool Succeeded => _state.HasSucceeded;
    public string DurationText => Formatting.duration(_state.Duration);
}
