using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// Tracks which sessions have a turn in flight, so handing an agent back can wait for the work to finish.
///
/// Releasing mid-turn is the ugly case: handing back starts a *new* process over the persisted transcript, so
/// whatever the running turn had not yet written is simply lost, and the resumed agent picks up from a
/// conversation that stops mid-thought. Waiting costs a few seconds; not waiting costs the turn.
public sealed class TurnGate
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _running = new();

    /// A prompt went out. A second prompt while one is already in flight keeps the original completion rather
    /// than replacing it, so an early finish cannot signal idle while the first turn is still going.
    public void Started(Guid sessionId) =>
        _running.TryAdd(sessionId, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// The turn produced its result.
    public void Finished(Guid sessionId)
    {
        if (_running.TryRemove(sessionId, out var completion))
        {
            completion.TrySetResult();
        }
    }

    public bool IsRunning(Guid sessionId) => _running.ContainsKey(sessionId);

    /// Wait for the session to fall idle. True when it is idle (already, or before the timeout); false when the
    /// turn is still running, which leaves the caller to decide whether to hand back anyway.
    public async Task<bool> WaitForIdleAsync(Guid sessionId, TimeSpan timeout)
    {
        if (!_running.TryGetValue(sessionId, out var completion))
        {
            return true;
        }

        return await Task.WhenAny(completion.Task, Task.Delay(timeout)) == completion.Task;
    }
}
