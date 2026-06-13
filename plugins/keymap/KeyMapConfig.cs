namespace FabioSoft.Nucleus.Plugins.KeyMap;

public sealed record KeyMapConfig
{
    /// The system-scope chord registered as an OS global hotkey. Surfaced as config so it can be tuned
    /// without editing the bindings list (the host reads it from the broadcast bindings, this is the
    /// seed default).
    public string SummonGesture { get; init; } = "Ctrl+Shift+V";
}
