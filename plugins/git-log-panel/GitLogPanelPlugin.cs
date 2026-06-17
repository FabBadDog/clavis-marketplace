using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.GitLogPanel;

/// Announces the "git-log" panel kind to the registry. View construction is deferred into the
/// registration's factory, which the host invokes on its UI thread when a panel is opened.
public sealed class GitLogPanelPlugin : IPlugin<GitLogPanelConfig>
{
    private const double MinPanelWidth = 220;
    private const double MinPanelHeight = 150;

    public string Id => "GitLogPanel";

    public GitLogPanelConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(GitLogPanelConfig config)
    {
        var errors = new List<string>();
        if (config.MaxCommits is < 1 or > 100)
        {
            errors.Add("MaxCommits must be between 1 and 100");
        }

        if (config.RefreshSeconds < 1)
        {
            errors.Add("RefreshSeconds must be at least 1");
        }

        return Task.FromResult<ConfigValidationResult>(
            errors.Count > 0 ? new ConfigInvalid(errors) : new ConfigValid());
    }

    public Task<IDisposable> ActivateAsync(IBus bus, GitLogPanelConfig config)
    {
        void Announce() =>
            bus.Send(new PanelKindRegistration(
                "git-log", "Git Log", MinPanelWidth, MinPanelHeight, "", true,
                context => GitLogPanelView.Create(config, context))
            {
                StatusTemplate = "{color(accent):git.branch}"
            });

        var subscription = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            Announce();
            return Task.CompletedTask;
        });

        // Announce immediately in case the registry is already up, and re-announce on request to cover
        // the case where the registry activates after this plugin.
        Announce();

        bus.LogInfo(Id, "Git log panel plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscription));
    }

    private sealed class PluginDisposable(ISubscription subscription) : IDisposable
    {
        public void Dispose() => subscription.Dispose();
    }
}
