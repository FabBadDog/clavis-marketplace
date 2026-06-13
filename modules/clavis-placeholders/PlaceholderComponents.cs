namespace FabioSoft.Clavis.Placeholders;

/// The engine's built-in component names. A token whose leading name is one of these is a component
/// (the part after the first ':' is its value); any other leading name is a plain value placeholder.
public static class PlaceholderComponents
{
    public const string Bar = "bar";
    public const string Badge = "badge";
    public const string LimitPlane = "limitPlane";
    public const string Microstat = "microstat";

    /// `color(name):value` renders the value's text in a named theme color (accent, yellow, green, purple,
    /// dim), and renders nothing when the value is empty - so `{color(yellow):git.dirtyStar}` is a yellow
    /// star only when the tree is dirty.
    public const string Color = "color";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Bar, Badge, LimitPlane, Microstat, Color };
}
