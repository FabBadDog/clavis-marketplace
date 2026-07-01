using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// Reads and writes the markdown-panel definitions YAML (a `panels:` list of {id, title, body}). The
/// Configuration store is a raw-text passthrough, so this owns the (de)serialization. Mirrors KeymapFile.
public static class MarkdownPanelFile
{
    /// Parse the YAML into definitions, dropping entries without an id. May throw on malformed YAML; the
    /// caller logs and falls back to an empty set.
    public static IReadOnlyList<MarkdownDefinition> Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return [];
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var document = deserializer.Deserialize<Document>(yaml);
        if (document?.Panels is not { } entries)
        {
            return [];
        }

        var result = new List<MarkdownDefinition>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                continue;
            }

            result.Add(new MarkdownDefinition(
                entry.Id!, MarkdownCatalog.NormalizeTitle(entry.Title), entry.Body ?? ""));
        }

        return result;
    }

    public static string Serialize(IReadOnlyList<MarkdownDefinition> definitions)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var document = new Document
        {
            Panels = definitions
                .Select(definition => new Entry { Id = definition.Id, Title = definition.Title, Body = definition.Body })
                .ToList()
        };

        return serializer.Serialize(document);
    }

    /// The starter YAML written when no config exists yet: one example panel that demonstrates live
    /// placeholders, so the feature is discoverable on first run.
    public static string SerializeStarter() =>
        Serialize([new MarkdownDefinition(StarterId, "Workspace", StarterBody)]);

    private const string StarterId = "example";

    private const string StarterBody =
        "# {cwd.short}\n\nBranch: **{git.branch}**\n\nModel: {agent.name}\n";

    private sealed class Document
    {
        public List<Entry>? Panels { get; set; }
    }

    private sealed class Entry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
    }
}
