namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

/// One entry in a turn's stats column: a geometric icon name and its value (both rendered dim). Value is mutable + INPC so
/// a live turn's stats update in place across ticks without rebuilding the collection.
public sealed class MicroStatViewModel : ObservableObject
{
    private string _value;

    public MicroStatViewModel(string icon, string value)
    {
        Icon = icon;
        _value = value;
    }

    public string Icon { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }
            _value = value;
            OnPropertyChanged();
        }
    }
}
