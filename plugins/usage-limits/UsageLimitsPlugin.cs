using System.Windows;
using System.Windows.Threading;

using FabioSoft.Contracts.Session;
using FabioSoft.Contracts.Workspace;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.UsageLimits;

/// Surfaces the agent's usage limits: a glyph in the status bar (click to open detail) and a dockable
/// "usage-limits" panel. Provider-neutral - it reflects whatever limit windows arrive on AgentUsageReport,
/// never naming a provider or assuming a window count.
public sealed class UsageLimitsPlugin : IPlugin<UsageLimitsConfig>
{
    private const string PanelKind = "usage-limits";
    private const double MinPanelWidth = 240;
    private const double MinPanelHeight = 240;

    public string Id => "UsageLimits";

    public UsageLimitsConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(UsageLimitsConfig config)
    {
        if (config.RefreshSeconds < 1)
        {
            return Task.FromResult<ConfigValidationResult>(
                new ConfigInvalid(["RefreshSeconds must be at least 1"]));
        }

        return Task.FromResult<ConfigValidationResult>(new ConfigValid());
    }

    public Task<IDisposable> ActivateAsync(IBus bus, UsageLimitsConfig config)
    {
        var indicator = new UsageIndicator();

        // The pace-plane is no longer pinned into the status bar unconditionally: it renders only where the
        // user places {limitPlane} (the Conversation feeds the live windows to its status/title strips), so
        // here we keep just the dockable detail panel and the refresh cadence that advances its countdown.
        if (Application.Current is not null)
        {
            Application.Current.Dispatcher.Invoke(() => indicator.StartRefreshTimer(config.RefreshSeconds));
        }

        // Re-announce on request so activation order relative to the registry does not matter.
        void Announce() =>
            bus.Send(new PanelKindRegistration(
                PanelKind, "Usage Limits", MinPanelWidth, MinPanelHeight, "", true,
                _ => indicator.CreatePanel()));

        var kindsSubscription = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            Announce();
            return Task.CompletedTask;
        });
        Announce();

        var reportSubscription = bus.Subscribe<AgentUsageReport>(report =>
        {
            indicator.SetReport(report.Windows);
            return Task.CompletedTask;
        });

        bus.LogInfo(Id, "Usage limits plugin activated");

        return Task.FromResult<IDisposable>(
            new PluginDisposable(reportSubscription, kindsSubscription, indicator));
    }

    private sealed class PluginDisposable(
        ISubscription reportSubscription,
        ISubscription kindsSubscription,
        UsageIndicator indicator) : IDisposable
    {
        public void Dispose()
        {
            reportSubscription.Dispose();
            kindsSubscription.Dispose();
            indicator.Dispose();
        }
    }
}
