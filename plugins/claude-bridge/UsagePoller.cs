using System.Diagnostics.CodeAnalysis;

using FabioSoft.Claude;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

// Polls the account's usage windows on an interval and publishes AgentUsageReport. Network + timer, so
// it is excluded from coverage; the value mapping it depends on (UsageReportMapping) is unit-tested and
// the fetcher is injectable so the plugin's own tests run without touching the network.
[ExcludeFromCodeCoverage]
internal sealed class UsagePoller(IBus bus, Func<Task<UsageWindow[]>> fetch) : IDisposable
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private Timer? _timer;
    private int _running;

    public void Start() => _timer = new Timer(_ => Poll(), null, FirstDelay, Interval);

    public void Dispose() => _timer?.Dispose();

    private async void Poll()
    {
        // A slow fetch must not stack with the next tick.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            var windows = await fetch();
            if (windows.Length > 0)
            {
                bus.Send(UsageReportMapping.ToReport(windows));
            }
        }
        catch (Exception ex)
        {
            bus.LogWarn("ClaudeBridge", $"usage poll failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
