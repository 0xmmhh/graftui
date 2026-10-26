using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Turns `git diff` output into lines for the Changes view. Pure: it runs no git.
/// Header lines are dropped, the metadata lines that matter (renames, mode changes, binary files, missing newline) become notes,
/// and hunk lines keep their +, - or space. A "--- " or "+++ " line inside a hunk is content (a removed line that starts with "-- "),
/// so those two prefixes only count as headers before the first hunk.
/// </summary>
internal static class DiffParser
{
    public const int DefaultMaxLines = 200;

    private const string NoChanges = "(no changes to show)";
    private const int TabWidth = 4;

    private static readonly string[] NoteStarts =
    [
        "new file mode", "deleted file mode", "old mode", "new mode", "similarity index",
        "rename from", "rename to", "Binary files", "\\ No newline",
    ];

    public static IReadOnlyList<DiffLine> Parse(string output, int maxLines = DefaultMaxLines)
    {
        var text = output.TrimEnd('\n');
        var lines = new List<DiffLine>();
        if (text.Length == 0)
            return [new DiffLine(DiffLineKind.Note, NoChanges)];

        var inHunk = false;
        foreach (var raw in text.Split('\n'))
        {
            if (lines.Count >= maxLines)
                break;

            if (raw.StartsWith("diff ", StringComparison.Ordinal))
            {
                inHunk = false; // diff --git, diff --cc, diff --combined
                continue;
            }
            if (raw.StartsWith("index ", StringComparison.Ordinal))
                continue;
            if (!inHunk && (raw.StartsWith("--- ", StringComparison.Ordinal) || raw.StartsWith("+++ ", StringComparison.Ordinal)))
                continue;

            var line = raw.Replace("\t", new string(' ', TabWidth));
            if (NoteStarts.Any(p => raw.StartsWith(p, StringComparison.Ordinal)))
            {
                lines.Add(new DiffLine(DiffLineKind.Note, line));
                continue;
            }
            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
                lines.Add(new DiffLine(DiffLineKind.Hunk, line));
                continue;
            }

            var kind = raw.Length > 0 && raw[0] == '+' ? DiffLineKind.Added
                : raw.Length > 0 && raw[0] == '-' ? DiffLineKind.Removed
                : DiffLineKind.Context;
            lines.Add(new DiffLine(kind, line));
        }

        return lines.Count > 0 ? lines : [new DiffLine(DiffLineKind.Note, NoChanges)];
    }
}
