using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Undo (u): takes the newest step off the undo history (refs/graftui/undo) and puts the repo back as it was before that step.
/// Check reads the newest step and refuses when undoing would overwrite or lose something: the state the step left behind has
/// changed, an operation is in progress, or files conflict. Each kind has its own guard.
/// Run checks the same step again, runs the kind's actions, verifies the result, and then drops the step. Run never throws.
/// Rollback: a stash that is undone is put back from a copy taken first (WorkState). Every other kind changes only what its
/// guard has shown to be the state it left. A step whose guard fails is refused every time and is never dropped silently.
/// </summary>
public sealed class UndoOperation(GitClient git) : IGitOperation<UndoRequest, UndoCheck, UndoOutcome>
{
    private const string StashRef = "refs/stash";
    private const string UndoPopRescue = "refs/graftui/rescue/undo-pop";
    private const string UndoStashRescue = "refs/graftui/rescue/undo-stash";

    /// <summary>The copies that a failed pop or undo keeps. While one exists, nothing is undone: the user inspects or deletes it first.</summary>
    private static readonly (string Ref, string What)[] KeptCopies =
    [
        (PopOperation.RescueRef, "the state from before an earlier pop"),
        (UndoPopRescue, "the state after a pop, from an undo of it that failed"),
        (UndoStashRescue, "the state from before a stash undo that failed"),
    ];

    private static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    public UndoCheck Check(UndoRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, log);
        }
    }

    public UndoOutcome Run(UndoRequest request, UndoCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun || check.Step is null)
            return Done(UndoOutcomeKind.Failed, check.Step, "", check.Refusal ?? ChangeRefusals.NothingToUndo, log);

        try
        {
            return RunCore(request, check, log);
        }
        catch (Exception ex)
        {
            return Done(UndoOutcomeKind.Failed, check.Step, "", $"{ex.Message}; repo state unknown, run git status", log);
        }
    }

    private UndoCheck CheckCore(List<GitResult> log)
    {
        // 1. The newest step must exist and be readable.
        var (entry, listing) = ReadTop(log);
        if (listing is not null)
            return Refused(listing, log);
        if (entry is null)
            return NothingLeft(log);
        if (entry.Step is null)
            return new UndoCheck(ChangeRefusals.UndoUnreadable(entry.Ref), entry.Ref, entry.Commit, null, "", log);

        // 2. The git dir, then the kind's guard.
        var gitDir = RepoState.GitDir(git, log);
        if (gitDir is null)
            return new UndoCheck("could not find the git dir", entry.Ref, entry.Commit, entry.Step, "", log);

        var refusal = Refusal(entry, gitDir, log);
        return new UndoCheck(refusal, entry.Ref, entry.Commit, entry.Step, gitDir, log);
    }

    private UndoOutcome RunCore(UndoRequest request, UndoCheck check, List<GitResult> log)
    {
        var step = check.Step!;

        // 1. The same step is still on top, and it is the one the user saw.
        var (entry, listing) = ReadTop(log);
        if (listing is not null)
            return Done(UndoOutcomeKind.Failed, step, "", listing, log);
        if (entry is null || entry.Ref != check.StepRef || entry.Commit != check.StepCommit
            || (request.ExpectedStepRef is not null && request.ExpectedStepRef != entry.Ref))
            return Done(UndoOutcomeKind.Failed, step, "", "the undo history changed since you pressed u; nothing was undone", log);

        // 2. The guard again, against the state as it is now.
        var refusal = Refusal(entry, check.GitDir, log);
        if (refusal is not null)
            return Done(UndoOutcomeKind.Failed, step, "", refusal, log);

        // 3. The kind's actions. Each one verifies its own result.
        var (problem, caption) = Apply(step, check.GitDir, log);
        if (problem is not null)
            return Done(UndoOutcomeKind.Failed, step, "", problem, log);

        // 4. The step is dropped only after the undo has been verified. If the drop fails, the undo still stands: the next
        // u refuses the step by its guard.
        var dropped = new UndoLog(git).Drop(entry, log);
        var message = $"undid the {step.What}";
        return Done(UndoOutcomeKind.Undone, step, caption,
            dropped ? message : $"{message}; could not remove the undo step {entry.Ref}, so u will refuse it next time", log);
    }

    /// <summary>What stops the step from being undone now, or null when it may be. Used by Check and again by Run.</summary>
    private string? Refusal(UndoEntry entry, string gitDir, List<GitResult> log)
    {
        var step = entry.Step!;
        if (Malformed(step))
            return ChangeRefusals.UndoUnreadable(entry.Ref);

        var kept = KeptCopy(log);
        if (kept is not null)
            return kept;

        var inProgress = RepoState.InProgress(a => Call(log, a), gitDir);
        if (inProgress is not null)
            return ChangeRefusals.InProgress(inProgress);

        var st = Call(log, StatusArgs);
        if (!st.Success)
            return GitClient.Describe(st);
        var status = StatusParser.Parse(st.StdOut);
        if (status.HasConflicts)
            return ChangeRefusals.Conflicts;

        return step.Kind switch
        {
            UndoKind.Commit or UndoKind.Amend => BranchGuard(step, log),
            UndoKind.Merge => BranchGuard(step, log) ?? (status.Rows.Count > 0 ? ChangeRefusals.UndoDirty : null),
            UndoKind.Discard => DiscardGuard(step, log),
            UndoKind.Stash => StashGuard(step, status, log),
            UndoKind.Pop => PopGuard(step, gitDir, log),
            _ => UnstageGuard(step, log),
        };
    }

    /// <summary>The refusal for the first kept copy that exists, or null when none does (or git fails, which is returned as the reason).</summary>
    private string? KeptCopy(List<GitResult> log)
    {
        foreach (var (reference, what) in KeptCopies)
        {
            var r = Call(log, "rev-parse", "-q", "--verify", reference);
            if (r.Success)
                return ChangeRefusals.RescueSaved(what, reference);
            if (r.ExitCode != 1)
                return GitClient.Describe(r);
        }
        return null;
    }

    /// <summary>
    /// Saves a copy create-only under a rescue ref, so that a failed undo never leaves the only copy of a state behind. The
    /// message names the ref, the commit and how to restore it. Nothing is undone, and the undo step stays on top.
    /// </summary>
    private string SaveCopy(List<GitResult> log, string reference, string commit, string problem)
    {
        var saved = Call(log, "update-ref", reference, commit, "");
        var where = saved.Success
            ? $"your changes are saved in {reference} (commit {commit}): restore them with git restore --source={commit} --worktree -- ., then git update-ref -d {reference}"
            : $"your changes are in commit {commit} ({GitClient.Describe(saved)})";
        return $"{problem}; {where}; the undo step is kept";
    }

    /// <summary>A record that lacks a field its kind needs. Such a record is refused, never run with a null or empty value.</summary>
    private static bool Malformed(UndoRecord s) => s.Kind switch
    {
        UndoKind.Commit or UndoKind.Amend or UndoKind.Merge =>
            s.Changes.Count != 1 || s.Branch is null || s.Changes[0].Ref != "refs/heads/" + s.Branch || s.Changes[0].After.Length == 0,
        UndoKind.Discard =>
            string.IsNullOrEmpty(s.Path) || string.IsNullOrEmpty(s.Backup) || string.IsNullOrEmpty(s.StashCommit)
            || s.StashBelow is null || s.IndexBefore is null,
        UndoKind.Stash =>
            string.IsNullOrEmpty(s.StashCommit) || s.StashBelow is null || string.IsNullOrEmpty(s.Head),
        UndoKind.Pop =>
            string.IsNullOrEmpty(s.StashCommit) || s.StashBelow is null || s.StashMessage is null
            || string.IsNullOrEmpty(s.StateBefore) || string.IsNullOrEmpty(s.StateAfter) || string.IsNullOrEmpty(s.Head),
        _ =>
            s.IndexBefore is null || s.IndexAfter is null || s.IndexBefore.Count == 0 || s.IndexAfter.Count == 0
            || s.IndexBefore.Any(e => e.Path.Length == 0) || s.IndexAfter.Any(e => e.Path.Length == 0),
    };

    /// <summary>Commit, amend and merge: the branch must be the one the step moved, and still at the commit the step left.</summary>
    private string? BranchGuard(UndoRecord s, List<GitResult> log)
    {
        var sym = Call(log, "symbolic-ref", "-q", "HEAD");
        if (!sym.Success || sym.StdOut.Trim() != "refs/heads/" + s.Branch)
            return ChangeRefusals.UndoWrongBranch(s.Branch!);

        var head = RevParse(log, "HEAD");
        if (head is null || head != s.Changes[0].After)
            return ChangeRefusals.UndoHeadMoved(s.Branch!);
        return null;
    }

    /// <summary>
    /// Discard: the stash entry must still hold the backup, and the path must still be as the discard left it: the index entry
    /// is the one it was, the work-tree file matches the index (tracked), or the file is absent (untracked).
    /// </summary>
    private string? DiscardGuard(UndoRecord s, List<GitResult> log)
    {
        var path = s.Path!;
        var position = StashPosition(s.StashCommit!, log);
        if (position is null)
            return "could not read the stash list";
        if (position < 0)
            return ChangeRefusals.UndoStashGone;

        var index = CallLiteral(log, "ls-files", "-s", "-z", "--", path);
        if (!index.Success || !RepoState.ParseIndex(index.StdOut).SequenceEqual(s.IndexBefore!))
            return ChangeRefusals.UndoChanged(path);

        if (s.Untracked)
        {
            if (Exists(Path.Combine(git.RepoPath, path)))
                return ChangeRefusals.UndoChanged(path);
        }
        else if (!CallLiteral(log, "diff", "--quiet", "--", path).Success)
        {
            return ChangeRefusals.UndoChanged(path);
        }
        return null;
    }

    /// <summary>
    /// Stash: the stash ref is the entry the stash made, it sits on HEAD, and the tree is clean. Otherwise the pop would
    /// take the changes out from under newer work.
    /// </summary>
    private string? StashGuard(UndoRecord s, WorkingStatus status, List<GitResult> log)
    {
        if (RevParse(log, StashRef) != s.StashCommit)
            return ChangeRefusals.UndoStashMoved;

        var parent = RevParse(log, s.StashCommit + "^1");
        var head = RevParse(log, "HEAD");
        if (parent is null || head is null || head.Length == 0 || head != parent)
            return ChangeRefusals.UndoHeadMoved(s.Branch ?? "HEAD");

        return status.Rows.Count > 0 ? ChangeRefusals.UndoDirty : null;
    }

    /// <summary>
    /// Pop: the stash ref is where the pop left it, HEAD has not moved, and the work tree and index are still the state
    /// the pop left (the snapshot taken after it).
    /// </summary>
    private string? PopGuard(UndoRecord s, string gitDir, List<GitResult> log)
    {
        if (RevParse(log, StashRef) != s.StashBelow)
            return ChangeRefusals.UndoStashMoved;

        if (RevParse(log, "HEAD") != s.Head)
            return ChangeRefusals.UndoHeadMoved(s.Branch ?? "HEAD");

        var state = new WorkState(git, gitDir);
        var now = state.Take(log);
        var after = state.Load(s.StateAfter!, log);
        return now is null || after is null || now != after ? ChangeRefusals.UndoStateChanged : null;
    }

    /// <summary>Unstage: the listed paths in the index are still the entries the unstage left.</summary>
    private string? UnstageGuard(UndoRecord s, List<GitResult> log)
    {
        var now = RepoState.IndexEntries(git, s.IndexAfter!.Select(e => e.Path), log);
        return now is not null && now.SequenceEqual(s.IndexAfter!) ? null : ChangeRefusals.UndoChanged(s.IndexAfter![0].Path);
    }

    /// <summary>Puts the state back for the step's kind. Returns the problem (null on success) and the caption for the screen.</summary>
    private (string? Problem, string Caption) Apply(UndoRecord s, string gitDir, List<GitResult> log) => s.Kind switch
    {
        UndoKind.Commit or UndoKind.Amend or UndoKind.Merge => MoveBranch(s, log),
        UndoKind.Discard => BringBack(s, gitDir, log),
        UndoKind.Stash => UnstashAgain(s, gitDir, log),
        UndoKind.Pop => PutBack(s, gitDir, log),
        _ => RestoreIndex(s, log),
    };

    /// <summary>
    /// Commit, amend and merge: the branch goes back to the commit it had before the step. A commit keeps its changes staged
    /// (a soft reset); a merge needs a clean tree, and its reset keeps the work tree.
    /// </summary>
    private (string? Problem, string Caption) MoveBranch(UndoRecord s, List<GitResult> log)
    {
        var change = s.Changes[0];
        var before = change.Before;
        var branch = s.Branch!;
        var indexTree = Hash(Call(log, "write-tree"));
        if (indexTree is null)
            return ("could not read the index; nothing was undone", "");

        GitResult move;
        if (before.Length == 0)
            move = Call(log, "update-ref", "-d", "refs/heads/" + branch, change.After);
        else if (s.Kind == UndoKind.Merge)
            move = Call(log, "reset", "-q", "--keep", before);
        else
            move = Call(log, "reset", "-q", "--soft", before);
        if (!move.Success)
            return ($"{GitClient.Describe(move)}; nothing was undone", "");

        var head = RevParse(log, "HEAD");
        if (head is null || head != before)
            return ("HEAD is not where the undo should have put it; check git status", "");
        // A commit's undo keeps the index as it was. A merge's undo resets the index to the commit it goes back to.
        var expectedIndex = s.Kind == UndoKind.Merge ? Hash(Call(log, "rev-parse", "-q", "--verify", before + "^{tree}")) : indexTree;
        if (Hash(Call(log, "write-tree")) != expectedIndex)
            return ("the index is not what the undo should have left; check git status", "");
        if (s.Kind == UndoKind.Merge && RevParse(log, "MERGE_HEAD") != "")
            return ("a merge is still in progress after the undo", "");

        var caption = before.Length == 0
            ? $"↶ Undid the {s.What}: {branch} has no commits again, and its changes are staged"
            : s.Kind == UndoKind.Commit
                ? $"↶ Undid the commit: {branch} is back at {Short(before)} and its changes are staged again"
                : $"↶ Undid the {s.What}: {branch} is back at {Short(before)}";
        return (null, caption);
    }

    /// <summary>
    /// Discard: the backup's version of the file goes back into the work tree (or the file is removed again when the backup
    /// has none), and the stash entry is dropped. The index is never touched.
    /// </summary>
    private (string? Problem, string Caption) BringBack(UndoRecord s, string gitDir, List<GitResult> log)
    {
        var path = s.Path!;
        var backup = s.Backup!;
        var full = Path.Combine(git.RepoPath, path);

        // 1. The file. A tracked file that the backup lacks was deleted before the discard; the guard showed it matches the
        // index, and the index keeps its content, so removing it loses nothing.
        var inBackup = s.Untracked || Call(log, "rev-parse", "-q", "--verify", backup + ":" + path).Success;
        if (inBackup)
        {
            var put = CallLiteral(log, "restore", "-q", "--source=" + backup, "--worktree", "--", path);
            if (!put.Success)
                return ($"{GitClient.Describe(put)}; nothing was brought back", "");
        }
        else
        {
            if (Directory.Exists(full))
                return ($"{path} is a folder now; nothing was brought back", "");
            if (File.Exists(full))
                File.Delete(full);
        }

        // 2. The stash entry, which must still be where the guard saw it.
        var position = StashPosition(s.StashCommit!, log);
        if (position is null or < 0 || RevParse(log, $"stash@{{{position}}}") != s.StashCommit)
            return ($"{path} was brought back, but its stash entry moved, so the entry was left alone", "");
        var drop = Call(log, "stash", "drop", "-q", $"stash@{{{position}}}");
        if (!drop.Success)
            return ($"{path} was brought back, but the stash entry could not be dropped: {GitClient.Describe(drop)}", "");

        // 3. Verify: the file is the backup's version, its index entry is as it was, and the entry is gone.
        var state = new WorkState(git, gitDir);
        var rebuilt = state.PathTree(path, s.Untracked, log);
        var backupTree = Hash(Call(log, "rev-parse", "-q", "--verify", backup + "^{tree}"));
        var fromRebuilt = rebuilt is null ? null : PathEntry(rebuilt, path, log);
        var fromBackup = backupTree is null ? null : PathEntry(backupTree, path, log);
        if (fromRebuilt is null || fromRebuilt != fromBackup)
            return ($"{path} does not match the saved change after the undo; check git status", "");
        var index = CallLiteral(log, "ls-files", "-s", "-z", "--", path);
        if (!index.Success || !RepoState.ParseIndex(index.StdOut).SequenceEqual(s.IndexBefore!))
            return ($"the index entry for {path} changed during the undo", "");
        if (StashPosition(s.StashCommit!, log) != -1)
            return ("the stash entry is still listed after the undo", "");

        return (null, $"↶ Brought back your changes to {path}");
    }

    /// <summary>
    /// Stash: `git stash pop --index` brings back the entry that the stash made. If git cannot apply it cleanly, the state from
    /// before the undo is put back from a copy, and the stash entry is kept.
    /// </summary>
    private (string? Problem, string Caption) UnstashAgain(UndoRecord s, string gitDir, List<GitResult> log)
    {
        var state = new WorkState(git, gitDir);
        var head = RevParse(log, "HEAD");
        var before = state.Take(log);
        if (before is null || string.IsNullOrEmpty(head))
            return ("could not read the working tree; nothing was undone", "");
        var copy = state.Save(before, head, log);
        if (copy is null)
            return ("could not save the working tree; nothing was undone", "");

        // Create-only, before git moves anything: a copy kept by an earlier failed undo is never replaced.
        var saved = Call(log, "update-ref", UndoStashRescue, copy, "");
        if (!saved.Success)
            return ($"could not save a safety copy ({GitClient.Describe(saved)}); nothing was undone", "");

        var pop = Call(log, "stash", "pop", "-q", "--index");
        if (!pop.Success)
        {
            if (state.Take(log) == before)
            {
                Call(log, "update-ref", "-d", UndoStashRescue, copy);
                return ($"git refused to bring the stash back ({FirstLine(pop)}); nothing changed, the entry is kept", "");
            }

            var removable = StashUntracked(s.StashCommit!, log);
            var problem = removable is null ? "could not list the stash's untracked files" : state.RestoreTo(before, copy, removable, log);
            if (problem is not null)
                return (SaveCopy(log, UndoStashRescue, copy, problem), "");
            Call(log, "update-ref", "-d", UndoStashRescue, copy);
            return ($"git stash pop failed ({FirstLine(pop)}); the state was put back and the stash entry is kept", "");
        }

        // Verify: the stash list is as it was before the stash, and the index is the stash's index. The copy is kept if not.
        if (RevParse(log, StashRef) != s.StashBelow)
            return (SaveCopy(log, UndoStashRescue, copy, "the stash list is not as expected after the undo; check git stash list"), "");
        var indexTree = Hash(Call(log, "write-tree"));
        var stashIndex = Hash(Call(log, "rev-parse", "-q", "--verify", s.StashCommit + "^2^{tree}"));
        if (indexTree is null || indexTree != stashIndex)
            return (SaveCopy(log, UndoStashRescue, copy, "the index is not the stash's index after the undo; check git status"), "");

        var st = Call(log, StatusArgs);
        if (!st.Success)
            return (SaveCopy(log, UndoStashRescue, copy, "could not read the status after the undo: " + GitClient.Describe(st)), "");
        Call(log, "update-ref", "-d", UndoStashRescue, copy);
        var count = StatusParser.Parse(st.StdOut).ChangedPaths;
        return (null, $"↶ Unstashed {count} {(count == 1 ? "file" : "files")}");
    }

    /// <summary>
    /// Pop: the state from before the pop is put back exactly, and the stash entry is stored again with its old message.
    /// Only the files that the pop left are removed, and only when the guard showed the state is still the one after the pop.
    /// </summary>
    private (string? Problem, string Caption) PutBack(UndoRecord s, string gitDir, List<GitResult> log)
    {
        var state = new WorkState(git, gitDir);
        var before = state.Load(s.StateBefore!, log);
        var after = state.Load(s.StateAfter!, log);
        if (before is null || after is null)
            return ("the saved states are missing; nothing was undone", "");

        // The files the pop left behind are the ones in the snapshot that are not in its index.
        var everything = ListFiles(after.AllTree, log);
        var tracked = ListFiles(after.IndexTree, log);
        if (everything is null || tracked is null)
            return ("could not list the files of the saved state; nothing was undone", "");
        var untracked = everything.Where(p => !tracked.Contains(p)).ToHashSet();

        var problem = state.RestoreTo(before, s.StateBefore!, untracked, log);
        if (problem is not null)
            return (SaveCopy(log, UndoPopRescue, s.StateAfter!, problem), "");

        var store = Call(log, "stash", "store", "-m", s.StashMessage!, s.StashCommit!);
        if (!store.Success)
            return ($"the working tree is back, but the stash entry could not be stored again: {GitClient.Describe(store)}", "");
        if (RevParse(log, StashRef) != s.StashCommit)
            return ("the working tree is back, but the stash entry is not the one the pop took; check git stash list", "");

        return (null, "↶ Put the changes back into the stash");
    }

    /// <summary>Unstage: the index entries that the unstage changed go back. The work tree is not touched.</summary>
    private (string? Problem, string Caption) RestoreIndex(UndoRecord s, List<GitResult> log)
    {
        foreach (var e in s.IndexBefore!)
        {
            var r = e.Mode.Length > 0
                ? Call(log, "update-index", "--add", "--cacheinfo", $"{e.Mode},{e.Blob},{e.Path}")
                : Call(log, "update-index", "--force-remove", "--", e.Path);
            if (!r.Success)
                return ($"{GitClient.Describe(r)}; the index may be partly restored", "");
        }
        // Exit 1 means some entries still need a refresh; the comparison below decides whether the index is right.
        Call(log, "update-index", "-q", "--refresh");

        var now = RepoState.IndexEntries(git, s.IndexBefore!.Select(e => e.Path), log);
        if (now is null || !now.SequenceEqual(s.IndexBefore!))
            return ("the index is not what it was before the unstage; check git status", "");

        var paths = s.IndexBefore!.Select(e => e.Path).Distinct().ToList();
        return (null, paths.Count == 1
            ? $"↶ Restored the hand-picked staged changes to {paths[0]}"
            : $"↶ Restored the hand-picked staged changes to {paths.Count} files");
    }

    /// <summary>The paths of a stash's untracked files (its third parent), or an empty set when there are none. Null when git fails.</summary>
    private HashSet<string>? StashUntracked(string stash, List<GitResult> log)
    {
        var parent = RevParse(log, stash + "^3");
        if (string.IsNullOrEmpty(parent))
            return [];
        return ListFiles(parent, log);
    }

    /// <summary>The newest step, or the refusal when the history cannot be listed.</summary>
    private (UndoEntry? Entry, string? Listing) ReadTop(List<GitResult> log)
    {
        try
        {
            return (new UndoLog(git).Top(log), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ChangeRefusals.UndoListFailed(ex.Message));
        }
    }

    /// <summary>The paths in a tree, or null when git fails.</summary>
    private HashSet<string>? ListFiles(string tree, List<GitResult> log)
    {
        var r = Call(log, "ls-tree", "-r", "-z", "--name-only", tree);
        return r.Success ? r.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet() : null;
    }

    /// <summary>The ls-tree line for one path in a tree (empty when the tree has no such path), or null when git fails.</summary>
    private string? PathEntry(string tree, string path, List<GitResult> log)
    {
        var r = CallLiteral(log, "ls-tree", "-z", tree, "--", path);
        return r.Success ? r.StdOut : null;
    }

    /// <summary>The position of a stash commit in the stash list (0 is the top), -1 when it is not listed, or null when git fails.</summary>
    private int? StashPosition(string commit, List<GitResult> log)
    {
        var r = Call(log, "stash", "list", "--format=%H");
        if (!r.Success)
            return null;
        var hashes = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Array.IndexOf(hashes, commit);
    }

    /// <summary>The full hash that a name resolves to, "" when it does not exist, or null when git fails.</summary>
    private string? RevParse(List<GitResult> log, string name)
    {
        var r = Call(log, "rev-parse", "-q", "--verify", name);
        if (r.Success)
            return r.StdOut.Trim();
        return r.ExitCode == 1 ? "" : null;
    }

    private static bool Exists(string path)
    {
        var info = new FileInfo(path);
        return File.Exists(path) || Directory.Exists(path) || info.LinkTarget is not null;
    }

    private static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash;

    private static string? Hash(GitResult r)
    {
        var hash = r.StdOut.Trim();
        return r.Success && hash.Length > 0 ? hash : null;
    }

    private static string FirstLine(GitResult r)
    {
        var lines = r.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault() ?? $"git exited with {r.ExitCode}";
    }

    private static UndoCheck Refused(string reason, List<GitResult> log) =>
        new(reason, null, null, null, "", log);

    private static UndoCheck NothingLeft(List<GitResult> log) =>
        new(ChangeRefusals.NothingToUndo, null, null, null, "", log, NothingToUndo: true);

    private static UndoOutcome Done(UndoOutcomeKind kind, UndoRecord? step, string caption, string message, List<GitResult> log) =>
        new(kind, step, caption, message, log);

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private GitResult CallLiteral(List<GitResult> log, params string[] args)
    {
        var r = git.RunLiteral(args);
        log.Add(r);
        return r;
    }
}
