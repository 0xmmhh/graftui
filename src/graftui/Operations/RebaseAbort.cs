using graftui.Git;

namespace graftui.Operations;

/// <summary>
/// The fallback abort of a rebase that graftui started and that stopped at a conflict, for when the resume operation could not abort it.
/// It runs git's own `rebase --abort` through the logged call, then checks that the branch is back on its old tip with no rebase in
/// progress. The caller reports a failure when the check fails, so the repo is never left mid-rebase silently.
/// </summary>
internal static class RebaseAbort
{
    /// <summary>
    /// Aborts the rebase in <paramref name="gitDir"/> if git still holds one that graftui started (<see cref="SequenceOwnership.IsOurRebase"/>,
    /// checked before git's abort runs), then checks that <paramref name="branch"/> is checked out at <paramref name="head"/> and that no
    /// rebase is in progress. The state file is deleted only when that holds. Every git call is added to <paramref name="log"/>. Returns
    /// false when the repo could not be put back, and when the rebase git holds is not graftui's: it is then left as it is.
    /// </summary>
    public static bool Force(GitClient git, string gitDir, string branch, string head, List<GitResult> log)
    {
        if (gitDir.Length == 0)
            return false;

        if (SequenceGit.RebaseInProgress(gitDir))
        {
            if (!SequenceOwnership.IsOurRebase(gitDir, branch, head))
                return false;
            BranchGit.Call(git, log, "rebase", "--abort");
        }
        if (SequenceGit.RebaseInProgress(gitDir))
            return false;

        var symbolic = BranchGit.SymbolicHead(git, log);
        var now = BranchGit.RefOrEmpty(git, log, "HEAD");
        if (symbolic != BranchGit.HeadsPrefix + branch || now != head)
            return false;

        SequenceStateFile.Delete(gitDir);
        return true;
    }
}
