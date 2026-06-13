using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Settings;

public sealed class SettingsPlugin : IPlugin<SettingsConfig>
{
    public string Id => "Settings";

    public SettingsConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(SettingsConfig config)
        => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, SettingsConfig config)
    {
        var viewModel = new SettingsViewModel();

        var activatedSub = bus.Subscribe<PluginActivated>(msg =>
        {
            return Task.CompletedTask;
        });

        bus.Send(new LogEntry(
            LogLevel.Info,
            "Settings",
            "Settings plugin activated",
            DateTimeOffset.UtcNow));

        return Task.FromResult<IDisposable>(new PluginDisposable(activatedSub));
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
