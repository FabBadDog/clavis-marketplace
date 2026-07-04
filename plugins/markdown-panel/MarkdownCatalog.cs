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

    /// True when another definition already has this title once normalized (case-insensitive) - names must
    /// be unique so the manager list and open panel tabs stay unambiguous.
    public static bool IsTitleTaken(IReadOnlyList<MarkdownDefinition> definitions, string id, string title)
    {
        var normalized = NormalizeTitle(title);
        return definitions.Any(definition =>
            !string.Equals(definition.Id, id, StringComparison.Ordinal) &&
            string.Equals(definition.Title, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// A default title guaranteed not to collide with any existing one ("Untitled", then "Untitled 2",
    /// "Untitled 3", ...), so repeated New clicks never produce ambiguous duplicate names.
    public static string NextDefaultTitle(IReadOnlyList<MarkdownDefinition> definitions)
    {
        var titles = new HashSet<string>(definitions.Select(definition => definition.Title), StringComparer.OrdinalIgnoreCase);
        if (!titles.Contains(DefaultTitle))
        {
            return DefaultTitle;
        }

        var suffix = 2;
        while (titles.Contains($"{DefaultTitle} {suffix}"))
        {
            suffix++;
        }

        return $"{DefaultTitle} {suffix}";
    }
}
