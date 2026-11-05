using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>The one-line sentence above the art: what is being compared or merged. Names are cut so the line fits.</summary>
public static class DirectionLine
{
    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style OkB = new(Ink.Ok, true);
    private static readonly Style BadB = new(Ink.Bad, true);
    private static readonly Style Hash = new(Ink.Hash);

    // When fewer columns than this are left for the names beside the long wording, Browse uses the narrow form.
    private const int NarrowNamesBudget = 16;

    /// <summary>
    /// "feature/x  is 1 ahead of, 1 behind  main": the selected branch, the counts, then the current branch. When the names
    /// do not fit beside the long wording, the narrow "feature/x  ↑1↓1 vs  main" is used. Only a detached HEAD
    /// is labelled; the art already marks the current branch. Each name is in its branch's lane colour.
    /// </summary>
    public static IReadOnlyList<Seg> Browse(GraphLayout L, int maxCols)
    {
        var cur = L.Detached ? "HEAD" : L.Current;
        var extra = L.Detached ? " (detached)" : "";
        var selB = new Style(BranchInks.Selected(L), true);
        var curB = new Style(BranchInks.Current(L), true);
        // The current branch itself, compared with itself, is just its name.
        if (L.Kind == GraphKind.Same && L.Selected == L.Current)
            return [new Seg(TextFit.Fit(cur, maxCols), curB)];

        var middle = $"  is {L.Ahead} ahead of, {L.Behind} behind  ";
        var budget = maxCols - TextFit.Columns(middle) - TextFit.Columns(extra);
        var namesCols = TextFit.Columns(L.Selected) + TextFit.Columns(cur);
        if (namesCols > budget && budget < NarrowNamesBudget)
        {
            middle = $"  ↑{L.Ahead}↓{L.Behind} vs  ";
            budget = maxCols - TextFit.Columns(middle) - TextFit.Columns(extra);
        }

        var (sel, curName) = TextFit.FitPair(L.Selected, cur, budget);
        List<Seg> segs = [new Seg(sel, selB), new Seg(middle, Dim), new Seg(curName, curB)];
        if (extra.Length > 0)
            segs.Add(new Seg(extra, Dim));
        return segs;
    }

    /// <summary>
    /// "merge        feature/x  -> into  main (current)". The inks are the two branches' lane colours (see <see cref="BranchInks"/>);
    /// a detached HEAD is named HEAD in the foreground.
    /// </summary>
    public static IReadOnlyList<Seg> Merge(string sel, Ink selInk, string cur, Ink curInk, bool detached, int maxCols) =>
        IntoLine("merge        ", sel, selInk, detached ? "HEAD" : cur, curInk, detached ? " (detached)" : " (current)", maxCols);

    /// <summary>"stash, merge feature/x  -> into  main (current)": the stash-merge-pop question and its scene.</summary>
    public static IReadOnlyList<Seg> StashMerge(string sel, Ink selInk, string cur, Ink curInk, int maxCols) =>
        IntoLine("stash, merge ", sel, selInk, cur, curInk, " (current)", maxCols);

    /// <summary>"switch       main  ->  feature/x": the current branch's label moves to the selected one. Each name keeps its lane colour.</summary>
    public static IReadOnlyList<Seg> Switch(string from, Ink fromInk, string to, Ink toInk, int maxCols)
    {
        const string verb = "switch       ";
        const string arrow = "  ->  ";
        var (a, b) = TextFit.FitPair(from, to, maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow), min: 1);
        return [new Seg(verb, Dim), new Seg(a, new Style(fromInk, true)), new Seg(arrow, Dim), new Seg(b, new Style(toInk, true))];
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

    /// <summary>"rename       feature/x  ->  feature/y"; with no new name yet (the box is open) only the old name. The new name is typed in Ok.</summary>
    public static IReadOnlyList<Seg> Rename(string old, Ink oldInk, string? nu, int maxCols)
    {
        const string verb = "rename       ";
        const string arrow = "  ->  ";
        var oldB = new Style(oldInk, true);
        if (nu is null)
            return [new Seg(verb, Dim), new Seg(TextFit.Fit(old, maxCols - TextFit.Columns(verb)), oldB)];

        var (a, b) = TextFit.FitPair(old, nu, maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow), min: 1);
        return [new Seg(verb, Dim), new Seg(a, oldB), new Seg(arrow, Dim), new Seg(b, OkB)];
    }

    /// <summary>"delete       feature/x"</summary>
    public static IReadOnlyList<Seg> Delete(string name, int maxCols)
    {
        const string verb = "delete       ";
        return [new Seg(verb, Dim), new Seg(TextFit.Fit(name, maxCols - TextFit.Columns(verb)), BadB)];
    }

    // The two-branch line that ends "into <current>". The verb is 13 columns, so the names line up with the others.
    private static IReadOnlyList<Seg> IntoLine(string verb, string sel, Ink selInk, string curName, Ink curInk, string curExtra, int maxCols)
    {
        const string arrow = "  -> into  ";
        var (selName, curShown) = TextFit.FitPair(sel, curName,
            maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow) - TextFit.Columns(curExtra));
        return
        [
            new Seg(verb, Dim),
            new Seg(selName, new Style(selInk, true)),
            new Seg(arrow, Dim),
            new Seg(curShown, new Style(curInk, true)),
            new Seg(curExtra, Dim),
        ];
    }

    // The two-branch line that ends "-> onto <branch>": the first branch can carry an extra (its "(current)"), and so can the second.
    private static IReadOnlyList<Seg> OntoLine(string verb, string first, Ink firstInk, string firstExtra, string second, Ink secondInk,
        string secondExtra, int maxCols)
    {
        const string arrow = "  -> onto  ";
        var budget = maxCols - TextFit.Columns(verb) - TextFit.Columns(arrow) - TextFit.Columns(firstExtra) - TextFit.Columns(secondExtra);
        var (firstShown, secondShown) = TextFit.FitPair(first, second, budget);
        return
        [
            new Seg(verb, Dim),
            new Seg(firstShown, new Style(firstInk, true)),
            new Seg(firstExtra, Dim),
            new Seg(arrow, Dim),
            new Seg(secondShown, new Style(secondInk, true)),
            new Seg(secondExtra, Dim),
        ];
    }

    /// <summary>
    /// "rebase       feature/x (current)  -> onto  main": the current branch, which is replayed, then the branch it is put on.
    /// Each name is in its branch's lane colour.
    /// </summary>
    public static IReadOnlyList<Seg> Rebase(string cur, Ink curInk, string onto, Ink ontoInk, int maxCols) =>
        OntoLine("rebase       ", cur, curInk, " (current)", onto, ontoInk, "", maxCols);

    /// <summary>
    /// The line with a dim tail added ("   todo", "   2 selected"). A tail that does not fit in <paramref name="cols"/> is left out,
    /// and the line is returned as it was.
    /// </summary>
    public static IReadOnlyList<Seg> WithTail(IReadOnlyList<Seg> line, string tail, int cols)
    {
        var used = line.Sum(s => TextFit.Columns(s.Text));
        if (used + TextFit.Columns(tail) > cols)
            return line;
        return [.. line, new Seg(tail, Dim)];
    }

    /// <summary>The line while the preview loads: the selected branch in its lane colour, then "…".</summary>
    public static IReadOnlyList<Seg> Loading(string sel, Ink selInk) =>
        [new Seg(sel, new Style(selInk, true)), new Seg("  …", Dim)];
}
