using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Built-in and user-defined aliases. Built-ins migrate the former chat-box commands onto the bus.
/// User aliases are authored in the plugin's YAML config (the `aliases:` map) and override built-ins.
public static class AliasCatalog
{
    public static IReadOnlyDictionary<string, string> BuiltIns { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["exit"] = "ApplicationShutdown",
            ["restart"] = "FullRestartRequested",

            // Logging shortcuts: the single positional argument fills LogEntry.Message; the source is the
            // user and the timestamp resolves to the moment the command runs. Quote a multi-word message.
            ["log-trace"] = "LogEntry Level=Trace Source=user Timestamp={Now}",
            ["log-debug"] = "LogEntry Level=Debug Source=user Timestamp={Now}",
            ["log-info"] = "LogEntry Level=Info Source=user Timestamp={Now}",
            ["log-warn"] = "LogEntry Level=Warn Source=user Timestamp={Now}",
            ["log-error"] = "LogEntry Level=Error Source=user Timestamp={Now}",

            // Window and chrome intents, so they are reachable by name as well as by gesture. The
            // per-panel toggle-<kind> aliases are synthesised live from the registered panel kinds.
            ["close-window"] = "CloseActiveWindow",
            ["open-chat"] = "OpenConversation",
            ["palette"] = "ToggleCommandPalette",
            ["shortcuts"] = "ToggleShortcutHelp"
        };

    /// Built-ins merged with the aliases parsed from the YAML config text. May throw on malformed YAML;
    /// the caller logs and falls back to the built-ins.
    public static IReadOnlyDictionary<string, string> Parse(string? yaml)
    {
        var result = new Dictionary<string, string>(BuiltIns, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return result;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var model = deserializer.Deserialize<AliasFile>(yaml);
        if (model?.Aliases is { } aliases)
        {
            foreach (var (name, template) in aliases)
            {
                result[name] = template;
            }
        }

        return result;
    }

    /// A starter YAML document seeded with the built-in aliases, written when no config exists yet.
    public static string SerializeStarter()
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return serializer.Serialize(new AliasFile
        {
            Aliases = new Dictionary<string, string>(BuiltIns)
        });
    }

    private sealed class AliasFile
    {
        public Dictionary<string, string>? Aliases { get; set; }
    }
}
