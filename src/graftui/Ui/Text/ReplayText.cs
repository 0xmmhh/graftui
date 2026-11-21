using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The words of g's replay on the lane graph: the title while it plays, and row 0 of the sheet, a spinner and the step's words. Pure: a
/// time goes in, words come out. The step times are the animation's (see <see cref="LaneMergeAnimation"/>).
/// </summary>
public static class ReplayText
{
    /// <summary>The title while g plays: "Merging feature → main".</summary>
    public static string Title(MergeCard c)
    {
        return "Merging " + c.Branch + " → " + DigestText.Cur(c);
    }

    /// <summary>The step's words at time t (ms): what git is doing then, in the order the play shows it.</summary>
    public static string Step(MergeCard c, long t)
    {
        if (c.Kind == DigestKind.Merged)
            return MergedStep(c, t);
        if (c.Kind == DigestKind.FastForwarded)
            return FastForwardStep(c, t);
        if (c.Kind == DigestKind.Stopped)
            return StoppedStep(c, t);
        return Checking(c);
    }

    /// <summary>Sheet row 0 during g: a spinner glyph, a space, the step, in one ink: merge, fast-forward or stop.</summary>
    public static IReadOnlyList<Seg> Line(MergeCard c, long t, Glyphs glyphs)
    {
        var spinner = glyphs.Spinner[(int)(t / LaneMergeAnimation.SpinMs % glyphs.Spinner.Length)];
        var text = spinner + " " + Step(c, t);
        var segs = new List<Seg>();
        segs.Add(new Seg(text, new Style(InkOf(c))));
        return segs;
    }

    /// <summary>"app.txt", "a.txt and b.txt", "a.txt and 2 more files": the names through TextFit.Sanitize, "" for none.</summary>
    internal static string FilesPhrase(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return "";
        if (files.Count == 1)
            return TextFit.Sanitize(files[0]);
        if (files.Count == 2)
            return TextFit.Sanitize(files[0]) + " and " + TextFit.Sanitize(files[1]);
        return TextFit.Sanitize(files[0]) + " and " + (files.Count - 1) + " more files";
    }

    // A merge: reading, joining, creating the merge commit, then checking.
    private static string MergedStep(MergeCard c, long t)
    {
        if (t < LaneMergeAnimation.MergePulseEnd)
            return Reading(c);
        if (t < LaneMergeAnimation.MergeTrailEnd)
            return "joining " + c.Branch + " into " + DigestText.Cur(c);
        if (t < LaneMergeAnimation.MergeGhostEnd)
            return "creating the merge commit";
        return Checking(c);
    }

    // A fast-forward: reading, moving the branch forward, the new tip, then checking.
    private static string FastForwardStep(MergeCard c, long t)
    {
        if (t < LaneMergeAnimation.FfPulseEnd)
            return Reading(c);
        if (t < LaneMergeAnimation.FfLiftEnd)
            return "moving " + DigestText.Cur(c) + " forward";
        if (t < LaneMergeAnimation.FfChipEnd)
            return DigestText.Cur(c) + " now points at " + DigestText.Seven(c.Hash);
        return Checking(c);
    }

    // A stop: reading, joining, the conflict, then the command that puts the repo back.
    private static string StoppedStep(MergeCard c, long t)
    {
        if (t < LaneMergeAnimation.ConflictPulseEnd)
            return Reading(c);
        if (t < LaneMergeAnimation.ConflictTrailEnd)
            return "joining " + c.Branch + " into " + DigestText.Cur(c);
        if (t < LaneMergeAnimation.ConflictFlashEnd)
            return ConflictStep(c);
        if (c.Rollback is null)
            return Checking(c);
        return c.Rollback + ": putting " + DigestText.Cur(c) + " back";
    }

    // "reading 3 commits from feature/picks", or "reading feature/x" when the branch brings nothing.
    private static string Reading(MergeCard c)
    {
        if (c.Ahead == 0)
            return "reading " + c.Branch;
        return "reading " + MergeScenes.Plural(c.Ahead, "commit") + " from " + c.Branch;
    }

    // "conflict in app.txt", or "conflict" when git named no file.
    private static string ConflictStep(MergeCard c)
    {
        if (c.ConflictFiles.Count == 0)
            return "conflict";
        return "conflict in " + FilesPhrase(c.ConflictFiles);
    }

    private static string Checking(MergeCard c)
    {
        return "checking " + DigestText.Cur(c);
    }

    // The ink of the line: green for a fast-forward, red for a stop, the merge ink otherwise.
    private static Ink InkOf(MergeCard c)
    {
        if (c.Kind == DigestKind.FastForwarded)
            return Ink.Ok;
        if (c.Kind == DigestKind.Stopped)
            return Ink.Bad;
        return Ink.Mrg;
    }
}
