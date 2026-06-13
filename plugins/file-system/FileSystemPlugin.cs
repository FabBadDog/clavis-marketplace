using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using FabioSoft.Contracts.Resource;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.FileSystem;

public sealed record FileSystemConfig;

internal sealed class FileResource : IResource
{
    public string Uri { get; }

    public FileResource(string uri) => Uri = uri;

    public ValueTask<Stream> OpenAsync(CancellationToken cancellationToken = default)
    {
        var path = new Uri(Uri).LocalPath;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ValueTask.FromResult<Stream>(stream);
    }
}

public sealed class FileSystemPlugin : IPlugin<FileSystemConfig>
{
    public string Id => "FileSystem";

    public FileSystemConfig DefaultConfig => new FileSystemConfig();

    public Task<ConfigValidationResult> ValidateConfigAsync(FileSystemConfig config) => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, FileSystemConfig config)
    {
        bus.Send(new RegisterScheme("file", Id));

        var loadSubscription = bus.Subscribe<LoadSchemeResource>(async message =>
        {
            if (!IsFileScheme(message.Scheme))
            {
                return;
            }

            try
            {
                var path = new Uri(message.Uri).LocalPath;

                if (!File.Exists(path))
                {
                    bus.Send<LoadResourceResult>(new LoadFailed($"File not found: {path}"));
                    bus.LogWarn("FileSystem", $"file not found: {path}");
                    return;
                }

                bus.Send<LoadResourceResult>(new ResourceLoaded(new FileResource(message.Uri)));
            }
            catch (Exception ex)
            {
                bus.Send<LoadResourceResult>(new LoadFailed(ex.Message));
                bus.LogError("FileSystem", $"load failed: {message.Uri}: {ex.Message}");
            }
        });

        var writeSubscription = bus.Subscribe<WriteSchemeResource>(async message =>
        {
            if (!IsFileScheme(message.Scheme))
            {
                return;
            }

            try
            {
                var path = new Uri(message.Uri).LocalPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(path, message.Content);
                bus.Send<WriteResourceResult>(new WriteSucceeded(message.Uri));
            }
            catch (Exception ex)
            {
                bus.Send<WriteResourceResult>(new WriteFailed(ex.Message));
                bus.LogError("FileSystem", $"write failed: {message.Uri}: {ex.Message}");
            }
        });

        bus.LogInfo("FileSystem", "File system scheme handler activated");

        return Task.FromResult<IDisposable>(new CompositeDisposable(loadSubscription, writeSubscription));
    }

    private static bool IsFileScheme(string scheme) =>
        string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase);
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
