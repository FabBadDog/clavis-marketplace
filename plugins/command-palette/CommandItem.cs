using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// The source/category of a palette command, shown as a badge in the suggestion list.
public enum CommandKind
{
    Message,
    Alias,
    Agent,
    Skill
}

/// One row in the palette's filtered suggestion list. Name is the full, routable command string - the
/// plugin prefix is kept so routing and tab-completion resolve correctly. DisplayName is the short label
/// shown in the Name column, and Source names where the command comes from (its plugin, the contract
/// group, or its origin) and is shown in its own column.
public sealed record CommandItem(
    string Name,
    CommandKind Kind,
    string Description,
    string Source,
    string DisplayName)
{
    public string KindLabel => Kind.ToString().ToUpperInvariant();

    // The kind badge for the shared BadgeTemplate: the kind word plus its palette resource key.
    public BadgeViewModel KindBadge => new(KindLabel, Kind switch
    {
        CommandKind.Message => "ClavisBrush",
        CommandKind.Alias => "HumanBrush",
        CommandKind.Agent => "InputBrush",
        CommandKind.Skill => "WarnBrush",
        _ => "SecondaryBrush"
    });

    /// The gesture currently bound to this command (empty when none). Set when suggestions are built from
    /// the live keymap snapshot, so the palette can show the shortcut next to each command.
    public string Shortcut { get; init; } = "";

    /// True when the command takes no mandatory arguments, so a bare gesture can invoke it. Set during
    /// suggestion building; gates the Alt+Enter assign affordance.
    public bool IsBindable { get; init; } = true;

    public static CommandItem ForAlias(string name, string template) =>
        new(name, CommandKind.Alias, template, "alias", name);

    public static CommandItem ForMessage(Type type) =>
        new(type.Name, CommandKind.Message, MessageDescription.Describe(type), MessageDescription.Group(type), type.Name);

    /// Classifies a command reported by the agent handshake. The provider marks personal skills with a
    /// trailing "(user)" and prefixes a plugin command's description with "(plugin-id)". Both the plugin
    /// id (from that prefix, else a "plugin:command" name prefix) and the bare command name are surfaced
    /// in their own columns, and the plugin id is stripped from the description so it shows only once.
    public static CommandItem FromAgentCommand(string name, string description)
    {
        const string userMarker = "(user)";
        const string builtInSource = "built-in";
        const string userSource = "user";

        var text = (description ?? "").Trim();

        var isSkill = text.EndsWith(userMarker, StringComparison.Ordinal);
        if (isSkill)
        {
            text = text[..^userMarker.Length].TrimEnd();
        }

        var pluginFromDescription = LeadingParenToken(text);
        if (pluginFromDescription is not null)
        {
            text = text[(pluginFromDescription.Length + 2)..].TrimStart();
        }

        var kind = isSkill ? CommandKind.Skill : CommandKind.Agent;
        var colon = name.IndexOf(':');
        var displayName = colon > 0 ? name[(colon + 1)..] : name;
        var sourceFromName = colon > 0 ? name[..colon] : null;
        var source = pluginFromDescription ?? sourceFromName ?? (isSkill ? userSource : builtInSource);

        return new CommandItem(name, kind, text, source, displayName);
    }

    /// The plugin id in a leading "(plugin-id) " prefix, or null. Plugin ids carry no spaces, so a
    /// parenthetical containing a space (a real sentence like "(see the docs)") is left untouched.
    private static string? LeadingParenToken(string text)
    {
        if (text.Length < 3 || text[0] != '(')
        {
            return null;
        }

        var close = text.IndexOf(')');
        if (close <= 1 || (close + 1 < text.Length && text[close + 1] != ' '))
        {
            return null;
        }

        var token = text[1..close];
        return token.Contains(' ') ? null : token;
    }
}
