using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using FabioSoft.Contracts.Resource;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Http;

public sealed record HttpConfig;

internal sealed class HttpResource(string uri, HttpClient httpClient) : IResource
{
    public string Uri { get; } = uri;

    public async ValueTask<Stream> OpenAsync(CancellationToken cancellationToken = default) => await httpClient.GetStreamAsync(Uri, cancellationToken);
}

public sealed class HttpPlugin : IPlugin<HttpConfig>
{
    public string Id => "Http";

    public HttpConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(HttpConfig config) => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, HttpConfig config)
    {
        var httpClient = new HttpClient();

        bus.Send(new RegisterScheme("http", Id));
        bus.Send(new RegisterScheme("https", Id));

        var loadSubscription = bus.Subscribe<LoadSchemeResource>(message =>
        {
            if (!IsHttpScheme(message.Scheme))
            {
                return Task.CompletedTask;
            }

            try
            {
                bus.Send<LoadResourceResult>(new ResourceLoaded(new HttpResource(message.Uri, httpClient)));
            }
            catch (Exception ex)
            {
                bus.Send<LoadResourceResult>(new LoadFailed(ex.Message));
                bus.LogError("Http", $"load failed: {message.Uri}: {ex.Message}");
            }

            return Task.CompletedTask;
        });

        var writeSubscription = bus.Subscribe<WriteSchemeResource>(message =>
        {
            if (IsHttpScheme(message.Scheme))
            {
                bus.Send<WriteResourceResult>(new WriteFailed("HTTP scheme does not support writing"));
                bus.LogWarn("Http", $"write unsupported: {message.Uri}");
            }

            return Task.CompletedTask;
        });

        bus.LogInfo("Http", "HTTP scheme handler activated");

        var disposable = new HttpPluginDisposable(httpClient, loadSubscription, writeSubscription);
        return Task.FromResult<IDisposable>(disposable);
    }

    private static bool IsHttpScheme(string scheme)
    {
        var lower = scheme.ToLowerInvariant();
        return lower is "http" or "https";
    }
}

internal sealed class HttpPluginDisposable(HttpClient httpClient, params IDisposable[] subscriptions) : IDisposable
{
    public void Dispose()
    {
        foreach (var subscription in subscriptions)
        {
            try { subscription.Dispose(); }
            catch { /* cleanup best-effort */ }
        }

        httpClient.Dispose();
    }
}
