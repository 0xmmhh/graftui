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
        var current = Run("symbolic-ref", "-q", "--short", "HEAD");
        var name = current.StdOut.Trim();
        if (!current.Success || name.Length == 0)
        {
            var reason = current.ExitCode is 0 or 1 || string.IsNullOrWhiteSpace(current.StdErr)
                ? "HEAD is detached"
                : current.StdErr.Trim();
            return new MergeCheck("", reason, log);
        }

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

        try
        {
            var oldHead = Run("rev-parse", "HEAD").StdOut.Trim();
            var tipResult = Run("rev-parse", "--verify", branch + "^{commit}");
            var branchTip = tipResult.StdOut.Trim();
            if (!tipResult.Success || branchTip.Length == 0)
                return Done(MergeKind.Failed, null, NoFiles, $"could not resolve branch {branch}");

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

            AbortIfMerging(Run);

            var dirty = !string.IsNullOrWhiteSpace(Run("status", "--porcelain").StdOut);
            var stillMerging = Run("rev-parse", "-q", "--verify", "MERGE_HEAD").Success;
            if (dirty || stillMerging)
                return Done(MergeKind.Failed, null, files, "repo may be mid-merge, run git merge --abort");

            if (files.Count > 0)
                return Done(MergeKind.Conflict, null, files, "merge aborted");

            var firstErr = merge.StdErr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? $"git merge exited with {merge.ExitCode}";
            return Done(MergeKind.Failed, null, NoFiles, firstErr);
        }
        catch (Exception ex)
        {
            try { AbortIfMerging(Run); } catch { /* best effort */ }
            return Done(MergeKind.Failed, null, NoFiles, ex.Message);
        }
    }

    private static void AbortIfMerging(Func<string[], GitResult> run)
    {
        if (!run(["rev-parse", "-q", "--verify", "MERGE_HEAD"]).Success)
            return;
        if (!run(["merge", "--abort"]).Success)
            run(["reset", "--merge"]);
    }
}
