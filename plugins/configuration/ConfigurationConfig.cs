namespace FabioSoft.Nucleus.Plugins.Configuration;

/// Paths to the two sectioned YAML stores the plugin owns: the durable per-plugin configuration and the
/// disposable per-plugin runtime state. Both default under ~/.clavis.
public sealed record ConfigurationConfig(string ConfigurationFilePath, string StateFilePath);
