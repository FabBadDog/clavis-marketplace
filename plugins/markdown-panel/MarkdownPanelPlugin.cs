using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// Announces the "markdown" panel kind. View construction is deferred into the registration's factory,
/// which the host invokes on its UI thread when a panel is opened or restored.
public sealed class MarkdownPanelPlugin : IPlugin<MarkdownPanelConfig>
{
    private const double MinPanelWidth = 240;
    private const double MinPanelHeight = 160;

    public string Id => "MarkdownPanel";

    public MarkdownPanelConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(MarkdownPanelConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, MarkdownPanelConfig config)
    {
        // Registered but not user-openable: a previously-docked markdown note still restores from a saved
        // layout, but the panel is not offered as something to open (no toggle command, no shortcut) until
        // there is a way to manage markdown note templates. Flip the flag to true once that exists.
        void Announce() =>
            bus.Send(new PanelKindRegistration(
                "markdown", "markdown", MinPanelWidth, MinPanelHeight, "", false,
                context => MarkdownPanelView.Create(config, context)));

        var subscription = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            Announce();
            return Task.CompletedTask;
        });

        Announce();

        bus.LogInfo(Id, "Markdown panel plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscription));
    }

    private sealed class PluginDisposable(ISubscription subscription) : IDisposable
    {
        public void Dispose() => subscription.Dispose();
    }
}
