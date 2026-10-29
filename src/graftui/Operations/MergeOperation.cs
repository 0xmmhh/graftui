using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Runs a merge safely: checks preconditions, merges, classifies the result structurally
/// (never by parsing localised text) and always aborts a conflicted merge.
/// Stages: Check = Check; Run = preflight + git merge; Verify = Verify; Rollback = Rollback.
/// </summary>
public sealed class MergeOperation(GitClient git) : IGitOperation<MergeRequest, MergeCheck, MergeOutcome>
{
    private static readonly string[] NoFiles = [];

    /// <summary>Preconditions for merging <paramref name="request"/>.Branch into the current branch.</summary>
    public MergeCheck Check(MergeRequest request)
    {
        var branch = request.Branch;
        var log = new List<GitResult>();
        GitResult Run(params string[] a) { var r = git.Run(a); log.Add(r); return r; }

        // symbolic-ref exits 1 (quietly) on a detached HEAD; any other failure is a real error.
        var current = Run("symbolic-ref", "-q", "HEAD");
        var name = GitClient.StripHeads(current.StdOut.Trim());
        if (!current.Success || name.Length == 0)
        {
            var reason = current.ExitCode is 0 or 1 || string.IsNullOrWhiteSpace(current.StdErr)
                ? "HEAD is detached"
                : current.StdErr.Trim();
            return new MergeCheck("", reason, log);
        }

        // `git merge` is called without an option terminator, so a leading '-' would be read as an option.
        if (branch.StartsWith('-'))
            return new MergeCheck(name, "invalid branch name", log);

        if (branch == name)
            return new MergeCheck(name, $"cannot merge {name} into itself", log);

        // Never abort a merge graftui didn't start.
        if (Run("rev-parse", "-q", "--verify", "MERGE_HEAD").Success)
            return new MergeCheck(name, "a merge is already in progress", log);

        // Dirty only when git read the status and it lists changes: a failed status is not a known dirty tree.
        var porcelain = Run("status", "--porcelain");
        if (!porcelain.Success)
            return new MergeCheck(name, "working tree has uncommitted changes", log);
        if (!string.IsNullOrWhiteSpace(porcelain.StdOut))
            return new MergeCheck(name, "working tree has uncommitted changes", log, Dirty: true);

        return new MergeCheck(name, null, log);
    }

    /// <summary>Blocking merge for a worker thread. Never throws. A verified merge saves its undo step.</summary>
    public MergeOutcome Run(MergeRequest request, MergeCheck check) => Run(request, check, saveUndo: true);

    /// <summary>
    /// The merge. With <paramref name="saveUndo"/> false the outcome still carries the verified step, but it is not saved:
    /// the caller (stash-merge-pop) saves one step for the whole operation instead.
    /// </summary>
    internal MergeOutcome Run(MergeRequest request, MergeCheck check, bool saveUndo)
    {
        var branch = request.Branch;
        var into = check.CurrentBranch;
        var log = new List<GitResult>();
        GitResult Run(params string[] a) { var r = git.Run(a); log.Add(r); return r; }
        MergeOutcome Done(MergeKind kind, string? head, IReadOnlyList<string> files, string message, UndoRecord? undo = null)
        {
            // Only a verified merge has an undo step, so only then is one saved. If saving fails, the merge still stands.
            var saved = undo;
            if (undo is not null && saveUndo)
            {
                var problem = new UndoLog(git).Push(undo, log);
                if (problem is not null)
                {
                    message += $"; could not save the undo step: {problem}";
                    saved = null;
                }
            }
            return new(kind, branch, into, head, files, message, log, saved);
        }

        var oldHead = "";
        var mergeRan = false;
        UndoRecord Moved(string head) =>
            new(UndoKind.Merge, $"merge of {branch}", [new RefChange("refs/heads/" + into, oldHead, head)], Branch: into, Head: head);
        try
        {
            var headResult = Run("rev-parse", "-q", "--verify", "HEAD");
            oldHead = headResult.Success ? headResult.StdOut.Trim() : "";
            if (oldHead.Length == 0)
                return Done(MergeKind.Failed, null, NoFiles, "HEAD has no commits yet");

            var tipResult = Run("rev-parse", "--verify", "refs/heads/" + branch + "^{commit}");
            var branchTip = tipResult.StdOut.Trim();
            if (!tipResult.Success || branchTip.Length == 0)
                return Done(MergeKind.Failed, null, NoFiles, $"could not resolve branch {branch}");

            // `git merge refs/heads/x` would record "Merge branch 'refs/heads/x'", so merge by plain name,
            // but refuse if that name resolves to something else (e.g. a tag shadowing the branch).
            var plainTip = Run("rev-parse", "--verify", branch + "^{commit}").StdOut.Trim();
            if (plainTip != branchTip)
                return Done(MergeKind.Failed, null, NoFiles,
                    $"'{branch}' is ambiguous (a tag or other ref shadows the branch); refusing to merge");

            // Re-check right before merging: if anything changed the tree since Check, don't merge, so a
            // later rollback can never be asked to clean up changes that weren't the merge's own.
            var preflight = Run("status", "--porcelain");
            if (!preflight.Success || !string.IsNullOrWhiteSpace(preflight.StdOut))
                return Done(MergeKind.Failed, null, NoFiles,
                    "working tree changed before the merge started; nothing was merged");

            mergeRan = true;
            var merge = Run("merge", "--no-edit", branch);

            // Classifies a merge that finished, by looking at HEAD and its parents (never at git's text).
            MergeOutcome Verify(string suffix)
            {
                // First token is HEAD, the rest are its parents.
                var tokens = Run("rev-list", "--parents", "-n", "1", "HEAD").StdOut
                    .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
                var head = tokens.Length > 0 ? tokens[0] : null;
                if (head is null)
                    return Done(MergeKind.Failed, null, NoFiles, "could not read HEAD after merge" + suffix);
                if (head == oldHead)
                    return Done(MergeKind.UpToDate, head, NoFiles, "already up to date" + suffix);
                if (head == branchTip)
                    return Done(MergeKind.FastForward, head, NoFiles, "fast-forward" + suffix, Moved(head));
                if (tokens.Length >= 3 && tokens[1] == oldHead)
                    return Done(MergeKind.MergeCommit, head, NoFiles, "merge commit created" + suffix, Moved(head));
                return Done(MergeKind.Failed, head, NoFiles, "merge finished with an unexpected result" + suffix);
            }

            if (merge.Success)
                return Verify("");

            var files = Run("diff", "--name-only", "--diff-filter=U").StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            // Signals arrive as 128+N (130 = SIGINT); exactly 128 is git's own die(), not a signal.
            var (leftover, mergeStarted) = Rollback(Run, oldHead, merge.ExitCode > 128);
            if (leftover is not null)
                return Done(MergeKind.Failed, null, files, leftover);

            // The repo is clean and not mid-merge. If the merge had started and HEAD still moved, it did complete
            // even though git exited non-zero (e.g. interrupted just after the commit). If it never started, a
            // moved HEAD came from elsewhere and is not ours to classify.
            var now = Run("rev-parse", "-q", "--verify", "HEAD");
            if (mergeStarted && now.Success && now.StdOut.Trim() != oldHead)
                return Verify($" (git exited {merge.ExitCode} after completing the merge)");

            if (files.Count > 0)
                return Done(MergeKind.Conflict, null, files, "merge aborted");

            var firstErr = merge.StdErr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? $"git merge exited with {merge.ExitCode}";
            return Done(MergeKind.Failed, null, NoFiles, firstErr);
        }
        catch (Exception ex)
        {
            // Before `git merge` ran there is nothing to roll back or to doubt.
            if (!mergeRan)
                return Done(MergeKind.Failed, null, NoFiles, ex.Message);
            string? leftover = null;
            var rollbackFailed = false;
            try { leftover = Rollback(Run, oldHead, false).Leftover; } catch { rollbackFailed = true; }
            var message = leftover is null ? ex.Message : $"{ex.Message}; {leftover}";
            if (rollbackFailed)
                message += "; repo state unknown, run git status";
            return Done(MergeKind.Failed, null, NoFiles, message);
        }
    }

    /// <summary>
    /// Undoes whatever a failed or interrupted merge left behind. Returns null when nothing is left to undo or when the repo is clean and
    /// not mid-merge afterwards, otherwise an accurate description of what is left. MergeStarted tells whether
    /// the merge actually began (MERGE_HEAD present or git killed by a signal).
    /// </summary>
    private static (string? Leftover, bool MergeStarted) Rollback(Func<string[], GitResult> run, string oldHead, bool interrupted)
    {
        // The merge only counts as started if git left MERGE_HEAD behind or was killed mid-run. A normal
        // non-zero exit without MERGE_HEAD is a refusal before anything was touched.
        var mergeStarted = interrupted || run(["rev-parse", "-q", "--verify", "MERGE_HEAD"]).Success;
        AbortIfMerging(run);

        var (dirty, merging) = Inspect(run);
        if (!dirty && !merging)
            return (null, mergeStarted);

        // Nothing of the merge's own to undo: any changes now belong to the user or something else, so never reset them.
        if (!mergeStarted)
            return ("working tree not clean after git merge failed; graftui did not touch it, inspect with git status", false);

        // An interrupted merge (e.g. Ctrl+C while a pre-merge-commit hook runs) can leave staged changes
        // without MERGE_HEAD, which `git merge --abort` cannot undo. `git reset --merge` is only used when
        // the merge actually started (see above) and HEAD is still where it began. The tree was verified
        // clean immediately before `git merge` ran, so changes present then came from the merge, barring
        // something else editing the repo during those few moments.
        if (oldHead.Length == 0)
            return ("starting HEAD unknown; not rolling back, inspect with git status", true);
        var head = run(["rev-parse", "HEAD"]).StdOut.Trim();
        if (head != oldHead)
            return ($"HEAD moved during an interrupted merge ({Short(oldHead)} to {Short(head)}); not rolling back, inspect with git status", true);

        run(["reset", "--merge"]);
        (dirty, merging) = Inspect(run);
        if (merging)
            return ("repo is still mid-merge, run git merge --abort", true);
        if (dirty)
            return ("working tree left dirty by an interrupted merge; inspect with git status", true);
        return (null, true);
    }

    private static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash.Length == 0 ? "unknown" : hash;

    private static (bool Dirty, bool Merging) Inspect(Func<string[], GitResult> run)
    {
        // A failed status means the state is unknown, which is treated as dirty.
        var status = run(["status", "--porcelain"]);
        var dirty = !status.Success || !string.IsNullOrWhiteSpace(status.StdOut);
        var merging = run(["rev-parse", "-q", "--verify", "MERGE_HEAD"]).Success;
        return (dirty, merging);
    }

    private static void AbortIfMerging(Func<string[], GitResult> run)
    {
        if (!run(["rev-parse", "-q", "--verify", "MERGE_HEAD"]).Success)
            return;
        if (!run(["merge", "--abort"]).Success)
            run(["reset", "--merge"]);
    }
}
