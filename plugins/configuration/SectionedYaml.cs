using System;
using System.IO;

using YamlDotNet.RepresentationModel;

namespace FabioSoft.Nucleus.Plugins.Configuration;

/// The pure in-memory splice behind the sectioned configuration/state file: the top level is a YAML mapping
/// of named sections, one per plugin, and these operations read or replace a single section's document
/// without disturbing the others. No file I/O, so they are unit-testable; SectionedYamlFile wraps them with
/// the exclusive-open-with-retry read-merge-write that keeps concurrent writers from clobbering each other.
public static class SectionedYaml
{
    /// The YAML text of one section out of the whole-file text, or null when there is no such section (or the
    /// text is empty / not a top-level mapping).
    public static string? ReadSection(string fileText, string section)
    {
        var root = ReadRoot(fileText);
        if (root is null)
        {
            return null;
        }

        foreach (var entry in root.Children)
        {
            if (entry.Key is YamlScalarNode key && key.Value == section)
            {
                return Serialize(entry.Value);
            }
        }

        return null;
    }

    /// Splices `rawYaml` in as the named section of the whole-file text and returns the new whole-file text.
    /// A present section is replaced in place (keeping its position); a new one is appended.
    public static string UpsertSection(string existingText, string section, string rawYaml)
    {
        var root = ReadRoot(existingText) ?? new YamlMappingNode();
        // YamlScalarNode equality is by value, so the indexer replaces a present section in place (keeping
        // its position) and appends a new one.
        root.Children[new YamlScalarNode(section)] = ParseNode(rawYaml);
        return Serialize(root);
    }

    private static YamlMappingNode? ReadRoot(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        return yaml.Documents.Count == 0 ? null : yaml.Documents[0].RootNode as YamlMappingNode;
    }

    private static YamlNode ParseNode(string rawYaml)
    {
        if (string.IsNullOrWhiteSpace(rawYaml))
        {
            return new YamlMappingNode();
        }

        var yaml = new YamlStream();
        yaml.Load(new StringReader(rawYaml));
        return yaml.Documents.Count == 0 ? new YamlMappingNode() : yaml.Documents[0].RootNode;
    }

    private static string Serialize(YamlNode node)
    {
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(node)).Save(writer, assignAnchors: false);
        return writer.ToString();
    }
}
