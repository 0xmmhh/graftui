using graftui.Model;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;

namespace graftui.Ui.Views;

/// <summary>The branch list: mark, name and ahead/behind. The ahead/behind column is dropped when the pane is too narrow for it.</summary>
public sealed class BranchesView : PaneView
{
    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim), CurB = new(Ink.Cur, true);
    private static readonly Style Inc = new(Ink.Inc), Warn = new(Ink.Warn);

    // The narrowest header worth drawing: "vs" and one name letter, with the "…" that cuts it.
    private const int MinHeaderCols = 4;

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

        // mark, space, name, space, ab: the name takes what the ab column and its space leave.
        var nameW = innerW - 4 - abW;
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
        if (sel < _top)
            _top = sel;
        if (sel >= _top + listH)
            _top = sel - listH + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, Branches.Count - listH));

        for (var i = _top; i < Branches.Count && i < _top + listH; i++)
        {
            var b = Branches[i];
            var row = firstRow + (i - _top);
            var isSel = i == sel;

            Paint.Fill(this, 1, row, innerW, Palette.Get(Fg, isSel));

            // mark
            DrawText(1, row, b.IsCurrent ? "*" : " ", 1, CurB, isSel);

            // name
            var nameX = 3;
            var nameStyle = b.IsCurrent ? CurB : Fg;
            DrawText(nameX, row, b.Name, nameW, nameStyle, isSel);

            if (showAb && !b.IsCurrent && Counts.TryGetValue(b.Name, out var c))
            {
                var segs = new List<Seg>
                {
                    new($"↑{c.Ahead}", c.Ahead > 0 ? Inc : Dim),
                    new($"↓{c.Behind}", c.Behind > 0 ? Warn : Dim),
                };
                Paint.Segs(this, nameX + nameW + 1, row, segs, abW, isSel);
            }
        }

        if (showHeader)
        {
            // Ends over the last column of the ab column, so the header reads as the column's title.
            var header = AbHeader(CurrentName(Branches), innerW - 2);
            var cols = TextFit.Columns(header);
            DrawText(innerW - cols, 1, header, cols, Dim);
        }
        return true;
    }

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

    // True when at least one branch other than the current one has counts, so the ab column has something to show.
    private static bool HasCounts(IReadOnlyList<Branch> branches, IReadOnlyDictionary<string, (int Ahead, int Behind)> counts)
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
        // border 2 + mark and space 2 + name + space 1 + ab
        return 2 + 2 + longest + 1 + AbWidth(branches, counts);
    }
}
