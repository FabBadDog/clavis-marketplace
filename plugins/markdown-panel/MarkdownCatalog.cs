namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// Pure operations over the set of markdown panel definitions: add, update (title + body), delete, and
/// lookup, plus title normalization. No bus, no I/O - the plugin shell owns persistence and id generation,
/// so every operation takes the id from the caller and returns a new immutable list.
public static class MarkdownCatalog
{
    public const string DefaultTitle = "Untitled";

    public static MarkdownDefinition? Find(IReadOnlyList<MarkdownDefinition> definitions, string id) =>
        definitions.FirstOrDefault(definition => string.Equals(definition.Id, id, StringComparison.Ordinal));

    /// Append a new definition. The title is trimmed and falls back to DefaultTitle when blank; a null body
    /// becomes empty. Order is preserved (append-only).
    public static IReadOnlyList<MarkdownDefinition> Add(
        IReadOnlyList<MarkdownDefinition> definitions, string id, string title, string body) =>
        [.. definitions, new MarkdownDefinition(id, NormalizeTitle(title), body ?? "")];

    /// Replace the title and body of the definition with the given id, leaving the rest untouched. A no-op
    /// (returns an equivalent list) when no definition matches.
    public static IReadOnlyList<MarkdownDefinition> Update(
        IReadOnlyList<MarkdownDefinition> definitions, string id, string title, string body) =>
        [.. definitions.Select(definition =>
            string.Equals(definition.Id, id, StringComparison.Ordinal)
                ? definition with { Title = NormalizeTitle(title), Body = body ?? "" }
                : definition)];

    public static IReadOnlyList<MarkdownDefinition> Delete(
        IReadOnlyList<MarkdownDefinition> definitions, string id) =>
        [.. definitions.Where(definition => !string.Equals(definition.Id, id, StringComparison.Ordinal))];

    public static string NormalizeTitle(string? title)
    {
        var trimmed = (title ?? "").Trim();
        return trimmed.Length == 0 ? DefaultTitle : trimmed;
    }
}
