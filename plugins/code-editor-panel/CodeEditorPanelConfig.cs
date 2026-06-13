namespace FabioSoft.Nucleus.Plugins.CodeEditorPanel;

public sealed record CodeEditorPanelConfig
{
    /// Root directory for the file browser. Empty means the directory Clavis was launched in.
    public string RootPath { get; init; } = "";

    public bool ShowHiddenFiles { get; init; } = false;
}
