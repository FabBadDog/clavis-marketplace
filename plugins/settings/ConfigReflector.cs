using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace FabioSoft.Nucleus.Plugins.Settings;

public sealed record ConfigProperty(
    string Name,
    string TypeName,
    string? Description,
    object? DefaultValue);

public static class ConfigReflector
{
    public static IReadOnlyList<ConfigProperty> Reflect(Type configType)
    {
        var properties = new List<ConfigProperty>();

        foreach (var property in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
            {
                continue;
            }

            var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
            object? defaultValue = null;

            try
            {
                var instance = Activator.CreateInstance(configType);
                if (instance is not null)
                {
                    defaultValue = property.GetValue(instance);
                }
            }
            catch
            {
                // config type may not have parameterless constructor
            }

            properties.Add(new ConfigProperty(
                property.Name,
                FormatTypeName(property.PropertyType),
                description,
                defaultValue));
        }

        return properties;
    }

    private static string FormatTypeName(Type type)
    {
        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(TimeSpan))
        {
            return "TimeSpan";
        }

        return type.Name;
    }
}
