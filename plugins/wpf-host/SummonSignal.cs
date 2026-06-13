using System.Diagnostics.CodeAnalysis;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Bridges the host's single-instance guard to the summon path. A second Clavis launch for the same
/// Clavis home signals a named event (advertised by the host through the ClavisActivationEvent
/// environment variable) and exits; this listener waits on that event and invokes the signaled callback,
/// which routes into SummonClavis - always bring-to-foreground, never the hide half of ToggleClavis.
/// The event is auto-reset, so a launch that happens while plugins are still compiling stays pending and
/// summons the window the moment this listener attaches. Inert when the variable is absent (a host
/// without the guard).
[ExcludeFromCodeCoverage] // named-handle interop + background wait loop
internal sealed class SummonSignal : IDisposable
{
    private const string ActivationEventVariable = "ClavisActivationEvent";
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly EventWaitHandle? _activationEvent;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Thread? _listener;

    public SummonSignal(Action signaled)
    {
        var eventName = Environment.GetEnvironmentVariable(ActivationEventVariable);
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        _listener = new Thread(() => Listen(_activationEvent, _stopping.Token, signaled))
        {
            IsBackground = true,
            Name = "ClavisSummonSignal",
        };
        _listener.Start();
    }

    private static void Listen(EventWaitHandle activationEvent, CancellationToken stopping, Action signaled)
    {
        var handles = new WaitHandle[] { activationEvent, stopping.WaitHandle };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            signaled();
        }
    }

    // The listener thread must end before the handles are disposed - and before the plugin's collectible
    // load context can unload - so disposal joins it rather than abandoning it.
    public void Dispose()
    {
        _stopping.Cancel();
        _listener?.Join(StopTimeout);
        _activationEvent?.Dispose();
        _stopping.Dispose();
    }
}
