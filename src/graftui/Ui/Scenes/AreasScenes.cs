using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>What the three areas hold at one moment: unstaged and staged rows, and the current branch.</summary>
public sealed record AreasModel(IReadOnlyList<FileChange> Unstaged, IReadOnlyList<FileChange> Staged, HeadLine Head);

/// <summary>
/// Options for one drawing of the areas. Hide* keeps rows out of a box while they animate. Line replaces the commit line
/// (default: the head's commits). LabelAt is the slot the current-branch marker sits on. GhostX draws the faded dot of a
/// commit that was just amended. HideLine leaves the commit line out (the message form).
/// </summary>
public sealed record AreasOptions(
    IReadOnlySet<string>? HideUnstaged = null,
    IReadOnlySet<string>? HideStaged = null,
    IReadOnlyList<CommitInfo>? Line = null,
    int? LabelAt = null,
    int? GhostX = null,
    bool HideLine = false);

/// <summary>
/// The three-areas scenes, ported from the design mock (drawAreas, flyTok, sceneDiff and the message form).
/// Drawing only: no git, no timing. The animation (<see cref="ChangesAnimation"/>) decides which frame to draw.
/// </summary>
public static class AreasScenes
{
    private static readonly Style Dim = new(Ink.Dim), Fg = new(Ink.Fg), FgB = new(Ink.Fg, true);
    private static readonly Style Cur = new(Ink.Cur), CurB = new(Ink.Cur, true);
    private static readonly Style Ok = new(Ink.Ok), OkB = new(Ink.Ok, true);
    private static readonly Style Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true);
    private static readonly Style Inc = new(Ink.Inc), IncB = new(Ink.Inc, true);
    private static readonly Style Bad = new(Ink.Bad), BadB = new(Ink.Bad, true);
    private static readonly Style Hash = new(Ink.Hash);

    private const int FlyPathCols = 15;
    private const int FlyBlankCols = 17;

    /// <summary>The three boxes, their files, the repository's branch, and the commit line with its label.</summary>
    public static ArtGrid Static(ArtGeometry g, AreasModel m, AreasOptions? o = null)
    {
        o ??= new AreasOptions();
        var L = AreasLayout.For(g.Width, g.Height);
        var grid = new ArtGrid(g.Width, g.Height);

        DrawBox(grid, L, 0, " Working tree ", WarnB);
        DrawBox(grid, L, 1, " Staging area ", OkB);
        DrawBox(grid, L, 2, " Repository ", CurB);

        grid.Text(L.BoxX(0) + L.BoxW, 2, "─▶", Dim);
        grid.Text(L.BoxX(1) + L.BoxW, 2, "─▶", Dim);

        DrawFiles(grid, L, 0, m.Unstaged.Where(f => !Hidden(o.HideUnstaged, f)).ToList());
        DrawFiles(grid, L, 1, m.Staged.Where(f => !Hidden(o.HideStaged, f)).ToList());
        DrawRepository(grid, L, m.Head);

        if (!o.HideLine)
            DrawLine(grid, L, m.Head, o);

        if (o.GhostX is { } gx)
            grid.Put(gx, L.LineY + 1, '◌', Dim);

        return grid;
    }

    /// <summary>Moves one token of a file from <paramref name="from"/> to <paramref name="to"/>, eased by the caller. Blanks 17 cells first.</summary>
    public static void FlyToken(ArtGrid grid, (int X, int Y) from, (int X, int Y) to, double p, char letter, string path, Style st)
    {
        var x = (int)Math.Round(from.X + (to.X - from.X) * p, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(from.Y + (to.Y - from.Y) * p, MidpointRounding.AwayFromZero);
        grid.Text(x, y, new string(' ', FlyBlankCols), Fg);
        grid.Put(x, y, letter, new Style(st.Ink, true));
        grid.Text(x + 2, y, TextFit.Fit(path, FlyPathCols), FgB);
    }

    /// <summary>The diff of the selected file, or the clean-tree message when there is none.</summary>
    public static SceneFrame Diff(ArtGeometry g, FileDiff? d)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        if (d is null)
        {
            var row = g.Height == 11 ? 4 : 3;
            grid.Text(2, row, "✔ Nothing to commit, working tree clean", OkB);
            grid.Text(2, row + 2, "Tab goes back to Branches.", Dim);
            return new SceneFrame(grid, null);
        }

        var lines = Math.Min(g.Height, d.Lines.Count);
        for (var y = 0; y < lines; y++)
        {
            var line = d.Lines[y];
            grid.Text(1, y, TextFit.Fit(line.Text, g.Width - 2), StyleOf(line.Kind));
        }
        return new SceneFrame(grid, null);
    }

    /// <summary>The message form: the areas at rest, with the labels and counters. The fields are real views drawn over it.</summary>
    /// <param name="subjectCols">Length of the subject, in columns.</param>
    /// <param name="longestBodyCols">Length of the longest body line.</param>
    public static SceneFrame Form(ArtGeometry g, AreasModel m, bool amend, int subjectCols, int longestBodyCols)
    {
        var grid = Static(g, m, new AreasOptions(HideLine: true));
        var L = AreasLayout.For(g.Width, g.Height);

        if (L.FormLabelRow >= 0)
            grid.Text(0, L.FormLabelRow, amend ? "Amend the last commit: new message" : "Commit message", FgB);
        grid.Text(0, L.SubjectRow, "subject  ", Dim);
        grid.Text(0, L.BodyRow, "body     ", Dim);

        var subjectStyle = subjectCols <= 50 ? Dim : subjectCols <= 72 ? Warn : Bad;
        grid.Text(L.Width - 6, L.SubjectRow, $"{subjectCols}/50", subjectStyle);
        var bodyStyle = longestBodyCols <= 72 ? Dim : Warn;
        grid.Text(L.Width - 6, L.BodyRow, $"{longestBodyCols}/72", bodyStyle);
        return new SceneFrame(grid, null);
    }

    /// <summary>The style of a file's letter: staged letters are green; unstaged ones follow the status colours.</summary>
    public static Style LetterStyle(FileChange f) =>
        f.Side == ChangeSide.Staged ? OkB : LetterStyleOf(f.Letter);

    /// <summary>The status colour of a letter in the working-tree list (M T warn, ? info, D U bad, A R C ok).</summary>
    internal static Style LetterStyleOf(char letter) => letter switch
    {
        'M' or 'T' => WarnB,
        '?' => IncB,
        'D' or 'U' => BadB,
        'A' or 'R' or 'C' => OkB,
        _ => FgB,
    };

    private static bool Hidden(IReadOnlySet<string>? set, FileChange f) => set is not null && set.Contains(f.Path);

    private static void DrawBox(ArtGrid grid, AreasLayout L, int i, string title, Style titleStyle)
    {
        var x = L.BoxX(i);
        var w = Math.Max(2, L.BoxWidth(i));
        var bottom = L.BoxBottom;
        grid.Text(x, 0, "┌" + new string('─', w - 2) + "┐", Dim);
        for (var y = 1; y < bottom; y++)
        {
            grid.Put(x, y, '│', Dim);
            grid.Put(x + w - 1, y, '│', Dim);
        }
        grid.Text(x, bottom, "└" + new string('─', w - 2) + "┘", Dim);
        grid.Text(x + 2, 0, TextFit.Fit(title, w - 3), titleStyle);
    }

    // Up to ListRows files; when there are more, the last row says how many are hidden.
    private static void DrawFiles(ArtGrid grid, AreasLayout L, int box, IReadOnlyList<FileChange> files)
    {
        var x = L.BoxX(box);
        var pathCols = L.BoxWidth(box) - 6;
        for (var j = 0; j < files.Count && j < L.ListRows; j++)
        {
            var row = 1 + j;
            if (j == L.ListRows - 1 && files.Count > L.ListRows)
            {
                grid.Text(x + 2, row, $"+{files.Count - (L.ListRows - 1)} more", Dim);
                return;
            }
            var f = files[j];
            grid.Put(x + 2, row, f.Letter, LetterStyle(f));
            grid.Text(x + 4, row, TextFit.Fit(f.Path, pathCols), Fg);
        }
    }

    private static void DrawRepository(ArtGrid grid, AreasLayout L, HeadLine head)
    {
        var x = L.BoxX(2) + 2;
        var cols = L.BoxWidth(2) - 4;
        var name = head.Detached ? "HEAD (detached)" : head.Branch + " (current)";
        grid.Text(x, 1, TextFit.Fit(name, cols), CurB);

        var tip = head.Tip;
        if (tip is null)
            grid.Text(x, 2, "no commits", Dim);
        else
        {
            grid.Text(x, 2, tip.ShortHash, Hash);
            grid.Text(x, 3, TextFit.Fit(tip.Subject, cols), Fg);
        }
    }

    // The commit line: dots joined by rules, the current-branch marker, and its label above the line.
    private static void DrawLine(ArtGrid grid, AreasLayout L, HeadLine head, AreasOptions o)
    {
        var commits = o.Line ?? head.Commits;
        for (var i = 0; i < commits.Count; i++)
        {
            var x = L.SlotX(i);
            if (i > 0)
                grid.HLine(x - (L.Step - 1), x - 1, L.LineY, Cur);
            grid.Put(x, L.LineY, MergeScenes.Dot(commits[i]), Cur);
        }

        var tx = commits.Count == 0 ? L.LineX : L.SlotX(o.LabelAt ?? commits.Count - 1);
        grid.Put(tx, L.LineY - 1, '▾', Cur);

        var name = head.Detached ? "HEAD" : head.Branch;
        var extra = head.Detached ? " (detached)" : " (current)";
        var lx = Math.Max(0, Math.Min(tx, L.Width - RuneCount(name) - 10));
        grid.Text(lx, L.LineY - 2, name, CurB);
        grid.Text(lx + RuneCount(name), L.LineY - 2, extra, Dim);

        if (commits.Count == 0)
            grid.Text(L.LineX, L.LineY, "no commits yet", Dim);
    }

    private static Style StyleOf(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Hunk => new Style(Ink.Cur),
        DiffLineKind.Added => Ok,
        DiffLineKind.Removed => Bad,
        DiffLineKind.Note => Dim,
        _ => Fg,
    };

    private static int RuneCount(string s) => s.EnumerateRunes().Count();
}
