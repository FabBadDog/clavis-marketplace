using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RefreshAll()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
