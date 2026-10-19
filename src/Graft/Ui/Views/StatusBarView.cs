using Graft.Model;
using Graft.Ui.Text;
using Terminal.Gui.ViewBase;

namespace Graft.Ui.Views;

/// <summary>Top row: the app name and repo path on the left, the branch and working-tree state on the right.</summary>
public sealed class StatusBarView : View
{
    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim), Ok = new(Ink.Ok), Warn = new(Ink.Warn);
    private static readonly Style Bad = new(Ink.Bad), CurB = new(Ink.Cur, true), Name = new(Ink.Cur, true);

    public RepoSnapshot? Snapshot { get; set; }

    public string RepoDisplay { get; set; } = "";

    public string? ErrorText { get; set; }

    // Column where the path starts: after "graft" and its two spaces.
    private const int LeftEnd = 8;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Paint.ClearAll(this);
        var w = Viewport.Width;
        if (w <= 0)
            return true;

        // "graft" and two spaces (columns 1 to 7) are never overwritten: when the window is too narrow, the right side is cut instead.
        var right = RightSegs();
        var rightCols = right.Sum(s => TextFit.Columns(s.Text));
        var rightStart = Math.Max(LeftEnd, w - 2 - rightCols);

        // "graft" and two spaces, then the path. The path is cut so it stops three columns before the right side.
        Paint.Text(this, 1, 0, "graft", 5, Name);
        Paint.Text(this, 6, 0, "  ", 2, Fg);
        var pathMax = Math.Max(0, rightStart - 3 - LeftEnd);
        Paint.Text(this, LeftEnd, 0, RepoDisplay, pathMax, Dim);

        Paint.Segs(this, rightStart, 0, right, w - 1 - rightStart);
        return true;
    }

    private List<Seg> RightSegs()
    {
        if (ErrorText is not null)
            return [new Seg("git error: " + ErrorText, Bad)];
        if (Snapshot is null)
            return [];
        if (Snapshot.CurrentBranch.Length == 0)
            return [new Seg("HEAD detached · merging disabled", Warn)];

        return
        [
            new Seg("on ", Fg),
            new Seg(Snapshot.CurrentBranch, CurB),
            new Seg(" · ", Dim),
            Snapshot.Clean
                ? new Seg("working tree clean", Ok)
                : new Seg("uncommitted changes, merging disabled", Warn),
        ];
    }
}
