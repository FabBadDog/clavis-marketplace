using System.Collections.Concurrent;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.PanelRegistry;

/// Catalogs the panel kinds that panel plugins announce and routes open/restore requests into ready
/// panel instances for the host to place. Owns no windows and no persistence - it is a pure router. The
/// catalog is an instance field (never a static registry) so the plugin's AssemblyLoadContext can unload.
public sealed class PanelRegistryPlugin : IPlugin<PanelRegistryConfig>
{
    public string Id => "PanelRegistry";

    public PanelRegistryConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(PanelRegistryConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, PanelRegistryConfig config)
    {
        var catalog = new PanelCatalog();
        var openInstances = new ConcurrentDictionary<Guid, string>();

        Action<string> StateCallback(Guid instanceId) =>
            state => bus.Send(new PanelStateChanged(instanceId, state));

        void Open(string kind, Guid instanceId, string savedState)
        {
            if (catalog.TryResolve(kind, instanceId, savedState, StateCallback, out var ready) && ready is not null)
            {
                openInstances[instanceId] = kind;
                bus.Send(ready);
            }
            else
            {
                // The owning plugin has not registered this kind yet (it activates in the background after
                // the restore was requested). Hold the request and replay it when the kind registers, so a
                // restored panel materialises as soon as its plugin is up rather than being dropped.
                catalog.Buffer(kind, instanceId, savedState);
            }
        }

        var registrationSubscription = bus.Subscribe<PanelKindRegistration>(registration =>
        {
            var pending = catalog.Register(registration);
            bus.LogInfo(Id, $"Registered panel kind '{registration.Kind}'");

            // Replay any open/restore requests that arrived before this kind registered.
            foreach (var open in pending)
            {
                Open(registration.Kind, open.InstanceId, open.SavedState);
            }

            return Task.CompletedTask;
        });

        var openSubscription = bus.Subscribe<OpenPanel>(message =>
        {
            Open(message.Kind, Guid.NewGuid(), "");
            return Task.CompletedTask;
        });

        var restoreSubscription = bus.Subscribe<RestorePanel>(message =>
        {
            Open(message.Kind, message.InstanceId, message.SavedState);
            return Task.CompletedTask;
        });

        var closedSubscription = bus.Subscribe<PanelClosed>(message =>
        {
            openInstances.TryRemove(message.InstanceId, out _);
            return Task.CompletedTask;
        });

        // Ask panel plugins to announce their kinds, so activation order does not matter: a registration
        // sent before this subscription existed would otherwise be lost.
        bus.Send(new PanelKindsRequested());

        bus.LogInfo(Id, "Panel registry plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(
            registrationSubscription, openSubscription, restoreSubscription, closedSubscription));
    }

    private sealed class PluginDisposable(params ISubscription[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}
