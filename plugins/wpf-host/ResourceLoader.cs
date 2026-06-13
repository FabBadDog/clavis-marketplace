using System.Windows;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

internal static class ResourceLoader
{
    private static readonly string AssemblyName = typeof(ResourceLoader).Assembly.GetName().Name!;

    public static T Load<T>(string path)
    {
        var uri = new Uri($"/{AssemblyName};component/{path}", UriKind.Relative);
        return (T)Application.LoadComponent(uri);
    }
}
