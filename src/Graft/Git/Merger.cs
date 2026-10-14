namespace Graft.Git;

/// <summary>
/// Runs a merge safely: checks preconditions, merges, classifies the result structurally
/// (never by parsing localised text) and always aborts a conflicted merge.
/// </summary>
public sealed class Merger(GitClient git)
{
    private static readonly string[] NoFiles = [];

    /// <summary>Preconditions for merging <paramref name="branch"/> into the current branch.</summary>
    public MergeCheck Check(string branch)
    {
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

        // Never abort a merge graft didn't start.
        if (Run("rev-parse", "-q", "--verify", "MERGE_HEAD").Success)
            return new MergeCheck(name, "a merge is already in progress", log);

        var porcelain = Run("status", "--porcelain");
        if (!porcelain.Success || !string.IsNullOrWhiteSpace(porcelain.StdOut))
            return new MergeCheck(name, "working tree has uncommitted changes", log);

        return new MergeCheck(name, null, log);
    }

    /// <summary>Blocking merge for a worker thread. Never throws.</summary>
    public MergeOutcome Merge(string branch, string into)
    {
        var log = new List<GitResult>();
        GitResult Run(params string[] a) { var r = git.Run(a); log.Add(r); return r; }
        MergeOutcome Done(MergeKind kind, string? head, IReadOnlyList<string> files, string message) =>
            new(kind, branch, into, head, files, message, log);

        var oldHead = "";
        try
        {
            oldHead = Run("rev-parse", "HEAD").StdOut.Trim();
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

            var merge = Run("merge", "--no-edit", branch);

            if (merge.Success)
            {
                // First token is HEAD, the rest are its parents.
                var tokens = Run("rev-list", "--parents", "-n", "1", "HEAD").StdOut
                    .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
                var head = tokens.Length > 0 ? tokens[0] : null;
                if (head is null)
                    return Done(MergeKind.Failed, null, NoFiles, "could not read HEAD after merge");
                if (head == oldHead)
                    return Done(MergeKind.UpToDate, head, NoFiles, "already up to date");
                if (head == branchTip)
                    return Done(MergeKind.FastForward, head, NoFiles, "fast-forward");
                if (tokens.Length >= 3 && tokens[1] == oldHead)
                    return Done(MergeKind.MergeCommit, head, NoFiles, "merge commit created");
                return Done(MergeKind.Failed, head, NoFiles, "merge finished with an unexpected result");
            }

            var files = Run("diff", "--name-only", "--diff-filter=U").StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var leftover = RollBack(Run, oldHead);
            if (leftover is not null)
                return Done(MergeKind.Failed, null, files, leftover);

            if (files.Count > 0)
                return Done(MergeKind.Conflict, null, files, "merge aborted");

            var firstErr = merge.StdErr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? $"git merge exited with {merge.ExitCode}";
            return Done(MergeKind.Failed, null, NoFiles, firstErr);
        }
        catch (Exception ex)
        {
            string? leftover = null;
            try { leftover = RollBack(Run, oldHead); } catch { /* best effort */ }
            return Done(MergeKind.Failed, null, NoFiles, leftover is null ? ex.Message : $"{ex.Message}; {leftover}");
        }
    }

    /// <summary>
    /// Undoes whatever a failed or interrupted merge left behind. Returns null when the repo is clean and
    /// not mid-merge afterwards, otherwise an accurate description of what is left.
    /// </summary>
    private static string? RollBack(Func<string[], GitResult> run, string oldHead)
    {
        AbortIfMerging(run);

        var (dirty, merging) = Inspect(run);
        if (!dirty && !merging)
            return null;

        // An interrupted merge (e.g. Ctrl+C while a pre-merge-commit hook runs) can leave staged changes
        // without MERGE_HEAD, which `git merge --abort` cannot undo. `git reset --merge` is safe here:
        // Check verified a clean tree and index immediately before graft started this merge, so every
        // change present now was made by the merge itself and none of the user's work can be lost.
        // Only do it while HEAD is still where the merge started; if HEAD moved, leave the repo alone.
        var head = run(["rev-parse", "HEAD"]).StdOut.Trim();
        if (oldHead.Length == 0 || head != oldHead)
            return $"HEAD moved during an interrupted merge ({Short(oldHead)} to {Short(head)}); not rolling back, inspect with git status";

        run(["reset", "--merge"]);
        (dirty, merging) = Inspect(run);
        if (merging)
            return "repo is still mid-merge, run git merge --abort";
        if (dirty)
            return "working tree left dirty by an interrupted merge; inspect with git status";
        return null;
    }

    private static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash.Length == 0 ? "unknown" : hash;

    private static (bool Dirty, bool Merging) Inspect(Func<string[], GitResult> run)
    {
        var dirty = !string.IsNullOrWhiteSpace(run(["status", "--porcelain"]).StdOut);
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
