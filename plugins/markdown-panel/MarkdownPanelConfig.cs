namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

public sealed record MarkdownPanelConfig
{
    public string DefaultTemplate { get; init; } = "# Notes\n";
}
