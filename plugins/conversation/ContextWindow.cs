namespace FabioSoft.Nucleus.Plugins.Conversation;

/// The token context window for a model. Replaces the previously hardcoded 200k: the extended-context
/// model variants (tagged "[1m]") carry a one-million-token window. Provider-neutral by shape - it keys off
/// a marker in the model id, not a named provider.
public static class ContextWindow
{
    public const int Default = 200_000;
    public const int Extended = 1_000_000;

    public static int ForModel(string? model) =>
        model is not null && model.Contains("[1m]", StringComparison.OrdinalIgnoreCase)
            ? Extended
            : Default;
}
