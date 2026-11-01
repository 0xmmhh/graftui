using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;

namespace graftui.Ui.Views;

/// <summary>Top row: the app name and repo path on the left, the branch and working-tree state on the right.</summary>
public sealed class StatusBarView : View
{
    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim), Ok = new(Ink.Ok), Warn = new(Ink.Warn);
    private static readonly Style Bad = new(Ink.Bad), Glyph = new(Ink.Cur);

    public RepoSnapshot? Snapshot { get; set; }

    public string RepoDisplay { get; set; } = "";

    public string? ErrorText { get; set; }

    /// <summary>
    /// Files with a change not yet committed, on the Changes tab. Null on the Branches tab, where the right side reads as before.
    /// </summary>
    public int? ChangedFiles { get; set; }

    // Column where the path starts: after the "▸" at column 1 and its space.
    internal const int LeftEnd = 3;

    /// <summary>Column where the right side starts. When the window is too narrow, the right side is cut, never the left.</summary>
    internal static int RightStart(int width, int rightCols) => Math.Max(LeftEnd, width - 2 - rightCols);

    /// <summary>How many columns the path may use: it stops three columns before the right side.</summary>
    internal static int PathMax(int width, int rightCols) => Math.Max(0, RightStart(width, rightCols) - 3 - LeftEnd);

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Paint.ClearAll(this);
        var w = Viewport.Width;
        if (w <= 0)
            return true;

        var right = RightSegs();
        var rightCols = right.Sum(s => TextFit.Columns(s.Text));

        // The "▸" at column 1 and the path from LeftEnd on. Neither is overwritten by the right side.
        Paint.Text(this, 1, 0, "▸", 1, Glyph);
        Paint.Text(this, LeftEnd, 0, RepoDisplay, PathMax(w, rightCols), Fg);

        var rightStart = RightStart(w, rightCols);
        Paint.Segs(this, rightStart, 0, right, w - 1 - rightStart);
        return true;
    }

    /// <summary>The current branch's name in the top bar: its lane colour, bold. A detached HEAD has no lane, so it is the foreground.</summary>
    internal static Style BranchStyle(RepoSnapshot s) => new(BranchInks.Named(s.Branches, s.CurrentBranch), true);

    private List<Seg> RightSegs()
    {
        if (ErrorText is not null)
            return [new Seg("git error: " + ErrorText, Bad)];
        if (Snapshot is null)
            return [];
        if (Snapshot.CurrentBranch.Length == 0)
            return [new Seg("HEAD detached · merging disabled", Warn)];

        if (ChangedFiles is { } changed)
        {
            return
            [
                new Seg("on ", Fg),
                new Seg(Snapshot.CurrentBranch, BranchStyle(Snapshot)),
                new Seg(" · ", Dim),
                changed == 0
                    ? new Seg("working tree clean", Ok)
                    : new Seg($"{MergeScenes.Plural(changed, "changed file")}, not committed", Warn),
            ];
        }

        return
        [
            new Seg("on ", Fg),
            new Seg(Snapshot.CurrentBranch, BranchStyle(Snapshot)),
            new Seg(" · ", Dim),
            Snapshot.Clean
                ? new Seg("working tree clean", Ok)
                : new Seg("uncommitted changes, merging disabled", Warn),
        ];
    }
}
