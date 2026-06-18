namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Builds the filtered suggestion list shown in the palette, aggregating aliases and the external
/// (Claude/Skill) commands. Raw bus messages are deliberately left OUT of the list (they are noise for a
/// human browsing the palette) but stay fully routable: typing a message name - with arguments if needed -
/// still constructs and sends it through CommandRouter, and aliases over messages are unaffected. Filtering
/// is by the typed command name (the first token).
public static class CommandSuggestions
{
    private static readonly IReadOnlyDictionary<string, string> NoArguments =
        new Dictionary<string, string>();

    public static IReadOnlyList<CommandItem> Build(
        string input,
        IReadOnlyList<Type> catalog,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<CommandItem> externalCommands,
        IReadOnlyDictionary<string, string>? shortcuts = null)
    {
        var items = new List<CommandItem>();

        foreach (var (name, template) in aliases)
        {
            items.Add(CommandItem.ForAlias(name, template));
        }

        items.AddRange(externalCommands);

        var filter = CommandLineParser.Parse(input).Name;
        var matches = filter.Length == 0
            ? items
            : items.Where(item =>
                item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));

        return matches
            .OrderByDescending(item => item.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item with
            {
                Shortcut = shortcuts is not null && shortcuts.TryGetValue(item.Name, out var gesture) ? gesture : "",
                IsBindable = IsBindable(item, catalog)
            })
            .ToList();
    }

    /// A command is bindable to a bare gesture when it needs no mandatory arguments: messages must
    /// construct from no arguments, while aliases and agent/skill commands are inherently parameterless.
    public static bool IsBindable(CommandItem item, IReadOnlyList<Type> catalog) =>
        item.Kind != CommandKind.Message
        || (MessageCatalog.Resolve(catalog, item.Name) is { } type
            && MessageActivator.Activate(type, [], NoArguments).IsSuccess);
}
