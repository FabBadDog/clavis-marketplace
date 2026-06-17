using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.CodeEditorPanel;

/// Announces the "code-editor" panel kind: a file browser paired with the shared CodeEditor. View
/// construction is deferred into the registration's factory, which the host invokes on its UI thread.
public sealed class CodeEditorPanelPlugin : IPlugin<CodeEditorPanelConfig>
{
    private const double MinPanelWidth = 360;
    private const double MinPanelHeight = 240;

    public string Id => "CodeEditorPanel";

    public CodeEditorPanelConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(CodeEditorPanelConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, CodeEditorPanelConfig config)
    {
        void Announce() =>
            bus.Send(new PanelKindRegistration(
                "code-editor", "Code Editor", MinPanelWidth, MinPanelHeight, "", true,
                context => CodeEditorPanelView.Create(config, bus, context)));

        var subscription = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            Announce();
            return Task.CompletedTask;
        });

        Announce();

        bus.LogInfo(Id, "Code editor panel plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscription));
    }

    private sealed class PluginDisposable(ISubscription subscription) : IDisposable
    {
        public void Dispose() => subscription.Dispose();
    }
}
