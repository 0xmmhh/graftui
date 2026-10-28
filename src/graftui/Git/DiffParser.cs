using System.Globalization;
using System.Text.RegularExpressions;
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

    // "@@ -a,b +c,d @@ context", or the combined form "@@@ -a -a +c @@@". The old starts are the captures of group 1.
    private static readonly Regex HunkHeader = new(@"^@@+ (?:-(\d+)(?:,\d+)? )+\+(\d+)(?:,\d+)? @@+", RegexOptions.CultureInvariant);

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
                lines.Add(new DiffLine(DiffLineKind.Hunk, HunkLabel(line)));
                continue;
            }

            var kind = raw.Length > 0 && raw[0] == '+' ? DiffLineKind.Added
                : raw.Length > 0 && raw[0] == '-' ? DiffLineKind.Removed
                : DiffLineKind.Context;
            lines.Add(new DiffLine(kind, line));
        }

        return lines.Count > 0 ? lines : [new DiffLine(DiffLineKind.Note, NoChanges)];
    }

    /// <summary>
    /// The plain label for a hunk header: "── line 5 ──", where 5 is the first line of the new file in the hunk (the first line of
    /// the old file when the new side is empty, and 1 when neither is known). A line that is not a hunk header is returned unchanged.
    /// </summary>
    internal static string HunkLabel(string raw)
    {
        var match = HunkHeader.Match(raw);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var line))
            return raw;

        if (line == 0)
        {
            var oldStart = match.Groups[1].Captures.Count > 0
                && int.TryParse(match.Groups[1].Captures[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var old)
                    ? old
                    : 0;
            line = oldStart > 0 ? oldStart : 1;
        }
        return $"── line {line} ──";
    }
}
