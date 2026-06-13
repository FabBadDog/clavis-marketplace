using FabioSoft.Contracts.Placeholders;

namespace FabioSoft.Clavis.Placeholders;

public sealed record CompletionItem(string Label, string InsertText, string Kind, string Detail);

/// ReplaceStart is the index in the source text from which Items should replace up to the caret.
public sealed record CompletionResult(int ReplaceStart, IReadOnlyList<CompletionItem> Items);

/// Computes IntelliSense suggestions for a placeholder template at a caret position. Pure: it inspects the
/// text around the caret and the aggregated provider descriptors. The UI layer renders the result and
/// applies the chosen InsertText over [ReplaceStart, caret).
public static class PlaceholderCompletion
{
    private static readonly CompletionResult None = new(0, []);

    public static CompletionResult Complete(
        string text,
        int caret,
        IReadOnlyList<PlaceholderDescriptor> descriptors,
        IReadOnlySet<string> components,
        IReadOnlyList<FormatDescriptor> formats)
    {
        var tokenStart = FindOpenToken(text, caret);
        if (tokenStart < 0)
        {
            return None;
        }

        var partialStart = tokenStart + 1;
        var partial = text[partialStart..caret];
        var colon = partial.IndexOf(':');

        if (colon < 0)
        {
            return new CompletionResult(partialStart, HeadCandidates(partial, descriptors, components));
        }

        var head = partial[..colon];
        var afterFirstColon = partialStart + colon + 1;
        var headName = ComponentName(head);

        if (components.Contains(headName))
        {
            var tail = partial[(colon + 1)..];
            var secondColon = tail.IndexOf(':');

            if (secondColon < 0)
            {
                return new CompletionResult(afterFirstColon, ValueCandidates(tail, descriptors));
            }

            var formatPartial = tail[(secondColon + 1)..];
            return new CompletionResult(afterFirstColon + secondColon + 1, FormatCandidates(formatPartial, formats));
        }

        var formatTail = partial[(colon + 1)..];
        return new CompletionResult(afterFirstColon, FormatCandidates(formatTail, formats));
    }

    private static int FindOpenToken(string text, int caret)
    {
        for (var i = caret - 1; i >= 0; i--)
        {
            if (text[i] == '}')
            {
                return -1;
            }

            if (text[i] == '{')
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<CompletionItem> HeadCandidates(
        string partial, IReadOnlyList<PlaceholderDescriptor> descriptors, IReadOnlySet<string> components)
    {
        var items = new List<CompletionItem>();

        if (!partial.Contains('.'))
        {
            foreach (var ns in Namespaces(descriptors))
            {
                if (StartsWith(ns, partial))
                {
                    items.Add(new CompletionItem(ns + ".", ns + ".", "namespace", $"{ns} placeholders"));
                }
            }

            foreach (var component in components)
            {
                if (StartsWith(component, partial))
                {
                    items.Add(new CompletionItem(component, component, "component", "Component"));
                }
            }
        }

        foreach (var descriptor in descriptors)
        {
            if (StartsWith(descriptor.Key, partial))
            {
                items.Add(new CompletionItem(descriptor.Key, descriptor.Key, descriptor.Kind, descriptor.Description));
            }
        }

        return items;
    }

    private static IReadOnlyList<CompletionItem> ValueCandidates(
        string partial, IReadOnlyList<PlaceholderDescriptor> descriptors)
    {
        var items = new List<CompletionItem>();

        foreach (var descriptor in descriptors)
        {
            if (StartsWith(descriptor.Key, partial))
            {
                items.Add(new CompletionItem(descriptor.Key, descriptor.Key, descriptor.Kind, descriptor.Description));
            }
        }

        return items;
    }

    private static IReadOnlyList<CompletionItem> FormatCandidates(
        string partial, IReadOnlyList<FormatDescriptor> formats)
    {
        var items = new List<CompletionItem>();

        foreach (var format in formats)
        {
            if (StartsWith(format.Name, partial))
            {
                items.Add(new CompletionItem(format.Name, format.Name, "format", format.Description));
            }
        }

        return items;
    }

    private static IEnumerable<string> Namespaces(IReadOnlyList<PlaceholderDescriptor> descriptors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            var dot = descriptor.Key.IndexOf('.');
            var ns = dot < 0 ? descriptor.Key : descriptor.Key[..dot];

            if (seen.Add(ns))
            {
                yield return ns;
            }
        }
    }

    private static string ComponentName(string head)
    {
        var paren = head.IndexOf('(');
        return paren < 0 ? head : head[..paren];
    }

    private static bool StartsWith(string candidate, string partial) =>
        candidate.StartsWith(partial, StringComparison.OrdinalIgnoreCase);
}
