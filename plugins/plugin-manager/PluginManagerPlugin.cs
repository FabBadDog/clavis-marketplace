using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.PluginManager;

public sealed class PluginManagerPlugin : IPlugin<PluginManagerConfig>
{
    public string Id => "PluginManager";

    public PluginManagerConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(PluginManagerConfig config)
        => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, PluginManagerConfig config)
    {
        PluginManagerViewModel? viewModel = null;

        if (Application.Current is not null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                viewModel = new PluginManagerViewModel();
            });
        }

        var activatedSub = bus.Subscribe<PluginActivated>(msg =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                viewModel?.AddOrUpdate(msg.PluginId, "Active", Unload));
            return Task.CompletedTask;
        });

        var deactivatedSub = bus.Subscribe<PluginDeactivated>(msg =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                viewModel?.Remove(msg.PluginId));
            return Task.CompletedTask;
        });

        var errorSub = bus.Subscribe<PluginError>(msg =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                viewModel?.AddOrUpdate(msg.PluginId, "Error", Unload));
            return Task.CompletedTask;
        });

        bus.Send(new LogEntry(
            LogLevel.Info,
            "PluginManager",
            "Plugin manager plugin activated",
            DateTimeOffset.UtcNow));

        return Task.FromResult<IDisposable>(new PluginDisposable(activatedSub, deactivatedSub, errorSub));

        void Unload(string pluginId) => bus.Send(new UnloadPlugin(pluginId));
    }

    private sealed class PluginDisposable(params ISubscription[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
