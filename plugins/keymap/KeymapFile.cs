using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.KeyMap;

/// Reads and writes the keymap YAML config (a `bindings:` list of {key, command, scope, panel}). The
/// store is a raw-text passthrough (Configuration plugin), so this owns the (de)serialization. Mirrors
/// the AliasCatalog round-trip in the CommandPalette plugin.
public static class KeymapFile
{
    /// Parse the YAML into bindings, dropping entries without a command or with an unparseable gesture.
    /// May throw on malformed YAML; the caller logs and falls back to the defaults.
    public static IReadOnlyList<KeyBinding> Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return [];
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var document = deserializer.Deserialize<KeymapDocument>(yaml);
        if (document?.Bindings is not { } entries)
        {
            return [];
        }

        var result = new List<KeyBinding>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Command))
            {
                continue;
            }

            var gesture = KeyGesture.TryNormalize(entry.Key);
            if (gesture is null)
            {
                continue;
            }

            result.Add(new KeyBinding(gesture, entry.Command!, Scope(entry.Scope), entry.Panel ?? ""));
        }

        return result;
    }

    public static string Serialize(IReadOnlyList<KeyBinding> bindings)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var document = new KeymapDocument
        {
            Bindings = bindings
                .Select(binding => new KeymapEntry
                {
                    Key = binding.Gesture,
                    Command = binding.Command,
                    Scope = binding.Scope,
                    Panel = string.IsNullOrEmpty(binding.PanelKind) ? null : binding.PanelKind
                })
                .ToList()
        };

        return serializer.Serialize(document);
    }

    /// The starter YAML written when no config exists yet: the full default binding set.
    public static string SerializeStarter() => Serialize(KeymapBindings.Defaults);

    private static string Scope(string? scope) => (scope ?? "").ToLowerInvariant() switch
    {
        KeymapScope.System => KeymapScope.System,
        KeymapScope.Panel => KeymapScope.Panel,
        _ => KeymapScope.Application
    };

    private sealed class KeymapDocument
    {
        public List<KeymapEntry>? Bindings { get; set; }
    }

    private sealed class KeymapEntry
    {
        public string? Key { get; set; }
        public string? Command { get; set; }
        public string? Scope { get; set; }
        public string? Panel { get; set; }
    }
}
