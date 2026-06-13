namespace FabioSoft.Nucleus.Plugins.Environment;

/// Pure working-directory display formatting for the `cwd.short` placeholder: collapse the home prefix to
/// `~`, and if the path has more than four segments cut it at the start to `...\last\four\segments\kept`.
public static class PathFormatting
{
    private const int MaxSegments = 4;

    public static string ShortPath(string path, string home, char separator = '\\')
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var display = CollapseHome(path, home, separator);
        var segments = display.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length <= MaxSegments
            ? display
            : "..." + separator + string.Join(separator, segments[^MaxSegments..]);
    }

    public static string LeafName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);

        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    private static string CollapseHome(string path, string home, char separator)
    {
        if (string.IsNullOrEmpty(home) || !path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var rest = path[home.Length..].TrimStart('\\', '/');

        return rest.Length == 0 ? "~" : "~" + separator + rest;
    }
}
