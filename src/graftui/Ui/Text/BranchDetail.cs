using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// What the detail block under the branch list shows for the selected branch: its name, its cached preview layout (null while it loads),
/// the files its new commits change (null until they are read, or when the read failed), and the state of that read.
/// </summary>
public sealed record BranchDetailInput(string Name, Ink NameInk, bool IsCurrent, bool Detached, GraphLayout? Layout,
    IncomingFiles? Files, IncomingFilesState FilesState);

/// <summary>One row of the block. Left starts at the block's first column; Right ends on its last column (empty when there is none).</summary>
public sealed record DetailLine(IReadOnlyList<Seg> Left, IReadOnlyList<Seg> Right);

/// <summary>
/// The detail block under the branch list (the new look): the selected branch's name, what its new commits would bring in, and the files
/// they change with their +/- counts. Pure: the screen draws the lines, the tab reads the layout and the files.
/// </summary>
public static class BranchDetail
{
    /// <summary>The rows the block needs at least: the separator, the name and the summary.</summary>
    public const int DetailMin = 3;

    /// <summary>The rows the block takes when the list has room: the separator, the name, the summary, a blank row and four files.</summary>
    public const int DetailComfort = 8;

    /// <summary>The fewest list rows kept when the block is made to fit: the list scrolls to make room.</summary>
    public const int MinListRows = 6;

    /// <summary>The narrowest block worth drawing.</summary>
    public const int MinWidth = 10;

    // The lines under the separator: 0 the name, 1 the summary, 2 blank, and from line 3 the files.
    private const int FirstFileLine = 3;

    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style Fg = new(Ink.Fg);
    private static readonly Style Warn = new(Ink.Warn);
    private static readonly Style Ok = new(Ink.Ok);
    private static readonly Style Bad = new(Ink.Bad);
    private static readonly Style HashInk = new(Ink.Hash);

    /// <summary>
    /// Splits the list's rows between the branch list and the block (the block's first row is its blank separator, included in the second
    /// number). The list keeps every branch when the block can have its comfortable size; else the list scrolls to MinListRows and the block
    /// gets DetailComfort; else the block is as big as fits; with too little room there is no block.
    /// </summary>
    public static (int ListRows, int DetailRows) Split(int listH, int count)
    {
        if (listH - count >= DetailComfort)
            return (count, listH - count);
        if (listH - DetailComfort >= MinListRows)
            return (listH - DetailComfort, DetailComfort);
        if (listH - count >= DetailMin)
            return (count, listH - count);
        return (listH, 0);
    }

    /// <summary>
    /// The block's lines under its separator: at most <paramref name="rows"/> lines, each fitted to <paramref name="width"/> columns. Empty when
    /// either is 0 or less. Never throws.
    /// </summary>
    public static IReadOnlyList<DetailLine> Lines(BranchDetailInput d, int width, int rows)
    {
        var lines = new List<DetailLine>();
        if (width <= 0 || rows <= 0)
            return lines;

        if (d.IsCurrent)
            AddCurrent(d, width, lines);
        else if (d.Detached)
        {
            lines.Add(Plain(d.Name, new Style(d.NameInk), width));
            lines.Add(Plain("HEAD is detached", Warn, width));
        }
        else
            AddIncoming(d, width, lines, rows);

        // The block is cut at its bottom edge, so it never takes a row it was not given.
        if (lines.Count > rows)
            lines.RemoveRange(rows, lines.Count - rows);
        return lines;
    }

    /// <summary>
    /// The counts of one file: "+1 -1" (the plus in Ok, the minus in Bad), "+1" or "-3" when one side is 0, "bin" for a binary file, "renamed"
    /// for a rename with no change, and "±0" for a change with no lines (a mode change or an empty file).
    /// </summary>
    internal static IReadOnlyList<Seg> Counts(IncomingFile f)
    {
        if (f.Added is null || f.Deleted is null)
            return [new Seg("bin", Dim)];

        var added = f.Added.Value;
        var deleted = f.Deleted.Value;
        if (added == 0 && deleted == 0)
            return [new Seg(f.OldPath is null ? "±0" : "renamed", Dim)];

        var segs = new List<Seg>();
        if (added > 0)
            segs.Add(new Seg("+" + added, Ok));
        if (added > 0 && deleted > 0)
            segs.Add(new Seg(" ", Fg));
        if (deleted > 0)
            segs.Add(new Seg("-" + deleted, Bad));
        return segs;
    }

    /// <summary>The columns that the segments take, in total.</summary>
    internal static int SegColumns(IReadOnlyList<Seg> segs)
    {
        var total = 0;
        foreach (var seg in segs)
            total += TextFit.Columns(seg.Text);
        return total;
    }

    // The current branch: its own commits are not what it brings in, so it shows its tip and the three commits under it, newest first.
    // It needs no read of its own: the layout of the preview is enough.
    private static void AddCurrent(BranchDetailInput d, int width, List<DetailLine> lines)
    {
        lines.Add(Plain("latest on " + d.Name, Dim, width));
        if (d.Layout is null)
        {
            // Nothing is said while the layout loads: the block is drawn again when it arrives.
            lines.Add(EmptyLine());
            return;
        }

        lines.Add(EmptyLine());
        var shared = d.Layout.Shared;
        for (var i = shared.Count - 1; i >= 0; i--)
            lines.Add(CommitLine(shared[i], width));
    }

    // Any other branch: how its new commits compare with the current branch, then the files they change.
    private static void AddIncoming(BranchDetailInput d, int width, List<DetailLine> lines, int rows)
    {
        lines.Add(Plain(d.Name, new Style(d.NameInk), width));
        var layout = d.Layout;
        if (layout is null)
        {
            // Nothing is said while the layout loads (or while the block is blank): the block is drawn again when it arrives.
            lines.Add(EmptyLine());
            return;
        }

        if (layout.Kind == GraphKind.Same)
        {
            lines.Add(Plain("same commit as " + layout.Current, Dim, width));
            return;
        }
        if (layout.Kind == GraphKind.UpToDate)
        {
            lines.Add(Plain("nothing new to merge", Dim, width));
            return;
        }

        var commits = MergeScenes.Plural(layout.Ahead, "new commit");
        if (layout.Kind == GraphKind.Unrelated)
        {
            lines.Add(Plain(commits + " · unrelated", Dim, width));
            return;
        }

        if (d.FilesState == IncomingFilesState.Read && d.Files is not null)
            AddFiles(d.Files, commits, width, lines, rows);
        else if (d.FilesState == IncomingFilesState.Reading)
            lines.Add(Plain(commits + " · reading…", Dim, width));
        else
            lines.Add(Plain(commits, Dim, width));
    }

    // The summary, a blank row, then the files. The last row that fits is "+K more files" when not every file has one.
    private static void AddFiles(IncomingFiles files, string commits, int width, List<DetailLine> lines, int rows)
    {
        if (files.Count == 0)
        {
            lines.Add(Plain(commits + " · no file changes", Dim, width));
            return;
        }

        lines.Add(Plain(commits + " · " + MergeScenes.Plural(files.Count, "file"), Dim, width));
        lines.Add(EmptyLine());

        var slots = rows - FirstFileLine;
        if (slots <= 0)
            return;

        var kept = files.Files;
        if (files.Count <= slots && kept.Count == files.Count)
        {
            foreach (var f in kept)
                lines.Add(FileLine(f, width));
            return;
        }

        var shown = Math.Min(kept.Count, slots - 1);
        for (var i = 0; i < shown; i++)
            lines.Add(FileLine(kept[i], width));
        lines.Add(Plain("+" + MergeScenes.Plural(files.Count - shown, "more file"), Dim, width));
    }

    // One file: its path, with the end kept when it is cut (the file name matters most), and its counts on the right. When the counts
    // would take the whole row, the path is cut alone.
    private static DetailLine FileLine(IncomingFile f, int width)
    {
        var right = Counts(f);
        var rightCols = SegColumns(right);
        if (rightCols >= width)
            return new DetailLine([new Seg(TextFit.Fit(f.Path, width), Fg)], []);

        var path = TextFit.Tail(TextFit.Sanitize(f.Path), width - rightCols - 1);
        return new DetailLine([new Seg(path, Fg)], right);
    }

    // A commit: its short hash (7 characters) and the subject as far as it fits.
    private static DetailLine CommitLine(CommitInfo c, int width)
    {
        var hash = TextFit.Fit(ShortSeven(c.ShortHash), width);
        var rest = width - TextFit.Columns(hash) - 1;
        if (rest <= 0)
            return new DetailLine([new Seg(hash, HashInk)], []);
        return new DetailLine([new Seg(hash, HashInk), new Seg(" " + TextFit.Fit(c.Subject, rest), Fg)], []);
    }

    private static string ShortSeven(string hash) => hash.Length > 7 ? hash.Substring(0, 7) : hash;

    // One line of text in one style, cut to the width.
    private static DetailLine Plain(string text, Style style, int width) =>
        new([new Seg(TextFit.Fit(text, width), style)], []);

    private static DetailLine EmptyLine() => new([], []);
}
