using System.Diagnostics.CodeAnalysis;
using FabioSoft.Process;

namespace FabioSoft.Nucleus.Plugins.GitLogPanel;

[ExcludeFromCodeCoverage] // process spawning
internal static class GitProcess
{
    public static string Run(string workingDirectory, int maxCommits)
    {
        var result = BufferedRun.run(
            "git",
            new[] { "log", "--oneline", $"-{maxCommits}", $"--format={GitLogParse.Format}" },
            workingDirectory);

        return result.StandardOutput;
    }
}
