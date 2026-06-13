using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Derives a bus-message command's palette text from its type: a human description (the [Description]
/// attribute when present, otherwise the type name split into words) and a short source group taken
/// from the contract namespace - never the raw dotted full name.
public static class MessageDescription
{
    private const string ClavisContractsPrefix = "FabioSoft.Contracts.";
    private const string NucleusContracts = "FabioSoft.Nucleus.Contracts";
    private const string NucleusGroup = "Nucleus";

    public static string Describe(Type type)
    {
        var described = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
        return string.IsNullOrWhiteSpace(described) ? Humanize(type.Name) : described;
    }

    public static string Group(Type type)
    {
        var space = type.Namespace ?? "";
        if (space.StartsWith(ClavisContractsPrefix, StringComparison.Ordinal))
        {
            return space[ClavisContractsPrefix.Length..];
        }

        if (space == NucleusContracts)
        {
            return NucleusGroup;
        }

        var lastDot = space.LastIndexOf('.');
        return lastDot >= 0 ? space[(lastDot + 1)..] : space;
    }

    /// Splits a PascalCase identifier into space-separated words, keeping acronym runs intact
    /// (e.g. "UiRegionContribution" -> "Ui Region Contribution").
    public static string Humanize(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            var startsNewWord = index > 0
                && char.IsUpper(current)
                && (!char.IsUpper(name[index - 1]) || (index + 1 < name.Length && char.IsLower(name[index + 1])));
            if (startsNewWord)
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
