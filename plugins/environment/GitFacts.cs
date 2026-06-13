namespace FabioSoft.Nucleus.Plugins.Environment;

/// Raw git command outputs, assembled by the impure probe and parsed by the pure helpers here.
public sealed record GitRaw(
    string Branch,
    string Porcelain,
    string Numstat,
    string LeftRight,
    string Repo,
    string Upstream,
    string User,
    string Version,
    string StashList,
    bool IsRepository);

/// Pure parsing of git command output into the values behind the `git.*` placeholders.
public static class GitFacts
{
    public static int ChangedFileCount(string porcelain) =>
        SplitLines(porcelain).Count;

    public static string DirtyStar(string porcelain) =>
        ChangedFileCount(porcelain) > 0 ? "★" : "";

    public static (int Added, int Removed) DiffLines(string numstat)
    {
        var added = 0;
        var removed = 0;

        foreach (var line in SplitLines(numstat))
        {
            var columns = line.Split('\t');
            if (columns.Length < 2)
            {
                continue;
            }

            if (int.TryParse(columns[0], out var lineAdded))
            {
                added += lineAdded;
            }

            if (int.TryParse(columns[1], out var lineRemoved))
            {
                removed += lineRemoved;
            }
        }

        return (added, removed);
    }

    /// `git rev-list --left-right --count @{u}...HEAD` prints "<behind>\t<ahead>".
    public static (int Ahead, int Behind) AheadBehind(string leftRight)
    {
        var columns = leftRight.Trim().Split('\t');
        if (columns.Length < 2)
        {
            return (0, 0);
        }

        _ = int.TryParse(columns[0], out var behind);
        _ = int.TryParse(columns[1], out var ahead);

        return (ahead, behind);
    }

    public static int StashCount(string stashList) =>
        SplitLines(stashList).Count;

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
