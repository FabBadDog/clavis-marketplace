using System;
using System.Threading.Tasks;

using FabioSoft.Contracts.Resource;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.ResourceBroker;

public sealed record ResourceBrokerConfig;

public sealed class ResourceBrokerPlugin : IPlugin<ResourceBrokerConfig>
{
    public string Id => "ResourceBroker";

    public ResourceBrokerConfig DefaultConfig => new ResourceBrokerConfig();

    public Task<ConfigValidationResult> ValidateConfigAsync(ResourceBrokerConfig config) => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, ResourceBrokerConfig config)
    {
        var router = new SchemeRouter();

        var registerSubscription = bus.Subscribe<RegisterScheme>(async message =>
        {
            router.Register(message.Scheme, message.HandlerPluginId);
            bus.LogDebug("ResourceBroker", $"registered scheme handler: {message.Scheme} -> {message.HandlerPluginId}");
        });

        var loadSubscription = bus.Subscribe<LoadResource>(async request =>
        {
            var outcome = router.Resolve(request.Uri);
            switch (outcome.Kind)
            {
                case RouteKind.Handled:
                    bus.Send(new LoadSchemeResource(outcome.Scheme!, request.Uri));
                    break;
                case RouteKind.Unknown:
                    bus.Send<LoadResourceResult>(new UnknownScheme(outcome.Scheme!));
                    bus.LogWarn("ResourceBroker", $"no handler for scheme: {outcome.Scheme} ({request.Uri})");
                    break;
                case RouteKind.Invalid:
                    bus.Send<LoadResourceResult>(new LoadFailed($"Invalid URI: {outcome.Message}"));
                    bus.LogWarn("ResourceBroker", $"invalid load URI: {request.Uri}: {outcome.Message}");
                    break;
            }
        });

        var writeSubscription = bus.Subscribe<WriteResource>(async request =>
        {
            var outcome = router.Resolve(request.Uri);
            switch (outcome.Kind)
            {
                case RouteKind.Handled:
                    bus.Send(new WriteSchemeResource(outcome.Scheme!, request.Uri, request.Content));
                    break;
                case RouteKind.Unknown:
                    bus.Send<WriteResourceResult>(new WriteUnknownScheme(outcome.Scheme!));
                    bus.LogWarn("ResourceBroker", $"no handler for scheme: {outcome.Scheme} ({request.Uri})");
                    break;
                case RouteKind.Invalid:
                    bus.Send<WriteResourceResult>(new WriteFailed($"Invalid URI: {outcome.Message}"));
                    bus.LogWarn("ResourceBroker", $"invalid write URI: {request.Uri}: {outcome.Message}");
                    break;
            }
        });

        bus.LogInfo("ResourceBroker", "Resource broker activated");

        var disposable = new CompositeDisposable(registerSubscription, loadSubscription, writeSubscription);
        return Task.FromResult<IDisposable>(disposable);
    }
}

internal sealed class CompositeDisposable : IDisposable
{
    private readonly IDisposable[] _disposables;

    public CompositeDisposable(params IDisposable[] disposables) => _disposables = disposables;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); }
            catch { /* cleanup best-effort */ }
        }
    }
}
