namespace FabioSoft.Nucleus.Plugins.GitLogPanel;

public sealed record GitLogPanelConfig
{
    public int MaxCommits { get; init; } = 10;

    public int RefreshSeconds { get; init; } = 5;
}
