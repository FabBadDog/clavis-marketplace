using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.Conversation;

/// The configurable placeholder templates for the conversation chrome (status bar zones + title bar), stored
/// in the shared "StatusLine" configuration section so the status-line editor panel can change them. Defaults
/// reproduce the built-in layout; the status bar shows the real context bar + short used/window counts left,
/// the shortened cwd center, the Clavis version and the usage limit-plane right; the title shows the branch
/// (+ dirty star) and the agent cluster. The limit-plane is just a placeholder like any other now, so it is
/// removable/movable; it only renders where {limitPlane} is configured.
public sealed class StatusLineTemplates
{
    public string StatusLeft { get; set; } =
        "ctx {bar:agent.contextPercent} {agent.contextUsedShort}/{agent.contextWindowShort}";
    public string StatusCenter { get; set; } = "{cwd.short}";
    public string StatusRight { get; set; } = "CLAVIS {clavis.version} {limitPlane}";
    public string TitleLeft { get; set; } = "{color(accent):git.branch} {color(yellow):git.dirtyStar}";
    public string AgentCluster { get; set; } = "{agent.modelName} {agent.effortName} {badge:agent.mode}";

    // The per-turn stats column (in front of the timeline rail): one microstat per entry, default runtime +
    // tokens. Resolved against each turn's turn.* values; empty values are skipped (e.g. tokens on the init turn).
    public const string DefaultStatsColumn = "{microstat(clock):turn.runtime} {microstat(tokens):turn.tokens}";
    public string StatsColumn { get; set; } = DefaultStatsColumn;

    // Per-panel chrome overrides for non-chat docked panels, keyed by panel kind. A panel with no entry (or
    // all-empty fields) falls back to its registration default - friendly name in the title, the panel's
    // optional default status template. The chat panel's own chrome stays in the fields above.
    public Dictionary<string, PanelChrome> Panels { get; set; } = new();

    public const string SectionId = "StatusLine";

    private static readonly ISerializer Serializer =
        new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static StatusLineTemplates Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new StatusLineTemplates();
        }

        try
        {
            return Deserializer.Deserialize<StatusLineTemplates>(yaml) ?? new StatusLineTemplates();
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return new StatusLineTemplates();
        }
    }

    public string Serialize() => Serializer.Serialize(this);
}

/// The configurable title-bar and status-bar templates for one non-chat panel kind. Empty fields fall back
/// to the panel's registration defaults during resolution (see PanelChromeResolver).
public sealed class PanelChrome
{
    public string TitleLeft { get; set; } = "";
    public string TitleRight { get; set; } = "";
    public string StatusLeft { get; set; } = "";
    public string StatusCenter { get; set; } = "";
    public string StatusRight { get; set; } = "";
}
