using graftui.Git;
using graftui.Model;
using static graftui.Operations.BranchGit;

namespace graftui.Operations;

/// <summary>
/// Stash, merge, pop (m over uncommitted changes, after the user says yes). The whole tree is stashed under a message that
/// names the merge, the branch is merged, then the stash is popped back with `git stash pop --index` (staged stays staged).
/// A copy of the whole state from before is kept under <see cref="RescueRef"/> until the operation is settled.
/// Outcomes: Merged (the merge stands, the changes are back, one StashMerge undo step is saved); MergeConflict (the merge was
/// aborted and the stash popped back, so the repo is as it was); PopConflict (the merge stands, the pop could not apply, the
/// changes stay in the stash, and a plain Merge undo step is saved); Failed (the message says where the changes are).
/// Check refuses what the merge and the stash refuse, and a clean tree (the plain merge does that). Run never throws.
/// </summary>
public sealed class StashMergeOperation(GitClient git) : IGitOperation<StashMergeRequest, StashMergeCheck, StashMergeOutcome>
{
    public const string RescueRef = "refs/graftui/rescue/stash-merge";
    private const string StashRef = "refs/stash";

    public StashMergeCheck Check(StashMergeRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request.Branch, log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, log);
        }
    }

    public StashMergeOutcome Run(StashMergeRequest request, StashMergeCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Done(StashMergeKind.Failed, null, [], check.Refusal ?? "nothing was merged", log);

        try
        {
            return RunCore(request, check, log);
        }
        catch (Exception ex)
        {
            return Done(StashMergeKind.Failed, null, [], $"{ex.Message}; repo state unknown, run git status and git stash list", log);
        }
    }

    private StashMergeCheck CheckCore(string branch, List<GitResult> log)
    {
        // 0. The copy from an earlier stash-merge-pop is kept until the user deals with it.
        var rescue = Call(log, "rev-parse", "-q", "--verify", RescueRef);
        if (rescue.Success)
            return Refused(ChangeRefusals.RescueSaved("your state from before an earlier stash-merge-pop", RescueRef), log);
        if (rescue.ExitCode != 1)
            return Refused(GitClient.Describe(rescue), log);

        // 1. The merge needs a branch to merge into.
        if (SymbolicHead(git, log).Length == 0)
            return Refused(ChangeRefusals.Detached, log);

        // 2. The merge's own preconditions. Only a dirty tree gets past them; a clean one is refused (the plain merge does it).
        var merge = new MergeOperation(git).Check(new MergeRequest(branch));
        log.AddRange(merge.Commands);
        if (!merge.Dirty)
            return Refused(merge.Refusal ?? BranchRefusals.NothingToStashForMerge, log);

        // 3. A commit must exist, the git dir must be known, and no other operation may own the index.
        var head = RefOrEmpty(git, log, "HEAD");
        if (head.Length == 0)
            return Refused(ChangeRefusals.NoCommitYet, log);
        var gitDir = RepoState.GitDir(git, log);
        if (gitDir is null)
            return Refused("could not find the git dir", log);
        var inProgress = RepoState.InProgress(a => Call(log, a), gitDir);
        if (inProgress is not null)
            return Refused(ChangeRefusals.InProgress(inProgress), log);

        // 4. The tree must be one that the stash can take (conflicts, nested repos, submodules, intent-to-add refuse).
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Refused(GitClient.Describe(st), log);
        var status = StatusParser.Parse(st.StdOut);
        var refusal = StashOperation.Refusal(status);
        if (refusal is not null)
            return Refused(refusal, log);

        // 5. The branch must exist, and must have something HEAD lacks.
        if (RefOrEmpty(git, log, HeadsPrefix + branch).Length == 0)
            return Refused(BranchRefusals.NoBranch(branch), log);
        if (IsAncestor(git, log, HeadsPrefix + branch, "HEAD"))
            return Refused(BranchRefusals.UpToDate(branch, merge.CurrentBranch), log);

        return new StashMergeCheck(null, merge.CurrentBranch, head, gitDir, status.ChangedPaths, log);
    }

    private StashMergeOutcome RunCore(StashMergeRequest request, StashMergeCheck check, List<GitResult> log)
    {
        var branch = request.Branch;
        var into = check.Into;
        var head = check.Head;
        var state = new WorkState(git, check.GitDir);

        // 1. Still on the branch the user saw, at the commit the check saw.
        if (SymbolicHead(git, log) != HeadsPrefix + into || RefOrEmpty(git, log, "HEAD") != head)
            return Done(StashMergeKind.Failed, null, [], "HEAD moved since the check; nothing changed", log);

        // 2. Save the whole state. The copy is create-only, so a copy kept by an earlier operation is never replaced.
        var before = state.Take(log);
        if (before is null)
            return Done(StashMergeKind.Failed, null, [], "could not read the working tree; nothing changed", log);
        var copy = state.Save(before, head, log);
        if (copy is null)
            return Done(StashMergeKind.Failed, null, [], "could not save the working tree; nothing changed", log);
        var saved = Call(log, "update-ref", RescueRef, copy, "");
        if (!saved.Success)
            return Done(StashMergeKind.Failed, null, [], $"could not save a safety copy ({GitClient.Describe(saved)}); nothing changed", log);

        // Keep: the copy stays, and the message says where it is. Untouched: nothing has been stashed yet, so the copy is dropped
        // unless the state is not the one it saved.
        string Keep(string message) => $"{message}; your state from before is saved as {RescueRef}";
        StashMergeOutcome Untouched(string message)
        {
            if (state.Take(log) == before)
            {
                Call(log, "update-ref", "-d", RescueRef, copy);
                return Done(StashMergeKind.Failed, null, [], message, log);
            }
            return Done(StashMergeKind.Failed, null, [], Keep(message), log);
        }

        // 3. Stash everything, under the message that names the merge (it stays in the stash list only if the pop fails).
        var below = RevParse(log, StashRef);
        if (below is null)
            return Untouched("could not read the stash; nothing was stashed");
        var push = Call(log, "stash", "push", "-u", "-m", $"graftui: before merging {branch} into {into}");
        var top = RevParse(log, StashRef);
        if (top is null)
            return Done(StashMergeKind.Failed, null, [],
                Keep("could not read the stash after the stash push; your changes may be in stash@{0}"), log);
        if (top == below)
            return Untouched((push.Success ? "nothing was stashed" : FirstLine(push) + "; nothing was stashed"));

        // 4. The entry sits on HEAD and the tree is clean. Otherwise the changes may still be in the tree, so nothing is merged.
        var parent = top.Length == 0 ? null : RevParse(log, top + "^1");
        if (parent != head)
            return Done(StashMergeKind.Failed, null, [],
                Keep("the stash entry was made, but it is not on top of HEAD; check git stash list"), log);
        var status = Call(log, StatusArgs);
        if (!status.Success)
            return Done(StashMergeKind.Failed, null, [],
                Keep($"your changes were saved in stash@{{0}}; could not read the status: {GitClient.Describe(status)}"), log);
        if (StatusParser.Parse(status.StdOut).Rows.Count > 0)
            return Done(StashMergeKind.Failed, null, [],
                Keep("your changes were saved in stash@{0} and may also still be in the working tree"), log);

        // 5. The merge. Its undo step is not saved here: the operation saves one step once the pop has settled.
        var merged = new MergeOperation(git).Run(new MergeRequest(branch), new MergeCheck(into, null, []), saveUndo: false);
        log.AddRange(merged.Commands);
        var merge = merged with { Undo = null };

        // 6a. The merge moved the branch: pop the stash back with its index.
        if (merged.Kind is MergeKind.FastForward or MergeKind.MergeCommit && merged.NewHead is { Length: > 0 } newHead)
            return PopAfterMerge(branch, into, head, newHead, top, below, copy, state, merged, merge, log);

        // 6b. The merge did not move the branch. If it left the tree as it was, the pop puts the changes back, with their index.
        var headNow = RevParse(log, "HEAD");
        var statusNow = Call(log, StatusArgs);
        var mergeHeadAbsent = Call(log, "rev-parse", "-q", "--verify", "MERGE_HEAD").ExitCode == 1;
        var clean = statusNow.Success && StatusParser.Parse(statusNow.StdOut).Rows.Count == 0;
        if (headNow != head || !clean || !mergeHeadAbsent)
            return Done(StashMergeKind.Failed, merge, [], Keep($"{merged.Message}; your changes are in stash@{{0}}"), log);

        var pop = Call(log, "stash", "pop", "--index");
        var nowState = state.Take(log);
        var stashAfter = RevParse(log, StashRef);
        if (pop.Success && nowState == before && stashAfter == below)
        {
            Call(log, "update-ref", "-d", RescueRef, copy);
            return BackOutcome(merged, merge, log);
        }

        // The pop did not put the state back exactly. The copy's state is restored, and the entry (whose changes are then in
        // the tree again) is dropped, so the stash list is as it was. The entry must still be the one the stash made.
        var untracked = StashUntracked(top, log);
        if (untracked is null)
            return Done(StashMergeKind.Failed, merge, [], Keep("could not list the stash's untracked files; your changes are in stash@{0}"), log);
        if (nowState != before)
        {
            var problem = state.RestoreTo(before, copy, untracked, log);
            if (problem is not null)
                return Done(StashMergeKind.Failed, merge, [], Keep($"{problem}; your changes are in stash@{{0}}"), log);
        }
        if (RevParse(log, StashRef) != top)
            return Done(StashMergeKind.Failed, merge, [], Keep("the stash entry moved, so it was left alone; your changes are in the stash"), log);
        var drop = Call(log, "stash", "drop", "-q", "stash@{0}");
        if (!drop.Success)
            return Done(StashMergeKind.Failed, merge, [],
                Keep($"the state is back, but the stash entry could not be dropped ({GitClient.Describe(drop)})"), log);
        if (state.Take(log) != before || RevParse(log, StashRef) != below)
            return Done(StashMergeKind.Failed, merge, [], Keep("the state is not as it was before the merge; check git status"), log);

        Call(log, "update-ref", "-d", RescueRef, copy);
        return BackOutcome(merged, merge, log);
    }

    /// <summary>The merge moved the branch: the stash is popped with its index. A pop that applies cleanly saves the StashMerge step.</summary>
    private StashMergeOutcome PopAfterMerge(string branch, string into, string head, string newHead, string top, string below, string copy,
        WorkState state, MergeOutcome merged, MergeOutcome merge, List<GitResult> log)
    {
        string Keep(string message) => $"{message}; your state from before is saved as {RescueRef}";

        var pop = Call(log, "stash", "pop", "--index");
        if (pop.Success)
        {
            var st = Call(log, StatusArgs);
            var listed = Call(log, "stash", "list", "--format=%H");
            if (!st.Success || !listed.Success)
                return Done(StashMergeKind.Failed, merge, [], Keep("the merge stands and the pop ran, but its result could not be read"), log);
            if (StatusParser.Parse(st.StdOut).HasConflicts || listed.StdOut.Split('\n').Any(h => h.Trim() == top))
                return Done(StashMergeKind.Failed, merge, [],
                    Keep("the merge stands and the pop ran, but its result is not as expected; check git status and git stash list"), log);

            var afterState = state.Take(log);
            var afterCommit = afterState is null ? null : state.Save(afterState, newHead, log);
            if (afterCommit is null)
                return Done(StashMergeKind.Merged, merge, [],
                    Keep($"merged {branch} into {into}; your changes are back, but the undo step could not be made"), log);

            var undo = new UndoRecord(UndoKind.StashMerge, $"merge of {branch} over your changes", [new RefChange(HeadsPrefix + into, head, newHead)],
                Branch: into, Head: newHead, StashCommit: top, StashBelow: below, StateBefore: copy, StateAfter: afterCommit);
            var problem = new UndoLog(git).Push(undo, log);
            if (problem is not null)
                return Done(StashMergeKind.Merged, merge, [],
                    Keep($"merged {branch} into {into}; your changes are back, but the undo step could not be saved ({problem})"), log);

            Call(log, "update-ref", "-d", RescueRef, copy);
            return Done(StashMergeKind.Merged, merge, [], $"merged {branch} into {into} ({merged.Message}); your changes are back", log, undo);
        }

        // The pop conflicted, or git refused it. The merge stands. The tree is the merge's own (it was clean before the pop), so the
        // files the pop wrote are reset to it. The stash entry is checked first: it holds the changes and must still be there.
        var files = Call(log, "diff", "--name-only", "-z", "--diff-filter=U");
        var conflicts = files.Success ? Split(files.StdOut) : [];
        var mergedTree = RevParse(log, newHead + "^{tree}");
        if (string.IsNullOrEmpty(mergedTree))
            return Done(StashMergeKind.Failed, merge, conflicts, Keep("the merge stands, but the pop's result could not be read; your changes are in stash@{0}"), log);
        if (RevParse(log, StashRef) != top)
            return Done(StashMergeKind.Failed, merge, conflicts, Keep("the pop failed and the stash entry is not the one it took; check git stash list"), log);

        var mergedSnapshot = new WorkSnapshot(mergedTree, mergedTree);
        if (state.Take(log) != mergedSnapshot)
        {
            var removable = StashUntracked(top, log);
            var problem = removable is null
                ? "could not list the stash's untracked files"
                : state.RestoreTo(mergedSnapshot, newHead, removable, log);
            if (problem is not null)
                return Done(StashMergeKind.Failed, merge, conflicts, Keep($"the merge stands, but the pop could not be undone ({problem}); your changes are in stash@{{0}}"), log);
        }

        if (merged.Undo is not { } mergeStep)
            return Done(StashMergeKind.Failed, merge, conflicts, Keep("the merge stands, but its undo step is missing; your changes are in stash@{0}"), log);
        var saved = new UndoLog(git).Push(mergeStep, log);
        if (saved is not null)
            return Done(StashMergeKind.PopConflict, merge, conflicts,
                Keep($"merged, but the pop did not apply and its undo step could not be saved ({saved}); your changes are in stash@{{0}}"), log);

        Call(log, "update-ref", "-d", RescueRef, copy);
        var message = conflicts.Length > 0
            ? $"merged, but the pop conflicted in {conflicts[0]}: changes kept in the stash"
            : $"merged, but the pop could not be applied ({FirstLine(pop)}): changes kept in the stash";
        return Done(StashMergeKind.PopConflict, merge, conflicts, message, log, mergeStep);
    }

    /// <summary>The outcome when the state is back as it was: the merge's own result, with the changes back.</summary>
    private static StashMergeOutcome BackOutcome(MergeOutcome merged, MergeOutcome merge, List<GitResult> log) => merged.Kind switch
    {
        MergeKind.Conflict => Done(StashMergeKind.MergeConflict, merge, merged.ConflictFiles, "merge aborted; your changes are back as they were", log),
        MergeKind.UpToDate => Done(StashMergeKind.Merged, merge, [], "already up to date; your changes are back", log),
        _ => Done(StashMergeKind.Failed, merge, [], $"merge failed: {merged.Message}; your changes are back", log),
    };

    /// <summary>The untracked paths a stash carries (its third parent): none when it has none. Null when git fails.</summary>
    private HashSet<string>? StashUntracked(string stash, List<GitResult> log)
    {
        var parent = RevParse(log, stash + "^3");
        if (parent is null)
            return null;
        if (parent.Length == 0)
            return [];
        var listed = Call(log, "ls-tree", "-r", "-z", "--name-only", parent);
        return listed.Success ? Split(listed.StdOut).ToHashSet() : null;
    }

    /// <summary>The full hash that a name resolves to, "" when it does not exist (exit 1), or null when git fails.</summary>
    private string? RevParse(List<GitResult> log, string name)
    {
        var r = Call(log, "rev-parse", "-q", "--verify", name);
        if (r.Success)
            return r.StdOut.Trim();
        return r.ExitCode == 1 ? "" : null;
    }

    private static string[] Split(string nulSeparated) =>
        nulSeparated.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static StashMergeCheck Refused(string reason, List<GitResult> log) =>
        new(reason, "", "", "", 0, log);

    private static StashMergeOutcome Done(StashMergeKind kind, MergeOutcome? merge, IReadOnlyList<string> files, string message,
        IReadOnlyList<GitResult> log, UndoRecord? undo = null) =>
        new(kind, merge, files, message, log, undo);

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }
}
