using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FabioSoft.Contracts.Services;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Configuration;

public sealed class ConfigurationPlugin : IPlugin<ConfigurationConfig>
{
    public string Id => "Configuration";

    public ConfigurationConfig DefaultConfig
    {
        get
        {
            var home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clavis");
            return new ConfigurationConfig(
                Path.Combine(home, "configuration.yaml"),
                Path.Combine(home, "state.yaml"));
        }
    }

    public Task<ConfigValidationResult> ValidateConfigAsync(ConfigurationConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.ConfigurationFilePath))
        {
            errors.Add("ConfigurationFilePath must not be empty");
        }

        if (string.IsNullOrWhiteSpace(config.StateFilePath))
        {
            errors.Add("StateFilePath must not be empty");
        }

        return Task.FromResult<ConfigValidationResult>(
            errors.Count == 0 ? new ConfigValid() : new ConfigInvalid(errors.ToArray()));
    }

    public Task<IDisposable> ActivateAsync(IBus bus, ConfigurationConfig config)
    {
        var configuration = new SectionedYamlFile(config.ConfigurationFilePath);
        var state = new SectionedYamlFile(config.StateFilePath);

        var getConfigSubscription = bus.Subscribe<GetConfig>(request =>
        {
            var raw = configuration.Load(request.PluginId);
            bus.Send<ConfigResult>(raw is not null
                ? new ConfigFound(request.PluginId, raw)
                : (ConfigResult)new ConfigNotFound(request.PluginId));
            return Task.CompletedTask;
        });

        var saveConfigSubscription = bus.Subscribe<SaveConfig>(request =>
        {
            configuration.Save(request.PluginId, request.RawConfig);
            bus.Send(new ConfigSaved(request.PluginId));
            bus.Send(new ConfigChanged(request.PluginId, request.RawConfig));
            return Task.CompletedTask;
        });

        var getStateSubscription = bus.Subscribe<GetState>(request =>
        {
            var raw = state.Load(request.PluginId);
            bus.Send<StateResult>(raw is not null
                ? new StateFound(request.PluginId, raw)
                : (StateResult)new StateNotFound(request.PluginId));
            return Task.CompletedTask;
        });

        var saveStateSubscription = bus.Subscribe<SaveState>(request =>
        {
            state.Save(request.PluginId, request.RawState);
            return Task.CompletedTask;
        });

        bus.Send(new LogEntry(
            LogLevel.Info,
            "Configuration",
            $"Configuration plugin activated; config: {config.ConfigurationFilePath}, state: {config.StateFilePath}",
            DateTimeOffset.UtcNow));

        var disposable = new CompositeDisposable(
            getConfigSubscription, saveConfigSubscription, getStateSubscription, saveStateSubscription);
        return Task.FromResult<IDisposable>(disposable);
    }
}

/// A YAML file whose top level is a mapping of named sections, one per plugin. Reads and writes a single
/// section by name without disturbing the others, so the whole configuration (or all state) lives in one
/// file. Each plugin still owns the YAML inside its section: Load hands back that section's document, Save
/// stores the document the plugin produced. Writes open the file exclusively (with retry) and read-merge-
/// write under that lock, so this plugin and the host (which writes the marketplace/logging sections of the
/// same configuration.yaml) never clobber each other's sections.
internal sealed class SectionedYamlFile
{
    private const int RetryAttempts = 100;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly string _path;

    public SectionedYamlFile(string path) => _path = path;

    public string? Load(string section)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        using var stream = OpenWithRetry(FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);
        return SectionedYaml.ReadSection(reader.ReadToEnd(), section);
    }

    public void Save(string section, string rawYaml)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = OpenWithRetry(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        string existing;
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            existing = reader.ReadToEnd();
        }

        var merged = SectionedYaml.UpsertSection(existing, section, rawYaml);

        stream.SetLength(0);
        stream.Position = 0;
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.Write(merged);
        writer.Flush();
    }

    private FileStream OpenWithRetry(FileMode mode, FileAccess access, FileShare share)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(_path, mode, access, share);
            }
            catch (IOException) when (attempt < RetryAttempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
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
