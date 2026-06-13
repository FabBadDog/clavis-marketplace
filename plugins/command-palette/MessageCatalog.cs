using System.Reflection;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Discovers the bus message types that the palette can construct, by scanning the loaded contract
/// assemblies. A candidate is a public, non-abstract, non-generic type that is an F# record or has a
/// public constructor (so it can be built from string arguments).
public static class MessageCatalog
{
    private static readonly FSharpOption<BindingFlags> NoFlags = FSharpOption<BindingFlags>.None;

    public static IReadOnlyList<Type> Discover() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => IsContractAssembly(assembly.GetName().Name))
            .SelectMany(SafeGetTypes)
            .Where(IsConstructibleMessage)
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// Resolve a typed command name to a message type. Prefers an exact full-name match, then a unique
    /// simple-name match; returns null when nothing matches or a simple name is ambiguous.
    public static Type? Resolve(IReadOnlyList<Type> catalog, string name)
    {
        var byFullName = catalog.FirstOrDefault(
            type => string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (byFullName is not null)
        {
            return byFullName;
        }

        var byName = catalog
            .Where(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    private static bool IsContractAssembly(string? assemblyName) =>
        assemblyName is not null
        && (assemblyName == "FabioSoft.Nucleus.Contracts"
            || assemblyName.StartsWith("FabioSoft.Contracts.", StringComparison.Ordinal));

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static bool IsConstructibleMessage(Type type)
    {
        if (!type.IsPublic || type.IsAbstract || type.IsInterface || type.IsEnum
            || type.IsGenericTypeDefinition || typeof(Attribute).IsAssignableFrom(type))
        {
            return false;
        }

        return FSharpType.IsRecord(type, NoFlags) || type.GetConstructors().Length > 0;
    }
}
