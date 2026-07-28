using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// Which provider instance each Clavis session is holding, and - the point of the type - that no instance is
/// held twice.
///
/// Adoption is exclusive because `--resume` does not join a session, it starts a second process over the same
/// persisted transcript. Two owners means two conversations writing one file, which corrupts it. The claim is
/// taken *before* the process is spawned, so a refused claim costs nothing.
///
/// This guards one Clavis home. Two homes on one machine still share the provider's session store and could
/// each claim the same instance; that needs out-of-band state and is deliberately not solved here.
public sealed class AgentInstanceRegistry
{
    private readonly ConcurrentDictionary<Guid, string> _instanceBySession = new();
    private readonly ConcurrentDictionary<string, Guid> _sessionByInstance = new();

    /// Take exclusive ownership of an instance for a session. False when somebody else already holds it, in
    /// which case the caller must not spawn. Re-claiming the same pair is idempotent, so a session confirming
    /// the id it was already given (from the provider's init event) is not a conflict.
    public bool TryClaim(string instanceId, Guid sessionId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var owner = _sessionByInstance.GetOrAdd(instanceId, sessionId);
        if (owner != sessionId)
        {
            return false;
        }

        _instanceBySession[sessionId] = instanceId;
        return true;
    }

    public string? InstanceOf(Guid sessionId) =>
        _instanceBySession.TryGetValue(sessionId, out var instanceId) ? instanceId : null;

    /// Give up whatever the session was holding, returning it so the caller can decide how to leave it.
    public string? Forget(Guid sessionId)
    {
        if (!_instanceBySession.TryRemove(sessionId, out var instanceId))
        {
            return null;
        }

        _sessionByInstance.TryRemove(instanceId, out _);
        return instanceId;
    }

    public IReadOnlyCollection<string> AdoptedInstanceIds => _sessionByInstance.Keys.ToArray();

    public bool IsAdopted(string instanceId) => _sessionByInstance.ContainsKey(instanceId);
}
