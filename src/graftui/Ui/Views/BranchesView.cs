using graftui.Model;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;

namespace graftui.Ui.Views;

/// <summary>The branch list: mark, name and ahead/behind. The ahead/behind column is dropped when the pane is too narrow for it.</summary>
public sealed class BranchesView : PaneView
{
    private static readonly Style Fg = new(Ink.Fg), FgB = new(Ink.Fg, true), Dim = new(Ink.Dim);
    private static readonly Style Inc = new(Ink.Inc), Warn = new(Ink.Warn);

    // The narrowest header worth drawing: "vs" and one name letter, with the "…" that cuts it.
    private const int MinHeaderCols = 4;

    // Column of the first name character: 1 the selection arrow, 2 the current branch's star, 3 a space.
    internal const int NameX = 4;

    private int _top;

    public BranchesView()
    {
        Title = "Branches";
        TitleSegs = [new Seg("Branches", Fg), new Seg(" │ Changes", Dim)];
    }

    public IReadOnlyList<Branch> Branches { get; set; } = [];

    public IReadOnlyDictionary<string, (int Ahead, int Behind)> Counts { get; set; } =
        new Dictionary<string, (int Ahead, int Behind)>();

    public int Selected { get; set; }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        ClearAll();
        DrawFrame();

        var w = Viewport.Width;
        var h = Viewport.Height;
        var innerW = w - 2;
        var innerH = h - 2;
        if (innerW <= 0 || innerH <= 0 || Branches.Count == 0)
            return true;

        var abW = AbWidth(Branches, Counts);

        // mark (2 columns), space, name, space, ab: the name takes what the ab column and its space leave.
        var nameW = NameWidth(innerW, abW);
        var showAb = true;
        if (nameW < 6)
        {
            nameW += abW + 1; // hide the ab column and its space
            showAb = false;
        }

        // The header names what the ab column compares with. It takes the first row, so the branches start one row lower.
        var showHeader = showAb && innerH >= 2 && HasCounts(Branches, Counts);
        var firstRow = showHeader ? 2 : 1;
        var listH = showHeader ? innerH - 1 : innerH;

        var sel = Math.Clamp(Selected, 0, Branches.Count - 1);
        _top = ScrollTop(sel, _top, Branches.Count, listH);
        var lanes = BranchLanes.Assign(Branches);

        for (var i = _top; i < Branches.Count && i < _top + listH; i++)
        {
            var b = Branches[i];
            var row = firstRow + (i - _top);
            var isSel = i == sel;
            var style = NameStyle(b, lanes[i]);

            Paint.Fill(this, 1, row, innerW, Palette.Get(Fg, isSel));

            // mark: the selection arrow, then the current branch's star
            var (arrow, star) = MarkCells(isSel, b.IsCurrent);
            DrawText(1, row, arrow, 1, FgB, isSel);
            DrawText(2, row, star, 1, style, isSel);

            // name
            DrawText(NameX, row, b.Name, nameW, style, isSel);

            if (showAb && !b.IsCurrent && Counts.TryGetValue(b.Name, out var c))
            {
                var segs = new List<Seg>
                {
                    new($"↑{c.Ahead}", c.Ahead > 0 ? Inc : Dim),
                    new($"↓{c.Behind}", c.Behind > 0 ? Warn : Dim),
                };
                Paint.Segs(this, NameX + nameW + 1, row, segs, abW, isSel);
            }
        }

        if (showHeader)
        {
            // Ends over the last column of the ab column, so the header reads as the column's title.
            var header = AbHeader(CurrentName(Branches), innerW - 2);
            var cols = TextFit.Columns(header);
            DrawText(HeaderX(innerW, cols), 1, header, cols, Dim);
        }
        return true;
    }

    /// <summary>
    /// A branch's name and mark: its lane colour, bold for the current branch. The colour never depends on the selection.
    /// </summary>
    internal static Style NameStyle(Branch b, int lane) => new(BranchInks.Of(lane), b.IsCurrent);

    /// <summary>The two mark columns: "▸" on the selected row, "*" on the current branch, and a space where there is none.</summary>
    internal static (string Arrow, string Star) MarkCells(bool selected, bool current) =>
        (selected ? "▸" : " ", current ? "*" : " ");

    /// <summary>The name's width: the inner width less the columns before the name (mark, space) and after it (space, ab column).</summary>
    internal static int NameWidth(int innerW, int abW) => innerW - NameX - abW;

    /// <summary>The ab column's header: "vs main", or "" when there is no room for even a short one.</summary>
    internal static string AbHeader(string current, int maxCols)
    {
        if (maxCols < MinHeaderCols)
            return "";
        return TextFit.Fit("vs " + current, maxCols);
    }

    // The name of the checked-out branch, or "HEAD" when no branch is checked out.
    internal static string CurrentName(IReadOnlyList<Branch> branches)
    {
        foreach (var b in branches)
            if (b.IsCurrent)
                return b.Name;
        return "HEAD";
    }

    /// <summary>
    /// The first branch shown, so the selected one is in view. The list scrolls only as far as it must, and never leaves
    /// blank rows at the end when there are more rows than branches.
    /// </summary>
    internal static int ScrollTop(int sel, int top, int count, int listH)
    {
        if (sel < top)
            top = sel;
        if (sel >= top + listH)
            top = sel - listH + 1;
        return Math.Clamp(top, 0, Math.Max(0, count - listH));
    }

    /// <summary>The x of the header: it ends on the last inner column, which is the last column of the ab column.</summary>
    internal static int HeaderX(int innerW, int headerCols) => innerW - headerCols;

    /// <summary>True when at least one branch other than the current one has counts, so the ab column has something to show.</summary>
    internal static bool HasCounts(IReadOnlyList<Branch> branches, IReadOnlyDictionary<string, (int Ahead, int Behind)> counts)
    {
        foreach (var b in branches)
            if (!b.IsCurrent && counts.ContainsKey(b.Name))
                return true;
        return false;
    }

    private static string AbText((int Ahead, int Behind) c) => $"↑{c.Ahead}↓{c.Behind}";

    // Width of the "↑a↓b" column: wide enough for every branch that has counts, and at least 4.
    private static int AbWidth(IReadOnlyList<Branch> branches, IReadOnlyDictionary<string, (int Ahead, int Behind)> counts)
    {
        var abW = 4;
        foreach (var b in branches)
            if (!b.IsCurrent && counts.TryGetValue(b.Name, out var c))
                abW = Math.Max(abW, TextFit.Columns(AbText(c)));
        return abW;
    }

    /// <summary>
    /// Pane width (border included) that shows the longest branch name in full, with its mark and the ↑↓ column.
    /// The layout clamps this to its own bounds; a narrower pane truncates names.
    /// </summary>
    public static int WantedWidth(IReadOnlyList<Branch> branches, IReadOnlyDictionary<string, (int Ahead, int Behind)> counts)
    {
        var longest = 0;
        foreach (var b in branches)
            longest = Math.Max(longest, TextFit.Columns(TextFit.Sanitize(b.Name)));
        // border 2 + mark (2 columns) and space 1 + name + space 1 + ab
        return 2 + 3 + longest + 1 + AbWidth(branches, counts);
    }
}
