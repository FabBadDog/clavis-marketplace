using System.Reflection;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// Publishes a runtime-typed message on the bus. IBus.Send is generic and the bus dispatches by the
/// static type parameter, so a boxed Send&lt;object&gt; would dead-letter; we close the generic method
/// over the message's concrete type instead.
public static class BusSender
{
    private static readonly MethodInfo SendDefinition = typeof(IBus)
        .GetMethods()
        .First(method => method.Name == nameof(IBus.Send) && method.GetParameters().Length == 1);

    public static void Send(IBus bus, object message) =>
        SendDefinition.MakeGenericMethod(message.GetType()).Invoke(bus, [message]);
}
