using System.Diagnostics.CodeAnalysis;
using FabioSoft.Process;

namespace FabioSoft.Nucleus.Plugins.Environment;

[ExcludeFromCodeCoverage] // process spawning
internal static class GitProbe
{
    public static GitRaw Read(string workingDirectory)
    {
        var insideWorkTree = Run(workingDirectory, "rev-parse", "--is-inside-work-tree").Trim();
        if (insideWorkTree != "true")
        {
            return new GitRaw("", "", "", "", "", "", "", "", "", false);
        }

        var branch = Run(workingDirectory, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        var porcelain = Run(workingDirectory, "status", "--porcelain");
        var numstat = Run(workingDirectory, "diff", "--numstat", "HEAD");
        var leftRight = Run(workingDirectory, "rev-list", "--left-right", "--count", "@{u}...HEAD").Trim();
        var repo = RepoName(Run(workingDirectory, "remote", "get-url", "origin").Trim());
        var upstream = Run(workingDirectory, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}").Trim();
        var user = Run(workingDirectory, "config", "user.name").Trim();
        var version = Run(workingDirectory, "--version").Trim().Replace("git version ", "");
        var stash = Run(workingDirectory, "stash", "list");

        return new GitRaw(branch, porcelain, numstat, leftRight, repo, upstream, user, version, stash, true);
    }

    private static string Run(string workingDirectory, params string[] arguments) =>
        BufferedRun.run("git", arguments, workingDirectory).StandardOutput;

    private static string RepoName(string url)
    {
        var trimmed = url.TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var index = trimmed.LastIndexOfAny(['/', ':']);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }
}
