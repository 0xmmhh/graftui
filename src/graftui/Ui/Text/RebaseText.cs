using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The words of the rebase screens. Refusals come from the engine as they are. The words of the pauses (a conflict, an edit, a message)
/// are here too, for the pause screens.
/// </summary>
public static class RebaseText
{
    public const string ConfirmTitle = "Rebase?";
    public const string WillBeRewritten = "will be rewritten";
    public const string NewBase = "new base";
    public const string RunTitle = "Rebasing";
    public const string DoneTitle = "Rebased";
    public const string AbortedTitle = "Aborted";

    /// <summary>The confirm screen's caption. "a backup is saved" (not "first"): the backup is the undo step, saved after the rebase.</summary>
    public static string Confirm(int n) =>
        $"Enter rebases: {MergeScenes.Plural(n, "commit")} {(n == 1 ? "gets" : "get")} new hashes, a backup is saved. Esc cancels.";

    /// <summary>The running line, without its spinner.</summary>
    public static string Running(string cur, string onto) => $"Rebasing {cur} onto {onto}…";

    /// <summary>The done caption: the replayed commits, then what went wrong or was skipped, if anything.</summary>
    public static string Done(SequenceOutcome o, string cur, string onto)
    {
        var text = PlainDone(cur, onto, Replayed(o));
        if (o.Skipped.Count > 0)
            text += $", {MergeScenes.Plural(o.Skipped.Count, "commit")} skipped";
        if (o.Resolved.Count > 0)
            text += " (conflict resolved)";
        return text;
    }

    /// <summary>"✔ Rebased feature/x onto main: 3 commits replayed, old ones kept as backup", with the count given.</summary>
    public static string PlainDone(string cur, string onto, int n) =>
        $"✔ Rebased {cur} onto {onto}: {MergeScenes.Plural(n, "commit")} replayed, old ones kept as backup";

    public static string Aborted(string cur) => $"✕ Rebase aborted: {cur} unchanged";

    /// <summary>The failure when a conflicting rebase could not be aborted: git still holds it, so the user must abort it by hand.</summary>
    public const string NotAborted = "the rebase stopped at a conflict and could not be aborted: run git rebase --abort";

    public static string Failed(string message) => $"✕ Rebase failed: {message}";

    /// <summary>The log line that closes a rebase that was aborted at once (the exit line says the same for the terminal).</summary>
    public static string ExitAborted(string cur, string onto) => $"graftui: rebase of {cur} onto {onto} aborted: {cur} unchanged";

    /// <summary>Rebasing a branch onto itself. The interactive edit of its own commits comes later.</summary>
    public static string OntoItself(string cur) => $"cannot rebase {cur} onto itself";

    /// <summary>The refusal for a branch with no commits of its own on top of the selected one: the engine's own words, as its check says them.</summary>
    public static string NoOwn(string cur, string sel) => SequenceRefusals.NoOwnCommits(cur, sel);

    /// <summary>
    /// The refusal that needs no git call: a fast-forward means the current branch has no commits of its own. Null when the check
    /// should run. The layout is what the preview read; it can be missing when the read failed.
    /// </summary>
    public static string? EarlyRefusal(GraphLayout? layout, string cur, string onto) =>
        layout is not null && layout.Kind == GraphKind.FastForward ? NoOwn(cur, onto) : null;

    /// <summary>True when the rebase ran but its undo step could not be saved. The message then says why.</summary>
    public static bool UndoSaveFailed(SequenceOutcome o) => o.Message.Contains("could not save the undo step", StringComparison.Ordinal);

    /// <summary>The label of a plan action, as git names it.</summary>
    public static string ActionLabel(RebaseAction action)
    {
        if (action == RebaseAction.Reword)
            return "reword";
        if (action == RebaseAction.Edit)
            return "edit";
        if (action == RebaseAction.Squash)
            return "squash";
        if (action == RebaseAction.Fixup)
            return "fixup";
        if (action == RebaseAction.Drop)
            return "drop";
        return "pick";
    }

    /// <summary>The commits that landed: every move with a new commit.</summary>
    private static int Replayed(SequenceOutcome o) => o.Moves.Count(m => m.New is not null);

    // ----- the pauses: the words for each stop, used by the pause screens -----

    /// <summary>The log line for a conflict that stopped the rebase.</summary>
    public static string ConflictLine(SequenceStop stop) =>
        $"Conflict while replaying {stop.ShortCommit} \"{stop.Subject}\" · {Files(stop.Files.Select(f => f.Path).ToList())} changed on both sides";

    public static string EditStop(SequenceStop stop) => $"Stopped at {stop.ShortCommit} to edit · change files, stage them, then Amend & continue";

    public static string MessagePause(SequenceStop stop) => $"Paused at {stop.ShortCommit} for its message · Continue opens it again";

    /// <summary>One conflicted file: "✖ app.txt  both modified" until it is resolved, then "✔ app.txt  resolved".</summary>
    public static string ConflictRow(ConflictFile f) => f.Resolved ? $"✔ {f.Path}  resolved" : $"✖ {f.Path}  both modified";

    /// <summary>"(rebasing 2/3)": the step a paused rebase is on.</summary>
    public static string Rebasing(int step, int total) => $"(rebasing {step}/{total})";

    /// <summary>The title of the paused rebase's screen.</summary>
    public const string PausedTitle = "Paused";

    /// <summary>The title of the question that asks before a paused rebase is aborted.</summary>
    public const string AbortTitle = "Abort the rebase?";

    /// <summary>The caption when the question is answered no, and the pause stays.</summary>
    public const string StillPaused = "Still paused: nothing changed";

    /// <summary>The running caption while a paused rebase is aborted.</summary>
    public const string Aborting = "Aborting the rebase…";

    /// <summary>The question's text: the branch goes back to its old tip. The conflict fixes in the files are lost.</summary>
    public static string AbortText(string branch, string? shortTip) =>
        $"{branch} goes back to {MergeScenes.Short(shortTip)} as it was before. Conflict fixes you made in the files are lost.";

    /// <summary>The direction line's tail while paused: "   paused 2/3".</summary>
    public static string PausedTail(SequenceStop s) => $"   paused {s.Step}/{s.Total}";

    /// <summary>The caption under a paused sequence: what stopped it, and what the keys do.</summary>
    public static string PauseCaption(SequenceStop s)
    {
        if (s.Pause == PauseKind.Conflict)
            return $"✕ CONFLICT in {Files(s.Files.Select(f => f.Path).ToList())}: resolve it in your editor, then Enter continues, or press X to abort";
        if (s.Pause == PauseKind.Edit)
            return EditStop(s);
        return MessagePause(s);
    }

    /// <summary>The status bar's last words for a pause: what it waits for.</summary>
    public static string PauseWord(SequenceStop s)
    {
        if (s.Pause == PauseKind.Conflict)
            return "conflict";
        if (s.Pause == PauseKind.Edit)
            return "editing";
        return "waiting for a message";
    }

    public static string RewordTitle(string shortHash) => $"Reword {shortHash}";

    public const string RewordBody = "Git holds this commit until you confirm its message.";

    public const string RewordHint = "Enter keeps it · Esc cancels";

    public const string CombineTitle = "Combine messages";

    public static string CombineBody(string folded, string target) =>
        $"{folded} folds into {target}. Keep one message for the combined commit.";

    public const string CombineHint = "Enter keeps this message · Esc cancels";

    /// <summary>"app.txt" or "app.txt +2 more", the way the merge names its conflicting files.</summary>
    public static string Files(IReadOnlyList<string> paths) => paths.Count == 0 ? "" : MergeScenes.ConflictFiles(paths);

    /// <summary>Short hashes, joined with commas.</summary>
    public static string Shorts(IEnumerable<string> hashes) => string.Join(", ", hashes.Select(MergeScenes.Short));
}
