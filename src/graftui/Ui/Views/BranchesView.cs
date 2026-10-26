using graftui.Model;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;

namespace graftui.Ui.Views;

/// <summary>The branch list: mark, name, ahead/behind and short hash. Columns are dropped as the pane narrows.</summary>
public sealed class BranchesView : PaneView
{
    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim), CurB = new(Ink.Cur, true);
    private static readonly Style Inc = new(Ink.Inc), Warn = new(Ink.Warn), Hash = new(Ink.Hash);

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

        var sel = Math.Clamp(Selected, 0, Branches.Count - 1);
        if (sel < _top)
            _top = sel;
        if (sel >= _top + innerH)
            _top = sel - innerH + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, Branches.Count - innerH));

        var abW = AbWidth(Branches, Counts);

        // mark + space + name + space + ab + space + hash(7) = 11 columns beyond the name and ab.
        var nameW = innerW - 2 - 1 - abW - 1 - 7;
        var showHash = true;
        var showAb = true;
        if (nameW < 14)
        {
            nameW += 8; // hide the hash and its space
            showHash = false;
        }
        if (nameW < 6)
        {
            nameW += abW + 1; // hide the ab column and its space
            showAb = false;
        }

        for (var i = _top; i < Branches.Count && i < _top + innerH; i++)
        {
            var b = Branches[i];
            var row = 1 + (i - _top);
            var isSel = i == sel;

            Paint.Fill(this, 1, row, innerW, Palette.Get(Fg, isSel));

            // mark
            DrawText(1, row, b.IsCurrent ? "*" : " ", 1, CurB, isSel);

            // name
            var nameX = 3;
            var nameStyle = b.IsCurrent ? CurB : Fg;
            DrawText(nameX, row, b.Name, nameW, nameStyle, isSel);

            var x = nameX + nameW + 1;
            var abX = x;
            if (showAb)
            {
                if (!b.IsCurrent && Counts.TryGetValue(b.Name, out var c))
                {
                    var segs = new List<Seg>
                    {
                        new($"↑{c.Ahead}", c.Ahead > 0 ? Inc : Dim),
                        new($"↓{c.Behind}", c.Behind > 0 ? Warn : Dim),
                    };
                    Paint.Segs(this, abX, row, segs, abW, isSel);
                }
                x += abW + 1;
            }

            if (showHash)
                DrawText(x, row, b.ShortHash, 7, Hash, isSel);
        }
        return true;
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
    /// Pane width (border included) that shows the longest branch name in full, with its mark, the ↑↓ column and the hash.
    /// The layout clamps this to its own bounds; a narrower pane truncates names.
    /// </summary>
    public static int WantedWidth(IReadOnlyList<Branch> branches, IReadOnlyDictionary<string, (int Ahead, int Behind)> counts)
    {
        var longest = 0;
        foreach (var b in branches)
            longest = Math.Max(longest, TextFit.Columns(TextFit.Sanitize(b.Name)));
        // border 2 + mark and space 2 + name + space 1 + ab + space 1 + hash 7
        return 2 + 2 + longest + 1 + AbWidth(branches, counts) + 1 + 7;
    }
}
