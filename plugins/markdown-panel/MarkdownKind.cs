namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// Maps a definition id to the panel-kind string it is registered under, and back. Each definition is its
/// own panel kind ("markdown:{id}") because the host treats a panel kind as a singleton - a per-definition
/// kind lets every definition dock, restore, and focus independently, and gives each panel its own tab
/// title. The manager is a separate singleton kind.
public static class MarkdownKind
{
    /// Prefix for a definition's display-panel kind. The colon namespaces it and never collides with the
    /// manager kind ("markdown-panels", which starts with "markdown-").
    public const string DisplayPrefix = "markdown:";

    /// The manager panel's own kind - a singleton, user-openable.
    public const string ManagerKind = "markdown-panels";

    public static string ForDefinition(string id) => DisplayPrefix + id;

    /// The definition id embedded in a display-panel kind, or null if the kind is not a markdown display kind.
    public static string? DefinitionId(string? kind) =>
        kind is not null && kind.StartsWith(DisplayPrefix, StringComparison.Ordinal)
            ? kind[DisplayPrefix.Length..]
            : null;
}
