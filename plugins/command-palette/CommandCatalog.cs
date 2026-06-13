namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Projects the palette's command sources (messages, aliases, agent/skill commands) into the shared
/// CommandDescriptor contract, so the keymap, the help overlay, and the shortcut-management panel can
/// list the bindable commands without re-implementing discovery.
public static class CommandCatalog
{
    public static IReadOnlyList<CommandDescriptor> BuildDescriptors(
        IReadOnlyList<Type> catalog,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<CommandItem> externalCommands) =>
        CommandSuggestions.Build("", catalog, aliases, externalCommands)
            .Select(item => new CommandDescriptor(
                item.Name, item.DisplayName, item.Kind.ToString(), item.Source, item.Description, item.IsBindable))
            .ToList();
}
