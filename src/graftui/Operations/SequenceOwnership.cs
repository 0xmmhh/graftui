using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// The one rule for "a sequence graftui started". graftui's state file is not enough on its own: the file outlives its sequence when the
/// user finishes a paused sequence with plain git, and a later sequence that git starts by hand would then look like graftui's. So git's own
/// files must match the state too. A rebase: git's rebase-merge folder names the state's branch and old tip. A cherry-pick: CHERRY_PICK_HEAD
/// is one of the state's picks. Anything else is not ours. The rule only reads files, so it never runs git and never throws.
/// A rebase that git starts by hand from the same branch and the same tip looks the same to these files; the Check of a new rebase or
/// cherry-pick removes the state file first, so that case only arises when graftui did not run in between.
/// </summary>
internal static class SequenceOwnership
{
    /// <summary>
    /// The state graftui saved for the sequence that git holds, or null when that sequence is not graftui's (or none is held).
    /// <paramref name="inProgress"/> is what <see cref="RepoState.InProgress"/> found: null means no sequence at all.
    /// </summary>
    public static SequenceState? Owned(string gitDir, string? inProgress)
    {
        if (gitDir.Length == 0 || inProgress is null)
            return null;

        var state = SequenceStateFile.Load(gitDir);
        if (state is null)
            return null;

        if (inProgress == "rebase")
            return state.Kind == SequenceKind.Rebase && IsOurRebase(gitDir, state.Branch, state.OldTip) ? state : null;

        if (inProgress == "cherry-pick")
            return state.Kind == SequenceKind.CherryPick && IsOurPick(gitDir, state.Picks, state.OldTip) ? state : null;

        return null;
    }

    /// <summary>
    /// True when git's rebase-merge folder is a rebase on <paramref name="branch"/> that started from <paramref name="oldTip"/>. A rebase
    /// that git keeps in rebase-apply is never ours: graftui starts its rebases with `rebase -i`, which uses rebase-merge.
    /// </summary>
    public static bool IsOurRebase(string gitDir, string branch, string oldTip)
    {
        var folder = Path.Combine(gitDir, "rebase-merge");
        return oldTip.Length > 0
            && ReadLine(Path.Combine(folder, "head-name")) == BranchGit.HeadsPrefix + branch
            && ReadLine(Path.Combine(folder, "orig-head")) == oldTip;
    }

    /// <summary>
    /// True when git's cherry-pick is graftui's. The sequencer folder, when git has one, must have started from <paramref name="oldTip"/>
    /// (its head file). A held pick (CHERRY_PICK_HEAD) must be one of the picks. With no held pick, the sequencer alone is enough: a
    /// multi-commit pick whose current commit was committed by hand has no CHERRY_PICK_HEAD, but the folder still names the old tip.
    /// </summary>
    public static bool IsOurPick(string gitDir, IReadOnlyList<string> picks, string oldTip)
    {
        var sequencer = Path.Combine(gitDir, "sequencer");
        var sequencerHead = Path.Combine(sequencer, "head");
        if (File.Exists(sequencerHead) && ReadLine(sequencerHead) != oldTip)
            return false;

        var head = ReadLine(Path.Combine(gitDir, "CHERRY_PICK_HEAD"));
        if (head.Length > 0)
            return picks.Contains(head);
        return oldTip.Length > 0 && File.Exists(sequencerHead);
    }

    /// <summary>The first line of a small git file, trimmed, or "" when it is missing or cannot be read.</summary>
    private static string ReadLine(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }
}
