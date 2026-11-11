using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The forecast's words on screen: the caption under the preview art, and the footer rows that name each conflicting file.
/// Pure: no git, no views.
/// </summary>
public static class ForecastText
{
    private static readonly Style OkB = new(Ink.Ok, true);
    private static readonly Style BadB = new(Ink.Bad, true);
    private static readonly Style Dim = new(Ink.Dim);

    /// <summary>The caption under the preview art, or null when the forecast says nothing (Unavailable).</summary>
    public static Caption? Caption(MergeForecast f, GraphLayout L)
    {
        switch (f.Kind)
        {
            case ForecastKind.Clean:
                if (f.Note == MergeForecast.FfOffNote)
                    return new Caption("✔ m makes a merge commit (merge.ff=false)", OkB);
                return new Caption("✔ m merges cleanly: a merge commit", OkB);

            case ForecastKind.FastForward:
                return new Caption("✔ m fast-forwards: " + MergeScenes.Plural(L.Ahead, "commit"), OkB);

            case ForecastKind.UpToDate:
                return new Caption("✔ already up to date: nothing to merge", OkB);

            case ForecastKind.Conflict:
                return new Caption(ConflictText(f), BadB);

            case ForecastKind.Refused:
                return new Caption(RefusedText(f), BadB);

            default:
                return null;
        }
    }

    /// <summary>
    /// The idle commit panel with one footer row per conflicting file: a cross, the file, and what kind of conflict it is.
    /// Any other forecast leaves the panel as it is.
    /// </summary>
    public static CommitPanel WithFiles(CommitPanel idle, MergeForecast f, GraphLayout L)
    {
        if (f.Kind != ForecastKind.Conflict)
            return idle;

        var rows = new List<CommitRow>();
        foreach (var file in f.Files)
            rows.Add(new CommitRow('✕', BadB, file.Path, BadB, "would conflict: " + KindText(file.Kind, L), Dim));
        return idle with { Footer = rows };
    }

    /// <summary>"content", "added on both", "deleted on main, changed on feature/x", "binary", "renamed" or "conflict".</summary>
    public static string KindText(ForecastConflict k, GraphLayout L)
    {
        switch (k)
        {
            case ForecastConflict.Content:
                return "content";
            case ForecastConflict.BothAdded:
                return "added on both";
            case ForecastConflict.DeletedCurrent:
                return $"deleted on {L.Current}, changed on {L.Selected}";
            case ForecastConflict.DeletedSelected:
                return $"deleted on {L.Selected}, changed on {L.Current}";
            case ForecastConflict.Binary:
                return "binary";
            case ForecastConflict.Rename:
                return "renamed";
            default:
                return "conflict";
        }
    }

    private static string ConflictText(MergeForecast f)
    {
        if (f.Files.Count == 0)
            return "✕ m would conflict: the merge would be aborted";

        var paths = f.Files.Select(file => file.Path).ToList();
        return "✕ m would conflict in " + MergeScenes.ConflictFiles(paths) + ": the merge would be aborted";
    }

    private static string RefusedText(MergeForecast f)
    {
        if (f.Note == MergeForecast.FfOnlyNote)
            return "✕ m can't fast-forward: the branches diverged (merge.ff=only)";
        return "✕ m can't merge: " + f.Note;
    }
}
