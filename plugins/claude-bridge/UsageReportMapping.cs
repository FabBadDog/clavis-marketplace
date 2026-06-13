using FabioSoft.Claude;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

// Translates the Claude usage API's windows into the provider-neutral AgentUsageReport. Utilization is a
// 0..100 percentage, so it maps directly to a used-of-100 budget with a "%" unit; the UI never sees the
// provider's metric. Pure - the poller in the plugin owns the fetch and bus publish.
public static class UsageReportMapping
{
    public static AgentUsageReport ToReport(IReadOnlyList<UsageWindow> windows)
    {
        var mapped = windows
            .Select(window => new AgentLimitWindow(
                window.Name, window.Utilization, 100.0, "%", window.WindowStart, window.ResetsAt))
            .ToArray();

        return new AgentUsageReport(mapped);
    }
}
