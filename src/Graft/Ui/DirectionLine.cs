using Graft.Git;

namespace Graft.Ui;

/// <summary>The one-line sentence above the art: what is being compared or merged. Names are cut so the line fits.</summary>
public static class DirectionLine
{
    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style IncB = new(Ink.Inc, true);
    private static readonly Style CurB = new(Ink.Cur, true);

    public static IReadOnlyList<Seg> Browse(GraphLayout L, int maxCols)
    {
        var cur = L.Detached ? "HEAD" : L.Current;
        var curExtra = L.Detached ? " (detached)" : " (current)";
        if (L.Kind == GraphKind.Same)
        {
            var limit = NameLimit(maxCols, curExtra);
            return [new Seg(TextFit.Fit(cur, limit), CurB), new Seg(curExtra, Dim)];
        }

        var middle = $"  is {L.Ahead} ahead, {L.Behind} behind  ";
        var fixedCols = TextFit.Columns(middle) + TextFit.Columns(curExtra);
        var names = NameLimit(maxCols, fixedCols);
        return
        [
            new Seg(TextFit.Fit(L.Selected, names), IncB),
            new Seg(middle, Dim),
            new Seg(TextFit.Fit(cur, names), CurB),
            new Seg(curExtra, Dim),
        ];
    }

    public static IReadOnlyList<Seg> Merge(string sel, string cur, bool detached, int maxCols)
    {
        var curName = detached ? "HEAD" : cur;
        var curExtra = detached ? " (detached)" : " (current)";
        const string verb = "merge        ";
        const string arrow = "  ──▶ into  ";
        var names = NameLimit(maxCols, TextFit.Columns(verb) + TextFit.Columns(arrow) + TextFit.Columns(curExtra));
        return
        [
            new Seg(verb, Dim),
            new Seg(TextFit.Fit(sel, names), IncB),
            new Seg(arrow, Dim),
            new Seg(TextFit.Fit(curName, names), CurB),
            new Seg(curExtra, Dim),
        ];
    }

    public static IReadOnlyList<Seg> Loading(string sel) =>
        [new Seg(sel, IncB), new Seg("  …", Dim)];

    // Each of the two names gets half of what the fixed text leaves, but never less than 8 columns.
    private static int NameLimit(int maxCols, string fixedText) => NameLimit(maxCols, TextFit.Columns(fixedText));

    private static int NameLimit(int maxCols, int fixedCols) => Math.Max(8, (maxCols - fixedCols) / 2);
}
