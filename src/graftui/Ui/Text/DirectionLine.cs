using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>The one-line sentence above the art: what is being compared or merged. Names are cut so the line fits.</summary>
public static class DirectionLine
{
    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style IncB = new(Ink.Inc, true);
    private static readonly Style CurB = new(Ink.Cur, true);

    /// <summary>
    /// "feature/x  is 1 ahead, 1 behind  main": the selected branch, the counts, then the current branch. Only a detached HEAD
    /// is labelled; the art already marks the current branch.
    /// </summary>
    public static IReadOnlyList<Seg> Browse(GraphLayout L, int maxCols)
    {
        var cur = L.Detached ? "HEAD" : L.Current;
        var extra = L.Detached ? " (detached)" : "";
        // The current branch itself, compared with itself, is just its name.
        if (L.Kind == GraphKind.Same && L.Selected == L.Current)
            return [new Seg(TextFit.Fit(cur, maxCols), CurB)];

        var middle = $"  is {L.Ahead} ahead, {L.Behind} behind  ";
        var (sel, curName) = TextFit.FitPair(L.Selected, cur, maxCols - TextFit.Columns(middle) - TextFit.Columns(extra));
        List<Seg> segs = [new Seg(sel, IncB), new Seg(middle, Dim), new Seg(curName, CurB)];
        if (extra.Length > 0)
            segs.Add(new Seg(extra, Dim));
        return segs;
    }

    public static IReadOnlyList<Seg> Merge(string sel, string cur, bool detached, int maxCols)
    {
        var curName = detached ? "HEAD" : cur;
        var curExtra = detached ? " (detached)" : " (current)";
        const string verb = "merge        ";
        const string arrow = "  ──▶ into  ";
        var (selName, curShown) = TextFit.FitPair(sel, curName,
            maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow) - TextFit.Columns(curExtra));
        return
        [
            new Seg(verb, Dim),
            new Seg(selName, IncB),
            new Seg(arrow, Dim),
            new Seg(curShown, CurB),
            new Seg(curExtra, Dim),
        ];
    }

    public static IReadOnlyList<Seg> Loading(string sel) =>
        [new Seg(sel, IncB), new Seg("  …", Dim)];
}
