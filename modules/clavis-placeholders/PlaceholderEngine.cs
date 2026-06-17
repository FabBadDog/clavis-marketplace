using System.Globalization;
using System.Text;

namespace FabioSoft.Clavis.Placeholders;

/// Parses and resolves placeholder templates. Pure: it holds only the set of known component names and
/// takes the value snapshot as an argument, so it is fully unit-testable without a bus or WPF. The WPF
/// layer turns the ResolvedComponent segments into controls; everything else is ready text.
public sealed class PlaceholderEngine
{
    private readonly IReadOnlySet<string> _components;

    public PlaceholderEngine()
        : this(PlaceholderComponents.All)
    {
    }

    public PlaceholderEngine(IReadOnlySet<string> components)
    {
        _components = components;
    }

    public IReadOnlyList<TemplateSegment> Parse(string template) =>
        PlaceholderTemplate.Parse(template, _components);

    public IReadOnlyList<ResolvedSegment> Resolve(string template, IReadOnlyDictionary<string, string> values) =>
        Resolve(Parse(template), values);

    public IReadOnlyList<ResolvedSegment> Resolve(
        IReadOnlyList<TemplateSegment> segments, IReadOnlyDictionary<string, string> values)
    {
        var resolved = new List<ResolvedSegment>(segments.Count);

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case LiteralSegment literal:
                    resolved.Add(new ResolvedText(literal.Text));
                    break;

                case ValueSegment value:
                    resolved.Add(values.TryGetValue(value.Key, out var raw)
                        ? new ResolvedText(PlaceholderFormats.Apply(raw, value.Format), IsValue: true, Key: value.Key)
                        : new ResolvedText(value.Raw, Unresolved: true, IsValue: true, Key: value.Key));
                    break;

                case ComponentSegment component:
                    var text = ResolveValue(component.ValueKey, component.ValueFormat, values);
                    var number = double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                        ? n
                        : (double?)null;
                    var unresolved = component.ValueKey is not null && !values.ContainsKey(component.ValueKey);
                    resolved.Add(new ResolvedComponent(component.Component, component.Arg, text, number, unresolved));
                    break;
            }
        }

        return resolved;
    }

    /// Resolves a template to plain text, with components falling back to their value text (bar/limitPlane
    /// have no text and contribute nothing). Used in tests and non-WPF contexts.
    public string ResolveToText(string template, IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();

        foreach (var segment in Resolve(template, values))
        {
            switch (segment)
            {
                case ResolvedText text:
                    builder.Append(text.Text);
                    break;

                case ResolvedComponent component:
                    builder.Append(component.Value);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string ResolveValue(
        string? key, string? format, IReadOnlyDictionary<string, string> values)
    {
        if (key is null)
        {
            return "";
        }

        return values.TryGetValue(key, out var raw)
            ? PlaceholderFormats.Apply(raw, format)
            : "";
    }
}
