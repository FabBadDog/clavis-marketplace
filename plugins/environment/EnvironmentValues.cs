using System.Globalization;

namespace FabioSoft.Nucleus.Plugins.Environment;

/// Pure assembly of the `cwd.*`, `git.*`, `clavis.*` and `time.*` placeholder values from raw inputs. The
/// `sys.*` values are sampled impurely and merged by the plugin; everything here is deterministic and tested.
public static class EnvironmentValues
{
    public static IReadOnlyDictionary<string, string> Build(
        string cwd, string home, GitRaw git, DateTimeOffset now, string clavisVersion)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cwd.path"] = cwd,
            ["cwd.name"] = PathFormatting.LeafName(cwd),
            ["cwd.short"] = PathFormatting.ShortPath(cwd, home),
            ["clavis.version"] = clavisVersion,
            ["time.now"] = now.ToString("o", CultureInfo.InvariantCulture),
            ["time.iso"] = now.ToString("o", CultureInfo.InvariantCulture),
            ["time.date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time.time"] = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["time.timezone"] = TimeZoneInfo.Local.StandardName,
        };

        // git.* keys are always present (the provider registers them), so they resolve to "" outside a work
        // tree rather than rendering the token verbatim - templates can then rely on them being defined.
        var (added, removed) = git.IsRepository ? GitFacts.DiffLines(git.Numstat) : (0, 0);
        var (ahead, behind) = git.IsRepository ? GitFacts.AheadBehind(git.LeftRight) : (0, 0);

        values["git.branch"] = git.Branch;
        values["git.dirtyStar"] = git.IsRepository ? GitFacts.DirtyStar(git.Porcelain) : "";
        values["git.changedFiles"] = git.IsRepository ? Number(GitFacts.ChangedFileCount(git.Porcelain)) : "";
        values["git.addedLines"] = git.IsRepository ? Number(added) : "";
        values["git.removedLines"] = git.IsRepository ? Number(removed) : "";
        values["git.ahead"] = git.IsRepository ? Number(ahead) : "";
        values["git.behind"] = git.IsRepository ? Number(behind) : "";
        values["git.repo"] = git.Repo;
        values["git.upstream"] = git.Upstream;
        values["git.user"] = git.User;
        values["git.version"] = git.Version;
        values["git.stashCount"] = git.IsRepository ? Number(GitFacts.StashCount(git.StashList)) : "";

        return values;
    }

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
