namespace FabioSoft.Nucleus.Plugins.Environment;

/// The catalog this provider announces (for IntelliSense and the placeholder catalog). Keys here must match
/// the keys produced by EnvironmentValues and the system sampler.
public static class EnvironmentDescriptors
{
    public static IReadOnlyList<PlaceholderDescriptor> All { get; } =
    [
        new("cwd.path", "value", "~/Repos/FS/clavis", "Working directory, full path"),
        new("cwd.name", "value", "clavis", "Working directory, leaf folder name"),
        new("cwd.short", "value", "~\\Repos\\FS\\clavis", "Working directory, home-collapsed and shortened"),

        new("git.branch", "value", "feature/context-info", "Current branch"),
        new("git.dirtyStar", "value", "★", "Star when the working tree is dirty, else empty"),
        new("git.changedFiles", "value", "5", "Number of changed files"),
        new("git.addedLines", "value", "3", "Lines added (unstaged + staged)"),
        new("git.removedLines", "value", "2", "Lines removed"),
        new("git.ahead", "value", "1", "Commits ahead of upstream"),
        new("git.behind", "value", "0", "Commits behind upstream"),
        new("git.repo", "value", "clavis", "Origin repository name"),
        new("git.upstream", "value", "origin/main", "Upstream tracking ref"),
        new("git.user", "value", "Fabio von Hertell", "git config user.name"),
        new("git.version", "value", "2.45.1", "Installed git version"),
        new("git.stashCount", "value", "0", "Number of stashes"),

        new("sys.cpu", "value", "23", "CPU usage percent (sampled)"),
        new("sys.ram", "value", "61", "RAM usage percent"),
        new("sys.ramUsed", "value", "19.4 GB", "RAM in use"),
        new("sys.ramTotal", "value", "32 GB", "Total physical RAM"),
        new("sys.disk", "value", "88", "Disk usage percent of the working-directory volume"),
        new("sys.diskFree", "value", "64 GB", "Free space on the working-directory volume"),

        new("clavis.version", "value", "v0.4.1", "Clavis application version"),

        new("time.now", "value", "12:48:31", "Current time (ISO; format with e.g. :HH:mm)"),
        new("time.date", "value", "2026-06-09", "Current date"),
        new("time.time", "value", "12:48", "Current time, HH:mm"),
        new("time.iso", "value", "2026-06-09T12:48:31", "ISO timestamp"),
        new("time.timezone", "value", "W. Europe Standard Time", "Local time zone"),
    ];
}
