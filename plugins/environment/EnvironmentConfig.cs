namespace FabioSoft.Nucleus.Plugins.Environment;

/// Configuration for the environment placeholder provider.
public sealed class EnvironmentConfig
{
    /// Seconds between value samples (git probe + system metrics), published as a snapshot.
    public int RefreshSeconds { get; init; } = 5;
}
