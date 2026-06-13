namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// What the palette should do with a submitted line. The router is pure: it decides the action; the
/// plugin performs the side effect (publish the message / send the prompt).
public abstract record RouteOutcome;

public sealed record SendBusMessage(object Message) : RouteOutcome;

public sealed record SendAgentPrompt(string Text) : RouteOutcome;

public sealed record RouteError(string Message) : RouteOutcome;

public sealed record NoMatch : RouteOutcome;

/// Classifies a submitted palette line into an action, resolving (in priority order) an alias, a
/// message type, or an agent slash command. Unmatched input yields NoMatch.
public static class CommandRouter
{
    public static RouteOutcome Route(
        string input,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<Type> catalog,
        IReadOnlyList<string> externalCommandNames,
        IReadOnlyDictionary<string, Func<string>> placeholders)
    {
        var parsed = CommandLineParser.Parse(input);
        if (string.IsNullOrEmpty(parsed.Name))
        {
            return new NoMatch();
        }

        if (aliases.TryGetValue(parsed.Name, out var template))
        {
            return RouteAlias(parsed, template, catalog, placeholders);
        }

        var messageType = MessageCatalog.Resolve(catalog, parsed.Name);
        if (messageType is not null)
        {
            return Construct(messageType, parsed.Positional, parsed.Named, placeholders);
        }

        if (externalCommandNames.Any(command => string.Equals(command, parsed.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var text = parsed.ArgumentsText.Length == 0 ? $"/{parsed.Name}" : $"/{parsed.Name} {parsed.ArgumentsText}";
            return new SendAgentPrompt(text);
        }

        return new NoMatch();
    }

    private static RouteOutcome RouteAlias(
        ParsedCommandLine invocation,
        string template,
        IReadOnlyList<Type> catalog,
        IReadOnlyDictionary<string, Func<string>> placeholders)
    {
        var templateParsed = CommandLineParser.Parse(template);
        var type = MessageCatalog.Resolve(catalog, templateParsed.Name);
        if (type is null)
        {
            return new RouteError(
                $"Alias '{invocation.Name}' references unknown message '{templateParsed.Name}'");
        }

        // The invocation's extra arguments extend (positional) and override (named) the template's.
        var positional = templateParsed.Positional.Concat(invocation.Positional).ToList();
        var named = new Dictionary<string, string>(templateParsed.Named, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in invocation.Named)
        {
            named[key] = value;
        }

        return Construct(type, positional, named, placeholders);
    }

    private static RouteOutcome Construct(
        Type type,
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> named,
        IReadOnlyDictionary<string, Func<string>> placeholders)
    {
        var resolvedPositional = positional
            .Select(value => Placeholders.Resolve(value, placeholders))
            .ToList();
        var resolvedNamed = named.ToDictionary(
            pair => pair.Key,
            pair => Placeholders.Resolve(pair.Value, placeholders),
            StringComparer.OrdinalIgnoreCase);

        var outcome = MessageActivator.Activate(type, resolvedPositional, resolvedNamed);
        return outcome.IsSuccess
            ? new SendBusMessage(outcome.Value!)
            : new RouteError(outcome.Error!);
    }
}
