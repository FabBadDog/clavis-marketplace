using System.Collections.Concurrent;

namespace FabioSoft.Nucleus.Plugins.PanelRegistry;

/// An open/restore request held for a kind that has not registered yet, replayed once it does.
public sealed record PendingOpen(Guid InstanceId, string SavedState);

/// The catalog of available panel kinds and the pure resolution from a kind to a ready-to-place panel.
/// No bus or WPF dependency: resolution defers view construction into a Func the host invokes on its UI
/// thread, and the per-instance state callback is supplied by the caller, so this is fully unit-testable.
public sealed class PanelCatalog
{
    private readonly ConcurrentDictionary<string, PanelKindRegistration> _kinds = new();
    private readonly Dictionary<string, List<PendingOpen>> _pending = [];
    private readonly object _pendingLock = new();

    public IReadOnlyCollection<string> Kinds => (IReadOnlyCollection<string>)_kinds.Keys;

    /// Register a kind and return any opens buffered for it before it registered, so the caller can replay
    /// them now that the kind resolves. Restore/open is thus order-independent: a request that arrives
    /// before the owning plugin announced its kind is held rather than dropped.
    public IReadOnlyList<PendingOpen> Register(PanelKindRegistration registration)
    {
        _kinds[registration.Kind] = registration;
        lock (_pendingLock)
        {
            return _pending.Remove(registration.Kind, out var buffered) ? buffered : [];
        }
    }

    /// Hold an open/restore for a kind that has not registered yet (the owning plugin activates in the
    /// background after a restore was requested); Register replays it once the kind arrives.
    public void Buffer(string kind, Guid instanceId, string savedState)
    {
        lock (_pendingLock)
        {
            if (!_pending.TryGetValue(kind, out var list))
            {
                list = [];
                _pending[kind] = list;
            }

            list.Add(new PendingOpen(instanceId, savedState));
        }
    }

    /// Resolve a kind into a PanelInstanceReady. View construction is deferred (the returned View Func
    /// builds the element when invoked) and bound to the supplied per-instance context.
    public bool TryResolve(
        string kind,
        Guid instanceId,
        string savedState,
        Func<Guid, Action<string>> stateCallback,
        out PanelInstanceReady? ready)
    {
        if (_kinds.TryGetValue(kind, out var registration))
        {
            var context = new PanelInstanceContext(instanceId, kind, savedState, stateCallback(instanceId));
            var view = new Func<object>(() => registration.ViewFactory.Invoke(context));
            ready = new PanelInstanceReady(
                instanceId, kind, registration.Title, registration.MinWidth, registration.MinHeight, view);
            return true;
        }

        ready = null;
        return false;
    }
}
