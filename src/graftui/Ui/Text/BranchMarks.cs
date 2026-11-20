using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>What a branch row's mark says about `m`: not known yet, a merge commit, a fast-forward, a conflict, nothing to merge, or refused.</summary>
public enum BranchMark { Unknown, Merge, FastForward, Conflict, Nothing, Refused }

/// <summary>
/// The mark in column 2 of each branch row on the new look: what `m` would do with that branch. Pure: the screen asks for the cell,
/// the forecast result gives the mark, and the order of the forecasts comes from the selection.
/// </summary>
public static class BranchMarks
{
    /// <summary>The mark of a branch whose forecast is not known yet, or that git could not say anything about.</summary>
    public const string UnknownChar = "·";

    /// <summary>The mark of a branch with nothing to merge (the same commit, or already contained in the current branch).</summary>
    public const string NothingChar = "=";

    /// <summary>The mark of a branch that `m` refuses (unrelated histories, or merge.ff=only with diverged branches).</summary>
    public const string RefusedChar = "!";

    /// <summary>The mark of the current branch, and of a paused rebase's branch.</summary>
    public const string CurrentChar = "*";

    /// <summary>
    /// The mark a layout and its forecast give. The layout's kind comes first: Same and UpToDate have nothing to merge, Unrelated is
    /// refused. Then the forecast: Clean is a merge commit, FastForward a fast-forward, Conflict a conflict, UpToDate nothing, Refused
    /// refused, and Unavailable is not known.
    /// </summary>
    public static BranchMark Of(GraphLayout layout, MergeForecast forecast)
    {
        if (layout.Kind == GraphKind.Same || layout.Kind == GraphKind.UpToDate)
            return BranchMark.Nothing;
        if (layout.Kind == GraphKind.Unrelated)
            return BranchMark.Refused;

        if (forecast.Kind == ForecastKind.Clean)
            return BranchMark.Merge;
        if (forecast.Kind == ForecastKind.FastForward)
            return BranchMark.FastForward;
        if (forecast.Kind == ForecastKind.Conflict)
            return BranchMark.Conflict;
        if (forecast.Kind == ForecastKind.UpToDate)
            return BranchMark.Nothing;
        if (forecast.Kind == ForecastKind.Refused)
            return BranchMark.Refused;
        return BranchMark.Unknown;
    }

    /// <summary>
    /// The cell at column 2 of a row. The current (or paused) branch gets "*" in its name's style. When HEAD is detached, every other
    /// row is blank. Otherwise the mark: Merge is the merge glyph in Mrg, FastForward the fast-forward glyph in Ok, Conflict the conflict
    /// glyph in Bad, Nothing "=" in Dim, Refused "!" in Warn, and Unknown (also a name missing from marks) "·" in Dim.
    /// </summary>
    public static (string Text, Style Style) Cell(string name, bool currentOrPaused, bool detached, Style nameStyle,
        IReadOnlyDictionary<string, BranchMark> marks, Glyphs glyphs)
    {
        if (currentOrPaused)
            return (CurrentChar, nameStyle);
        if (detached)
            return (" ", new Style(Ink.Fg));

        var mark = BranchMark.Unknown;
        if (marks.TryGetValue(name, out var found))
            mark = found;
        return MarkCell(mark, glyphs);
    }

    // The glyph and its ink for one mark. The if chain is the whole table: each mark has one glyph.
    private static (string Text, Style Style) MarkCell(BranchMark mark, Glyphs glyphs)
    {
        if (mark == BranchMark.Merge)
            return (glyphs.MergeMark.ToString(), new Style(Ink.Mrg));
        if (mark == BranchMark.FastForward)
            return (glyphs.FastForward.ToString(), new Style(Ink.Ok));
        if (mark == BranchMark.Conflict)
            return (glyphs.Conflict.ToString(), new Style(Ink.Bad));
        if (mark == BranchMark.Nothing)
            return (NothingChar, new Style(Ink.Dim));
        if (mark == BranchMark.Refused)
            return (RefusedChar, new Style(Ink.Warn));
        return (UnknownChar, new Style(Ink.Dim));
    }

    /// <summary>
    /// The branches to forecast, nearest the selection first: the selected row, then the row above and the row below, then two above
    /// and two below, and so on (the upper row first on a tie). The current branch is skipped. At most <paramref name="max"/> are kept.
    /// </summary>
    internal static IReadOnlyList<Branch> Order(IReadOnlyList<Branch> branches, int selected, int max)
    {
        var result = new List<Branch>();
        var n = branches.Count;
        if (n == 0 || max <= 0)
            return result;

        for (var distance = 0; distance < n && result.Count < max; distance++)
        {
            if (distance == 0)
                AddIfWanted(branches, selected, result, max);
            else
            {
                var upper = selected - distance;
                var lower = selected + distance;
                AddIfWanted(branches, upper, result, max);
                AddIfWanted(branches, lower, result, max);
            }
        }
        return result;
    }

    // Adds the branch at index when it is in the list, is not the current branch, and the result has room.
    private static void AddIfWanted(IReadOnlyList<Branch> branches, int index, List<Branch> result, int max)
    {
        if (index < 0 || index >= branches.Count || result.Count >= max)
            return;
        if (branches[index].IsCurrent)
            return;
        result.Add(branches[index]);
    }
}
