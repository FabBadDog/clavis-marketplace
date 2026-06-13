using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

namespace FabioSoft.Nucleus.Plugins.Environment;

/// Publishes the `cwd.*`, `git.*`, `sys.*`, `clavis.*` and `time.*` placeholders. Announces its descriptors
/// (for IntelliSense/catalog) and, on a timer, samples values and broadcasts a PlaceholderSnapshot.
public sealed class EnvironmentPlugin : IPlugin<EnvironmentConfig>
{
    public string Id => "Environment";

    public EnvironmentConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(EnvironmentConfig config)
    {
        var errors = new List<string>();
        if (config.RefreshSeconds < 1)
        {
            errors.Add("RefreshSeconds must be at least 1");
        }

        return Task.FromResult<ConfigValidationResult>(
            errors.Count > 0 ? new ConfigInvalid(errors) : new ConfigValid());
    }

    [ExcludeFromCodeCoverage] // impure: bus wiring, timer, sampling
    public Task<IDisposable> ActivateAsync(IBus bus, EnvironmentConfig config)
    {
        var sampler = new SystemSample();
        var version = ClavisVersion();
        var busy = new int[1];

        void Announce() => bus.Send(new RegisterPlaceholderProvider(Id, EnvironmentDescriptors.All));

        void Publish()
        {
            if (Interlocked.CompareExchange(ref busy[0], 1, 0) != 0)
            {
                return;
            }

            try
            {
                var workingDirectory = Directory.GetCurrentDirectory();
                var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                var git = GitProbe.Read(workingDirectory);

                var values = new Dictionary<string, string>(
                    EnvironmentValues.Build(workingDirectory, home, git, DateTimeOffset.Now, version),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var pair in sampler.Read(workingDirectory))
                {
                    values[pair.Key] = pair.Value;
                }

                bus.Send(new PlaceholderSnapshot(Id, values));
            }
            catch (Exception ex)
            {
                bus.LogWarn(Id, $"Environment sample failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref busy[0], 0);
            }
        }

        var subscription = bus.Subscribe<PlaceholdersRequested>(_ =>
        {
            Announce();
            Publish();
            return Task.CompletedTask;
        });

        var timer = new Timer(_ => Publish(), null, TimeSpan.Zero, TimeSpan.FromSeconds(config.RefreshSeconds));

        Announce();
        bus.LogInfo(Id, "Environment placeholder provider activated");

        return Task.FromResult<IDisposable>(new Disposer(subscription, timer));
    }

    private static string ClavisVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    [ExcludeFromCodeCoverage]
    private sealed class Disposer(ISubscription subscription, Timer timer) : IDisposable
    {
        public void Dispose()
        {
            subscription.Dispose();
            timer.Dispose();
        }
    }
}
