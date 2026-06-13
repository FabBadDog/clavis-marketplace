using System.Collections.Concurrent;

namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// Correlates ask-the-user selection requests with their answers. The ask_user tool registers a pending
/// request before publishing SelectionRequested; the plugin's SelectionCompleted subscription resolves it.
/// Manual correlation (not IBus.Request) because a human answer easily outlives the bus's default request
/// timeout. Pending entries that are never answered are removed by the caller's timeout path.
internal sealed class SelectionBroker
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<SelectionCompleted>> _pending = new();

    public Task<SelectionCompleted> Register(Guid requestId)
    {
        var source = new TaskCompletionSource<SelectionCompleted>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = source;
        return source.Task;
    }

    /// Resolve the matching pending request; an unknown id (timed out, duplicate answer) is ignored.
    public void Complete(SelectionCompleted answer)
    {
        if (_pending.TryRemove(answer.RequestId, out var source))
        {
            source.TrySetResult(answer);
        }
    }

    public void Abandon(Guid requestId) => _pending.TryRemove(requestId, out _);
}
