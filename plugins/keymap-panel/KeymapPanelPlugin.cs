namespace FabioSoft.Nucleus.Plugins.KeymapPanel;

/// Announces the "keymap" panel kind: a dockable shortcut-management view that lists the current key
/// bindings by scope and lets the user add, change, and remove them. The view talks to the KeyMap plugin
/// over the bus (Set/RemoveKeyBinding) and reads the live KeymapChanged / CommandsAvailable broadcasts.
public sealed class KeymapPanelPlugin : IPlugin<KeymapPanelConfig>
{
    private const double MinPanelWidth = 320;
    private const double MinPanelHeight = 200;

    public string Id => "KeymapPanel";

    public KeymapPanelConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(KeymapPanelConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, KeymapPanelConfig config)
    {
        void Announce() =>
            bus.Send(new PanelKindRegistration(
                "keymap", "Shortcuts", MinPanelWidth, MinPanelHeight, "", true,
                context => KeymapPanelView.Create(bus, context)));

        var subscription = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            Announce();
            return Task.CompletedTask;
        });

        Announce();
        bus.LogInfo(Id, "Keymap panel plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscription));
    }

    private sealed class PluginDisposable(ISubscription subscription) : IDisposable
    {
        public void Dispose() => subscription.Dispose();
    }
}
