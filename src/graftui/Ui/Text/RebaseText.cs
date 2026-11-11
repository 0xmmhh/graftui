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

    // ----- the interactive rebase (the todo list) -----

    /// <summary>The title of the todo list's art.</summary>
    public const string TodoTitle = "Interactive rebase";

    /// <summary>The direction line's tail while the todo list is open.</summary>
    public const string TodoTail = "   todo";

    /// <summary>The todo list's caption while nothing is refused.</summary>
    public const string TodoHint = "Top runs first. Enter starts, a backup is saved.";

    /// <summary>Every row is dropped: at least one commit must be kept.</summary>
    public const string KeepOne = "Keep at least one commit, or press Esc to cancel";

    /// <summary>The first kept commit is a squash: nothing is above it to squash into.</summary>
    public const string FirstSquash = "The first kept commit can't be a squash: there is nothing before it to squash into";

    /// <summary>The first kept commit is a fixup: nothing is above it to fold into.</summary>
    public const string FirstFixup = "The first kept commit can't be a fixup: there is nothing before it to fold into";

    /// <summary>
    /// The refusal of i on the current branch: the interactive edit of its own commits is not built yet, so the user picks another branch.
    /// </summary>
    public static string InteractiveOntoSelf(string cur) =>
        $"select another branch to rebase {cur} onto; editing the last commits of {cur} comes later";

    /// <summary>The running caption of a commit that is replayed (a pick, a reword or an edit), with its place in the plan.</summary>
    public static string Replaying(RebaseRow row, int j, int n) => $"Replaying {row.ShortHash} {row.Subject}  ({j}/{n})";

    /// <summary>The running caption of a commit that is folded into the one above it (a squash or a fixup).</summary>
    public static string Squashing(RebaseRow row, int j, int n) => $"Squashing {row.ShortHash} {row.Subject}  ({j}/{n})";

    /// <summary>The running caption of a dropped commit.</summary>
    public static string Dropping(RebaseRow row) => $"Dropping {row.ShortHash} {row.Subject}";

    /// <summary>The headline of an interactive rebase that ran: what was kept, squashed and dropped.</summary>
    public static string InteractiveDone(string cur, int kept, int squashed, int dropped) =>
        $"✔ Rebased {cur}: {kept} kept, {squashed} squashed, {dropped} dropped, backup saved";

    /// <summary>
    /// The done caption of an interactive rebase, with the counts read from the plan. A commit git skipped is not kept; the suffixes for
    /// skipped commits and for a resolved conflict are the same as for a plain rebase.
    /// </summary>
    public static string Interactive(SequenceOutcome o, string cur, IReadOnlyList<RebaseRow> rows)
    {
        var kept = 0;
        var squashed = 0;
        var dropped = 0;
        foreach (var row in rows)
        {
            if (row.Action == RebaseAction.Drop)
                dropped++;
            else if (o.Skipped.Contains(row.Hash))
                continue;
            else if (row.Action == RebaseAction.Squash || row.Action == RebaseAction.Fixup)
                squashed++;
            else
                kept++;
        }

        var text = InteractiveDone(cur, kept, squashed, dropped);
        if (o.Skipped.Count > 0)
            text += $", {MergeScenes.Plural(o.Skipped.Count, "commit")} skipped";
        if (o.Resolved.Count > 0)
            text += " (conflict resolved)";
        return text;
    }

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

    /// <summary>
    /// The name of the "ours" side of a conflict: the onto branch of a rebase, the branch a pick lands on. Stage 2 is this side.
    /// </summary>
    public static string OursName(SequenceStop s)
    {
        if (s.Kind == SequenceKind.Rebase)
            return s.Onto;
        return s.Branch;
    }

    /// <summary>
    /// The name of the "theirs" side of a conflict: the branch a rebase replays, the commit a pick applies. Stage 3 is this side.
    /// </summary>
    public static string TheirsName(SequenceStop s)
    {
        if (s.Kind == SequenceKind.Rebase)
            return s.Branch;
        return s.ShortCommit;
    }

    /// <summary>
    /// One conflicted file's row. A text file reads "✖ app.txt  both modified" until its markers are gone, then "✔ app.txt  resolved". A binary
    /// file, a submodule or a side's add or delete is never resolved by markers: it reads "not resolved" until the user keeps a version (a
    /// submodule is staged in a shell instead).
    /// </summary>
    public static string ConflictRow(ConflictFile f, SequenceStop s)
    {
        if (f.Kind == ConflictKind.Binary)
            return $"✖ {f.Path}  binary · not resolved";
        if (f.Kind == ConflictKind.Submodule)
            return $"✖ {f.Path}  submodule · not resolved";
        if (f.Kind == ConflictKind.AddedOurs)
            return $"✖ {f.Path}  added on {OursName(s)} only · not resolved";
        if (f.Kind == ConflictKind.AddedTheirs)
            return $"✖ {f.Path}  added on {TheirsName(s)} only · not resolved";
        if (f.Kind == ConflictKind.DeletedOurs)
            return $"✖ {f.Path}  deleted on {OursName(s)} · not resolved";
        if (f.Kind == ConflictKind.DeletedTheirs)
            return $"✖ {f.Path}  deleted on {TheirsName(s)} · not resolved";
        if (f.Kind == ConflictKind.DeletedBoth)
            return $"✖ {f.Path}  deleted on both sides · not resolved";
        return f.Resolved ? $"✔ {f.Path}  resolved" : $"✖ {f.Path}  both modified";
    }

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
        {
            var listed = Files(s.Files.Select(f => f.Path).ToList());
            if (HasSubmodule(s.Files))
                return $"✕ CONFLICT in {listed}: stage a submodule in a shell, then Enter continues, or press X to abort";
            if (AllWithoutMarkers(s.Files))
                return $"✕ CONFLICT in {listed}: no conflict markers, so Enter asks before keeping the working tree's version, or press X to abort";
            return $"✕ CONFLICT in {listed}: resolve it in your editor, then Enter continues, or press X to abort";
        }
        if (s.Pause == PauseKind.Edit)
            return EditStop(s);
        return MessagePause(s);
    }

    /// <summary>True when there is a conflicted file and none of them is text (each is binary, a link or an add or delete on one side).</summary>
    private static bool AllWithoutMarkers(IReadOnlyList<ConflictFile> files)
    {
        if (files.Count == 0)
            return false;
        foreach (var f in files)
        {
            if (f.Kind == ConflictKind.Text)
                return false;
        }
        return true;
    }

    /// <summary>True when one of the conflicted files is a submodule.</summary>
    private static bool HasSubmodule(IReadOnlyList<ConflictFile> files)
    {
        foreach (var f in files)
        {
            if (f.Kind == ConflictKind.Submodule)
                return true;
        }
        return false;
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

    /// <summary>The title of the question that asks which version to keep of a file that has no conflict markers.</summary>
    public const string KeepTitle = "Keep without markers?";

    /// <summary>
    /// What the working tree holds, for a question about a file without markers: "holds main's version", "holds your edited version" or
    /// "has it deleted".
    /// </summary>
    public static string TreeText(SequenceStop s, KeptFile k)
    {
        if (k.Side == TreeSide.Ours)
            return $"holds {OursName(s)}'s version";
        if (k.Side == TreeSide.Theirs)
            return $"holds {TheirsName(s)}'s version";
        if (k.Side == TreeSide.Edited)
            return "holds your edited version";
        return "has it deleted";
    }

    /// <summary>
    /// The question for files without markers: which version the working tree holds, and whether to keep it and continue. One file reads
    /// "logo.bin has no conflict markers. The working tree holds main's version. Keep it and continue?"; a deleted one is kept deleted.
    /// </summary>
    public static string KeepText(SequenceStop s, IReadOnlyList<KeptFile> files)
    {
        if (files.Count == 1)
        {
            var k = files[0];
            if (k.Side == TreeSide.Deleted)
                return $"{k.Path} has no conflict markers. The working tree has it deleted. Keep it deleted and continue?";
            return $"{k.Path} has no conflict markers. The working tree {TreeText(s, k)}. Keep it and continue?";
        }
        var parts = files.Select(k => $"{k.Path} {TreeText(s, k)}");
        return $"{MergeScenes.Plural(files.Count, "file")} have no conflict markers: {string.Join("; ", parts)}. Keep them and continue?";
    }

    /// <summary>The question in fewer words, for an art too short for the full one.</summary>
    public static string KeepShort(IReadOnlyList<KeptFile> files) =>
        $"Keep {Files(files.Select(k => k.Path).ToList())} as the working tree holds it and continue?";

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
