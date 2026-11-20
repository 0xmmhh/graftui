using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The words of the Branches preview sheet: the pane title, the forecast line, the command line and the ghost kind. Pure: no git,
/// no views. "main" in the texts is always the current branch.
/// </summary>
public static class PreviewText
{
    /// <summary>The title of the incoming list under the lane graph.</summary>
    public const string IncomingTitle = "commits that would come in (newest first)";

    /// <summary>The forecast line while the forecast is still being read.</summary>
    public const string Pending = "checking what m would do…";

    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style Ok = new(Ink.Ok);
    private static readonly Style Bad = new(Ink.Bad);
    private static readonly Style Warn = new(Ink.Warn);

    // The Dim tail on a dirty work tree: m asks to stash the changes first. The same words as ForecastText.
    private const string StashFirst = "m asks to stash your changes first";

    /// <summary>True when the selected branch is the current one (and HEAD is not detached).</summary>
    public static bool IsCurrent(GraphLayout L) => !L.Detached && L.Selected == L.Current;

    /// <summary>"main is checked out", "HEAD is detached", or "If you press m: feature → main".</summary>
    public static string Title(GraphLayout L)
    {
        if (IsCurrent(L))
            return L.Current + " is checked out";
        if (L.Detached)
            return "HEAD is detached";
        return "If you press m: " + L.Selected + " → " + L.Current;
    }

    /// <summary>
    /// Sheet row 0: what m would do, as coloured segments. Empty when the forecast says nothing (Unavailable). Clean is the snapshot's
    /// clean flag: on a dirty tree the Dim tail asks to stash first.
    /// </summary>
    public static IReadOnlyList<Seg> Forecast(GraphLayout L, MergeForecast? f, bool clean)
    {
        var segs = new List<Seg>();
        if (IsCurrent(L))
        {
            segs.Add(new Seg(L.Current + " is checked out: pick a branch on the left to preview its merge.", Dim));
            return segs;
        }

        if (L.Detached)
        {
            segs.Add(new Seg("! HEAD is detached", Warn));
            segs.Add(new Seg(" · switch to a branch first, then m merges into it", Dim));
            return segs;
        }

        if (L.Kind == GraphKind.Same)
        {
            segs.Add(new Seg("= same commit as " + L.Current, Dim));
            segs.Add(new Seg(" · nothing to merge", Dim));
            return segs;
        }

        if (L.Kind == GraphKind.Unrelated)
        {
            segs.Add(new Seg("✕ no common history", Bad));
            segs.Add(new Seg(" · git refuses to merge unrelated histories", Dim));
            return segs;
        }

        if (f is null)
        {
            segs.Add(new Seg(Pending, Dim));
            return segs;
        }

        if (f.Kind == ForecastKind.Clean)
        {
            var cleanTail = "creates a merge commit on " + L.Current;
            if (f.Note == MergeForecast.FfOffNote)
                cleanTail += " (merge.ff=false)";
            segs.Add(new Seg("✔ merges cleanly", Ok));
            segs.Add(new Seg(Tail(clean, cleanTail), Dim));
            return segs;
        }

        if (f.Kind == ForecastKind.FastForward)
        {
            segs.Add(new Seg("⇢ fast-forwards", Ok));
            segs.Add(new Seg(Tail(clean, MergeScenes.Plural(L.Ahead, "commit") + ", no merge commit"), Dim));
            return segs;
        }

        if (f.Kind == ForecastKind.UpToDate)
        {
            segs.Add(new Seg("✔ already up to date", Ok));
            segs.Add(new Seg(Tail(clean, "nothing to merge"), Dim));
            return segs;
        }

        if (f.Kind == ForecastKind.Conflict)
        {
            segs.Add(new Seg(ConflictText(f), Bad));
            segs.Add(new Seg(Tail(clean, "the merge would be aborted, " + L.Current + " unchanged"), Dim));
            return segs;
        }

        if (f.Kind == ForecastKind.Refused)
        {
            if (f.Note == MergeForecast.FfOnlyNote)
            {
                segs.Add(new Seg("✕ can't fast-forward", Bad));
                segs.Add(new Seg(" · the branches diverged (merge.ff=only)", Dim));
            }
            else
            {
                segs.Add(new Seg("✕ can't merge", Bad));
                if (f.Note is not null)
                    segs.Add(new Seg(" · " + f.Note, Dim));
            }
        }

        return segs;
    }

    /// <summary>
    /// Sheet row 1: "$ git merge feature", the simplified command the Commands pane shows. Empty when m would not run git merge
    /// (the current branch, or a detached HEAD).
    /// </summary>
    public static IReadOnlyList<Seg> Command(GraphLayout L)
    {
        var segs = new List<Seg>();
        if (L.Detached || IsCurrent(L))
            return segs;

        segs.Add(new Seg("$ git merge " + L.Selected, Dim));
        return segs;
    }

    /// <summary>
    /// The ghost the lane graph draws at its right end. None for the current branch, a detached HEAD, the same commit, unrelated
    /// history, and any forecast that is not a merge, a fast-forward or a conflict (up to date, refused, unavailable, not read yet).
    /// </summary>
    public static GhostKind Ghost(GraphLayout L, MergeForecast? f)
    {
        if (IsCurrent(L) || L.Detached)
            return GhostKind.None;
        if (L.Kind == GraphKind.Same || L.Kind == GraphKind.Unrelated)
            return GhostKind.None;
        if (f is null)
            return GhostKind.None;

        if (f.Kind == ForecastKind.Clean)
            return GhostKind.Merge;
        if (f.Kind == ForecastKind.FastForward)
            return GhostKind.FastForward;
        if (f.Kind == ForecastKind.Conflict)
            return GhostKind.Conflict;
        return GhostKind.None;
    }

    // The Dim tail of a forecast line: the clean text, or on a dirty tree the words that ask to stash first.
    private static string Tail(bool clean, string cleanText)
    {
        if (clean)
            return " · " + cleanText;
        return " · " + StashFirst;
    }

    // "✕ would conflict in app.txt", or "✕ would conflict" when git named no file.
    private static string ConflictText(MergeForecast f)
    {
        if (f.Files.Count == 0)
            return "✕ would conflict";

        var paths = new List<string>();
        foreach (var file in f.Files)
            paths.Add(file.Path);
        return "✕ would conflict in " + MergeScenes.ConflictFiles(paths);
    }
}
