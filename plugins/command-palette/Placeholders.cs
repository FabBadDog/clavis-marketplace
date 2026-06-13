using System.Text.RegularExpressions;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Replaces {Token} placeholders in argument values with current values (e.g. {Now}). The token map is
/// injected so the resolution is deterministic in tests. Unknown placeholders are left untouched.
public static partial class Placeholders
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex TokenPattern();

    /// The default placeholder set. Each value is produced on demand at resolution time.
    public static IReadOnlyDictionary<string, Func<string>> Default { get; } =
        new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Now"] = () => DateTimeOffset.Now.ToString("o"),
            ["UtcNow"] = () => DateTimeOffset.UtcNow.ToString("o"),
            ["Today"] = () => DateTime.Today.ToString("yyyy-MM-dd"),
            ["Guid"] = () => Guid.NewGuid().ToString()
        };

    public static string Resolve(string value, IReadOnlyDictionary<string, Func<string>> tokens) =>
        TokenPattern().Replace(value, match =>
            tokens.TryGetValue(match.Groups[1].Value, out var produce) ? produce() : match.Value);
}
