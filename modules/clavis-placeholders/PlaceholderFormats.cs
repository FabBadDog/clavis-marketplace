using System.Globalization;
using System.Text.RegularExpressions;

namespace FabioSoft.Clavis.Placeholders;

/// One discoverable format/transform, for IntelliSense.
public sealed record FormatDescriptor(string Name, string Sample, string Description);

/// Applies a format suffix to a resolved value. Named transforms (uppercase, lowercase, trim, default(x),
/// pad(n)) are tried first; otherwise the format is treated as a .NET format string applied to the value
/// when it parses as a date/time or a number (so `{time.now:HH:mm}` and `{usd:F2}` work even though values
/// travel as strings). Anything unrecognised leaves the value unchanged.
public static partial class PlaceholderFormats
{
    [GeneratedRegex(@"^(?<name>[A-Za-z]+)(?:\((?<arg>.*)\))?$")]
    private static partial Regex NamedPattern();

    public static readonly IReadOnlyList<FormatDescriptor> Known =
    [
        new("uppercase", "CLAUDE", "Upper-case the value"),
        new("lowercase", "claude", "Lower-case the value"),
        new("trim", "claude", "Trim surrounding whitespace"),
        new("default(—)", "—", "Fallback shown when the value is empty"),
        new("pad(8)", "claude  ", "Right-pad to a fixed width"),
        new("HH:mm", "12:48", ".NET format for a date/time value"),
        new("F2", "0.42", ".NET format for a numeric value"),
    ];

    public static string Apply(string raw, string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return raw;
        }

        var (name, arg) = ParseNamed(format);

        switch (name?.ToLowerInvariant())
        {
            case "uppercase":
                return raw.ToUpperInvariant();
            case "lowercase":
                return raw.ToLowerInvariant();
            case "trim":
                return raw.Trim();
            case "default":
                return string.IsNullOrEmpty(raw) ? arg ?? "" : raw;
            case "pad":
                return int.TryParse(arg, out var width) ? raw.PadRight(width) : raw;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return moment.ToString(format, CultureInfo.InvariantCulture);
        }

        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString(format, CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static (string? Name, string? Arg) ParseNamed(string format)
    {
        var match = NamedPattern().Match(format);

        return match.Success
            ? (match.Groups["name"].Value, match.Groups["arg"].Success ? match.Groups["arg"].Value : null)
            : (null, null);
    }
}
