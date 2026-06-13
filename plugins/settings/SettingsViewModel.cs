using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FabioSoft.Nucleus.Plugins.Settings;

public sealed class PluginConfigViewModel(string pluginId, IReadOnlyList<ConfigProperty> properties)
{
    public string PluginId { get; } = pluginId;
    public IReadOnlyList<ConfigProperty> Properties { get; } = properties;
    public bool IsExpanded { get; set; }
}

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PluginConfigViewModel> PluginConfigs { get; } = [];

    public void AddPluginConfig(string pluginId, Type configType)
    {
        var properties = ConfigReflector.Reflect(configType);
        PluginConfigs.Add(new PluginConfigViewModel(pluginId, properties));
        OnPropertyChanged(nameof(HasConfigs));
    }

    public bool HasConfigs => PluginConfigs.Count > 0;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
