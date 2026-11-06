using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>
/// What the screens say about a paused sequence: the pause's caption, the status bar's words, the terminal's exit line, the branch the
/// list marks, and whether a conflicted file has been resolved since the last look. A pause counts only when graftui started it.
/// </summary>
public static class SequenceStatus
{
    private static readonly Style Dim = new(Ink.Dim), Bad = new(Ink.Bad), Warn = new(Ink.Warn);
    private static readonly Style BadB = new(Ink.Bad, true), WarnB = new(Ink.Warn, true);

    /// <summary>True when the snapshot holds a pause that graftui started.</summary>
    public static bool IsPaused(RepoSnapshot s) => s.Paused is not null && s.Paused.Ours;

    /// <summary>The kind of the pause, or null when nothing graftui started is paused.</summary>
    public static PauseKind? KindOf(RepoSnapshot s)
    {
        if (!IsPaused(s))
            return null;
        return s.Paused!.Pause;
    }

    /// <summary>The branch the list marks as paused (it stays marked while HEAD is detached); "" when nothing is paused.</summary>
    public static string PausedBranch(RepoSnapshot s) => IsPaused(s) ? s.Paused!.Branch : "";

    /// <summary>The pause's caption: what is wrong, and which keys go on. A conflict is bad; the other stops are a warning.</summary>
    public static Caption Caption(SequenceStop s) => new(RebaseText.PauseCaption(s), s.Pause == PauseKind.Conflict ? BadB : WarnB);

    /// <summary>
    /// The status bar's right side for a paused sequence: the branch in its colour, its progress, and what the pause waits for.
    /// </summary>
    public static List<Seg> Segs(SequenceStop s, Style branch) =>
    [
        new Seg(s.Branch, branch),
        new Seg(" " + Progress(s), Dim),
        new Seg(" · ", Dim),
        new Seg(RebaseText.PauseWord(s), s.Pause == PauseKind.Conflict ? Bad : Warn),
    ];

    /// <summary>"(rebasing 1/1)" for a rebase, "(picking 1/2)" for a cherry-pick.</summary>
    private static string Progress(SequenceStop s) =>
        s.Kind == SequenceKind.Rebase ? RebaseText.Rebasing(s.Step, s.Total) : $"(picking {s.Step}/{s.Total})";

    /// <summary>The line the terminal prints when a pause is left: the same facts as the screen, and what to run next.</summary>
    public static string ExitLine(SequenceStop s)
    {
        if (s.Kind == SequenceKind.CherryPick)
            return $"graftui: cherry-pick onto {s.Branch} is paused at {s.ShortCommit} ({s.Step}/{s.Total}): run graftui again to continue, skip or abort";
        return $"graftui: rebase of {s.Branch} onto {s.Onto} is paused at {s.ShortCommit} ({s.Step}/{s.Total}): run graftui again to continue, skip or abort";
    }

    /// <summary>
    /// The line the terminal prints when graftui's own sequence is still in progress but its stop cannot be read. The word is "rebase" or
    /// "cherry-pick". Nothing else is offered: the repo is left as it is, and git status shows it.
    /// </summary>
    public static string StillInProgressLine(string word) => $"graftui: a {word} is still in progress: run git status";

    /// <summary>True when any file's resolved flag differs between two reads, or the two reads list different files.</summary>
    public static bool ResolvedChanged(IReadOnlyList<ConflictFile> before, IReadOnlyList<ConflictFile> after)
    {
        if (before.Count != after.Count)
            return true;
        for (var i = 0; i < before.Count; i++)
        {
            if (before[i].Resolved != after[i].Resolved)
                return true;
        }
        return false;
    }
}
