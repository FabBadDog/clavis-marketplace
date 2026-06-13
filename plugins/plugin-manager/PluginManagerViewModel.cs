using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FabioSoft.Nucleus.Plugins.PluginManager;

public sealed class PluginEntryViewModel : INotifyPropertyChanged
{
    private string _state;

    public PluginEntryViewModel(string pluginId, string state, Action<string> unload)
    {
        PluginId = pluginId;
        _state = state;
        UnloadCommand = new SimpleCommand(() => unload(pluginId));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PluginId { get; }

    public string State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsError)));
        }
    }

    public bool IsActive => _state == "Active";
    public bool IsError => _state == "Error";

    public ICommand UnloadCommand { get; }

    private sealed class SimpleCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}

public sealed class PluginManagerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PluginEntryViewModel> Plugins { get; } = [];

    public int ActiveCount => Plugins.Count(p => p.IsActive);
    public int TotalCount => Plugins.Count;

    public void AddOrUpdate(string pluginId, string state, Action<string> unload)
    {
        var existing = Plugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (existing is not null)
        {
            existing.State = state;
        }
        else
        {
            Plugins.Add(new PluginEntryViewModel(pluginId, state, unload));
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    public void Remove(string pluginId)
    {
        var existing = Plugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (existing is not null)
        {
            Plugins.Remove(existing);
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(TotalCount));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
