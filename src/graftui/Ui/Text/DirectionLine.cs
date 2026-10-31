using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>The one-line sentence above the art: what is being compared or merged. Names are cut so the line fits.</summary>
public static class DirectionLine
{
    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style IncB = new(Ink.Inc, true);
    private static readonly Style CurB = new(Ink.Cur, true);
    private static readonly Style OkB = new(Ink.Ok, true);
    private static readonly Style BadB = new(Ink.Bad, true);
    private static readonly Style Hash = new(Ink.Hash);

    // When fewer columns than this are left for the names beside the long wording, Browse uses the narrow form.
    private const int NarrowNamesBudget = 16;

    /// <summary>
    /// "feature/x  is 1 ahead of, 1 behind  main": the selected branch, the counts, then the current branch. When the names
    /// do not fit beside the long wording, the narrow "feature/x  ↑1↓1 vs  main" is used. Only a detached HEAD
    /// is labelled; the art already marks the current branch.
    /// </summary>
    public static IReadOnlyList<Seg> Browse(GraphLayout L, int maxCols)
    {
        var cur = L.Detached ? "HEAD" : L.Current;
        var extra = L.Detached ? " (detached)" : "";
        // The current branch itself, compared with itself, is just its name.
        if (L.Kind == GraphKind.Same && L.Selected == L.Current)
            return [new Seg(TextFit.Fit(cur, maxCols), CurB)];

        var middle = $"  is {L.Ahead} ahead of, {L.Behind} behind  ";
        var budget = maxCols - TextFit.Columns(middle) - TextFit.Columns(extra);
        var namesCols = TextFit.Columns(L.Selected) + TextFit.Columns(cur);
        if (namesCols > budget && budget < NarrowNamesBudget)
        {
            middle = $"  ↑{L.Ahead}↓{L.Behind} vs  ";
            budget = maxCols - TextFit.Columns(middle) - TextFit.Columns(extra);
        }

        var (sel, curName) = TextFit.FitPair(L.Selected, cur, budget);
        List<Seg> segs = [new Seg(sel, IncB), new Seg(middle, Dim), new Seg(curName, CurB)];
        if (extra.Length > 0)
            segs.Add(new Seg(extra, Dim));
        return segs;
    }

    public static IReadOnlyList<Seg> Merge(string sel, string cur, bool detached, int maxCols) =>
        IntoLine("merge        ", sel, detached ? "HEAD" : cur, detached ? " (detached)" : " (current)", maxCols);

    /// <summary>"stash, merge feature/x  -> into  main (current)": the stash-merge-pop question and its scene.</summary>
    public static IReadOnlyList<Seg> StashMerge(string sel, string cur, int maxCols) =>
        IntoLine("stash, merge ", sel, cur, " (current)", maxCols);

    /// <summary>"switch       main  ->  feature/x": the current branch's label moves to the selected one.</summary>
    public static IReadOnlyList<Seg> Switch(string from, string to, int maxCols)
    {
        const string verb = "switch       ";
        const string arrow = "  ->  ";
        var (a, b) = TextFit.FitPair(from, to, maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow), min: 1);
        return [new Seg(verb, Dim), new Seg(a, CurB), new Seg(arrow, Dim), new Seg(b, IncB)];
    }

    /// <summary>"new branch   topic  at abc1234": the name is what the box holds (empty while it is being typed).</summary>
    public static IReadOnlyList<Seg> NewBranch(string name, string tipShort, int maxCols)
    {
        const string verb = "new branch   ";
        const string at = "  at ";
        var budget = maxCols - TextFit.Columns(verb) - TextFit.Columns(at) - TextFit.Columns(tipShort);
        return
        [
            new Seg(verb, Dim),
            new Seg(TextFit.Fit(name, budget), OkB),
            new Seg(at, Dim),
            new Seg(tipShort, Hash),
        ];
    }

    /// <summary>"rename       feature/x  ->  feature/y"; with no new name yet (the box is open) only the old name.</summary>
    public static IReadOnlyList<Seg> Rename(string old, string? nu, int maxCols)
    {
        const string verb = "rename       ";
        const string arrow = "  ->  ";
        if (nu is null)
            return [new Seg(verb, Dim), new Seg(TextFit.Fit(old, maxCols - TextFit.Columns(verb)), IncB)];

        var (a, b) = TextFit.FitPair(old, nu, maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow), min: 1);
        return [new Seg(verb, Dim), new Seg(a, IncB), new Seg(arrow, Dim), new Seg(b, OkB)];
    }

    /// <summary>"delete       feature/x"</summary>
    public static IReadOnlyList<Seg> Delete(string name, int maxCols)
    {
        const string verb = "delete       ";
        return [new Seg(verb, Dim), new Seg(TextFit.Fit(name, maxCols - TextFit.Columns(verb)), BadB)];
    }

    // The two-branch line that ends "into <current>". The verb is 13 columns, so the names line up with the others.
    private static IReadOnlyList<Seg> IntoLine(string verb, string sel, string curName, string curExtra, int maxCols)
    {
        const string arrow = "  -> into  ";
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
