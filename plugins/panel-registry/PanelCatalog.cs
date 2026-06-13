using System.Collections.Concurrent;

namespace FabioSoft.Nucleus.Plugins.PanelRegistry;

/// The catalog of available panel kinds and the pure resolution from a kind to a ready-to-place panel.
/// No bus or WPF dependency: resolution defers view construction into a Func the host invokes on its UI
/// thread, and the per-instance state callback is supplied by the caller, so this is fully unit-testable.
public sealed class PanelCatalog
{
    private readonly ConcurrentDictionary<string, PanelKindRegistration> _kinds = new();

    public IReadOnlyCollection<string> Kinds => (IReadOnlyCollection<string>)_kinds.Keys;

    public void Register(PanelKindRegistration registration) =>
        _kinds[registration.Kind] = registration;

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
