using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

public enum ChangesLineKind { Header, File, Empty, Clean, Stash }

/// <summary>
/// One line of the Changes list. Row is the index into <see cref="WorkingStatus.Rows"/> for file lines, or -1.
/// File is set on file lines only.
/// </summary>
public sealed record ChangesLine(ChangesLineKind Kind, string Text, Style Style, FileChange? File, int Row);

/// <summary>
/// The Changes list, as lines: the staged group, then the unstaged group (mock changesHtml). With a stash that has entries,
/// a last line names the count and the newest entry.
/// </summary>
public static class ChangesList
{
    private static readonly Style OkB = new(Ink.Ok, true), WarnB = new(Ink.Warn, true), Dim = new(Ink.Dim);

    public static IReadOnlyList<ChangesLine> Build(WorkingStatus s, StashSummary? stash = null)
    {
        var stashLine = stash is { Count: > 0 } st
            ? new ChangesLine(ChangesLineKind.Stash, $"Stash ({st.Count}) {st.TopMessage}", Dim, null, -1)
            : null;

        if (s.Rows.Count == 0)
        {
            var clean = new List<ChangesLine> { new(ChangesLineKind.Clean, "✔ working tree clean", new Style(Ink.Ok), null, -1) };
            if (stashLine is not null)
                clean.Add(stashLine);
            return clean;
        }

        var lines = new List<ChangesLine>
        {
            new(ChangesLineKind.Header, $"Staged ({s.Staged.Count})", OkB, null, -1),
        };
        if (s.Staged.Count == 0)
            lines.Add(new ChangesLine(ChangesLineKind.Empty, "  nothing staged", Dim, null, -1));
        for (var i = 0; i < s.Staged.Count; i++)
            lines.Add(FileLine(s.Staged[i], i));

        lines.Add(new ChangesLine(ChangesLineKind.Header, $"Unstaged ({s.Unstaged.Count})", WarnB, null, -1));
        if (s.Unstaged.Count == 0)
            lines.Add(new ChangesLine(ChangesLineKind.Empty, "  nothing unstaged", Dim, null, -1));
        for (var j = 0; j < s.Unstaged.Count; j++)
            lines.Add(FileLine(s.Unstaged[j], s.Staged.Count + j));

        if (stashLine is not null)
            lines.Add(stashLine);
        return lines;
    }

    private static ChangesLine FileLine(FileChange f, int row)
    {
        var text = f.OrigPath is null ? f.Path : $"{f.OrigPath} → {f.Path}";
        return new ChangesLine(ChangesLineKind.File, text, AreasScenes.LetterStyle(f), f, row);
    }
}
