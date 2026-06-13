namespace FabioSoft.Nucleus.Plugins.GitLogPanel;

public sealed record CommitRow(string Hash, string Message, string Author, string RelativeTime);

/// Pure parsing of `git log` output. Fields are separated by the ASCII unit separator (0x1F) rather than
/// a printable character so a commit subject containing the separator is impossible in practice - this
/// avoids the pipe-in-subject ambiguity the original panel had.
public static class GitLogParse
{
    public const char FieldSeparator = (char)0x1F;

    public static readonly string Format =
        $"%h{FieldSeparator}%s{FieldSeparator}%an{FieldSeparator}%ar";

    public static IReadOnlyList<CommitRow> Parse(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return [];
        }

        return rawOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split(FieldSeparator))
            .Where(fields => fields.Length >= 4)
            .Select(fields => new CommitRow(fields[0], fields[1], fields[2], fields[3]))
            .ToList();
    }
}
