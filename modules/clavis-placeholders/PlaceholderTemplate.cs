using System.Text.RegularExpressions;

namespace FabioSoft.Clavis.Placeholders;

/// Parses a template string into segments. The grammar inside `{...}`:
///   head[:tail]   where head is `name` or `name(arg)`.
/// If head's name is a registered component, the token is a component and tail is `value[:format]`;
/// otherwise head is a value key and tail is its format. Splitting is always on the FIRST ':' so that
/// .NET format strings containing colons (HH:mm:ss) survive intact.
public static partial class PlaceholderTemplate
{
    // Runtime Regex rather than [GeneratedRegex]: the in-process build (FabioSoft.Build) does not run C#
    // source generators, so a generated partial method would have no body. Behaviour is identical.
    private static readonly Regex TokenPattern = new(@"\{([^{}]+)\}");

    private static readonly Regex HeadPattern = new(@"^(?<name>[^()]+)\((?<arg>.*)\)$");

    public static IReadOnlyList<TemplateSegment> Parse(string template, IReadOnlySet<string> components)
    {
        var segments = new List<TemplateSegment>();
        var last = 0;

        foreach (Match match in TokenPattern.Matches(template))
        {
            if (match.Index > last)
            {
                segments.Add(new LiteralSegment(template[last..match.Index]));
            }

            segments.Add(ParseToken(match.Value, match.Groups[1].Value, components));
            last = match.Index + match.Length;
        }

        if (last < template.Length)
        {
            segments.Add(new LiteralSegment(template[last..]));
        }

        return segments;
    }

    private static TemplateSegment ParseToken(string raw, string inner, IReadOnlySet<string> components)
    {
        var (head, tail) = SplitFirst(inner, ':');
        var (name, arg) = ParseHead(head);

        if (components.Contains(name))
        {
            if (tail is null)
            {
                return new ComponentSegment(raw, name, arg, null, null);
            }

            var (value, format) = SplitFirst(tail, ':');
            return new ComponentSegment(raw, name, arg, value, format);
        }

        return new ValueSegment(raw, head, tail);
    }

    private static (string Head, string? Tail) SplitFirst(string text, char separator)
    {
        var index = text.IndexOf(separator);

        return index < 0
            ? (text, null)
            : (text[..index], text[(index + 1)..]);
    }

    private static (string Name, string? Arg) ParseHead(string head)
    {
        var match = HeadPattern.Match(head);

        return match.Success
            ? (match.Groups["name"].Value, match.Groups["arg"].Value)
            : (head, null);
    }
}
