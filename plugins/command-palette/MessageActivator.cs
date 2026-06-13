using System.Globalization;
using System.Reflection;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// The result of trying to construct a message instance from string arguments.
public sealed record ActivationOutcome(object? Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static ActivationOutcome Ok(object value) => new(value, null);

    public static ActivationOutcome Fail(string error) => new(null, error);
}

/// Builds an instance of a message type from positional and named string arguments, converting each
/// value to the target member type. Supports F# records (constructed by field, declaration order) and
/// classes (constructed by a matching public constructor). Pure: no bus, no side effects.
public static class MessageActivator
{
    private static readonly FSharpOption<BindingFlags> NoFlags = FSharpOption<BindingFlags>.None;

    public static ActivationOutcome Activate(
        Type type, IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named) =>
            FSharpType.IsRecord(type, NoFlags)
                ? ActivateRecord(type, positional, named)
                : ActivateClass(type, positional, named);

    private static ActivationOutcome ActivateRecord(
        Type type, IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named)
    {
        var fields = FSharpType.GetRecordFields(type, NoFlags);
        var values = new object[fields.Length];
        var assigned = new bool[fields.Length];

        foreach (var (key, raw) in named)
        {
            var index = Array.FindIndex(fields, f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return ActivationOutcome.Fail($"Unknown field '{key}' on {type.Name}");
            }

            var (ok, value, error) = TryConvert(raw, fields[index].PropertyType);
            if (!ok)
            {
                return ActivationOutcome.Fail(error!);
            }

            values[index] = value!;
            assigned[index] = true;
        }

        var nextPositional = 0;
        for (var i = 0; i < fields.Length && nextPositional < positional.Count; i++)
        {
            if (assigned[i])
            {
                continue;
            }

            var (ok, value, error) = TryConvert(positional[nextPositional++], fields[i].PropertyType);
            if (!ok)
            {
                return ActivationOutcome.Fail(error!);
            }

            values[i] = value!;
            assigned[i] = true;
        }

        if (nextPositional < positional.Count)
        {
            return ActivationOutcome.Fail($"Too many arguments for {type.Name}");
        }

        for (var i = 0; i < fields.Length; i++)
        {
            if (!assigned[i])
            {
                return ActivationOutcome.Fail($"Missing value for '{fields[i].Name}' on {type.Name}");
            }
        }

        return ActivationOutcome.Ok(FSharpValue.MakeRecord(type, values, NoFlags));
    }

    private static ActivationOutcome ActivateClass(
        Type type, IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named)
    {
        var constructors = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length);
        string? lastError = null;
        var any = false;

        foreach (var constructor in constructors)
        {
            any = true;
            var bound = TryBind(constructor, positional, named);
            if (bound.IsSuccess)
            {
                return bound;
            }

            lastError = bound.Error;
        }

        if (!any)
        {
            return ActivationOutcome.Fail($"{type.Name} has no public constructor");
        }

        return ActivationOutcome.Fail(lastError ?? $"No constructor of {type.Name} matches the arguments");
    }

    private static ActivationOutcome TryBind(
        ConstructorInfo constructor, IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named)
    {
        var parameters = constructor.GetParameters();
        var values = new object?[parameters.Length];
        var usedPositional = 0;
        var usedNamed = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.Name is { } name && named.TryGetValue(name, out var namedValue))
            {
                var (ok, value, error) = TryConvert(namedValue, parameter.ParameterType);
                if (!ok)
                {
                    return ActivationOutcome.Fail(error!);
                }

                values[i] = value;
                usedNamed++;
            }
            else if (usedPositional < positional.Count)
            {
                var (ok, value, error) = TryConvert(positional[usedPositional++], parameter.ParameterType);
                if (!ok)
                {
                    return ActivationOutcome.Fail(error!);
                }

                values[i] = value;
            }
            else if (parameter.HasDefaultValue)
            {
                values[i] = parameter.DefaultValue;
            }
            else
            {
                return ActivationOutcome.Fail($"Missing value for '{parameter.Name}'");
            }
        }

        if (usedPositional != positional.Count || usedNamed != named.Count)
        {
            return ActivationOutcome.Fail("Arguments do not match the constructor");
        }

        return ActivationOutcome.Ok(constructor.Invoke(values)!);
    }

    private static (bool Ok, object? Value, string? Error) TryConvert(string raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying is not null)
        {
            if (raw.Length == 0)
            {
                return (true, null, null);
            }

            targetType = underlying;
        }

        if (targetType == typeof(string))
        {
            return (true, raw, null);
        }

        try
        {
            if (targetType.IsEnum)
            {
                return (true, Enum.Parse(targetType, raw, ignoreCase: true), null);
            }

            if (targetType == typeof(Guid))
            {
                return (true, Guid.Parse(raw), null);
            }

            if (targetType == typeof(DateTimeOffset))
            {
                return (true, DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), null);
            }

            if (targetType == typeof(DateTime))
            {
                return (true, DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), null);
            }

            if (targetType == typeof(TimeSpan))
            {
                return (true, TimeSpan.Parse(raw, CultureInfo.InvariantCulture), null);
            }

            return (true, Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture), null);
        }
        catch (Exception exception)
        {
            return (false, null, $"Cannot convert '{raw}' to {targetType.Name}: {exception.Message}");
        }
    }
}
