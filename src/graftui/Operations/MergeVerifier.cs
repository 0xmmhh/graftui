using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// What graftui read back after a merge: whether HEAD is still on the branch, where HEAD is, whether a merge is still in progress and
/// whether the tree is clean. Verified is true only when all of that matches the outcome. Commands: the four reads, in the order made.
/// </summary>
public sealed record MergeVerdict(bool Verified, bool OnBranch, string? Head, bool Merging, bool Clean, IReadOnlyList<GitResult> Commands);

/// <summary>
/// Reads the repo again after <see cref="MergeOperation.Run"/>, so the screen says "verified" only for what it has checked. Read-only:
/// every call is a read, and nothing is written. Never throws.
/// </summary>
public static class MergeVerifier
{
    private static readonly string[] SymbolicHead = ["symbolic-ref", "-q", "HEAD"];
    private static readonly string[] RevHead = [GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD"];
    private static readonly string[] RevMergeHead = [GitArgs.RevParse, "-q", GitArgs.Verify, GitArgs.MergeHead];
    private static readonly string[] Status = ["status", "--porcelain", "--untracked-files=normal"];

    // git rev-parse -q --verify exits 1, quietly, when the name is missing: that is how "no MERGE_HEAD" is told apart from a failure.
    private const int QuietMissing = 1;

    /// <summary>
    /// The commit HEAD must be at for this outcome, or null when nothing can be expected (so the merge is never verified). Before is the
    /// HEAD that m was pressed on: a merge commit or a fast-forward moves HEAD to its new head; a conflict, an up-to-date merge and a
    /// failure with no new head leave HEAD at before.
    /// </summary>
    public static string? ExpectedHead(MergeOutcome o, string? before)
    {
        if (before is null)
            return null;

        if (o.Kind == MergeKind.MergeCommit)
        {
            // A merge commit must have moved HEAD. Equal heads mean nothing moved, so there is nothing to verify.
            if (o.NewHead is null || o.NewHead == before)
                return null;
            return o.NewHead;
        }

        if (o.Kind == MergeKind.FastForward)
            return o.NewHead;

        if (o.Kind == MergeKind.Conflict || o.Kind == MergeKind.UpToDate)
            return before;

        // A failure without a new head was rolled back (or never started), so HEAD is at before. A failure with a new head is an
        // unexpected result, which is never verified.
        if (o.NewHead is null)
            return before;
        return null;
    }

    /// <summary>
    /// Reads HEAD, the symbolic ref, MERGE_HEAD and the tree's status, and says whether they match <paramref name="o"/> (see
    /// <see cref="ExpectedHead"/>). A read that fails, or an exception from git, is not verified. Never throws.
    /// </summary>
    public static MergeVerdict Check(GitClient git, MergeOutcome o, string? before)
    {
        var log = new List<GitResult>();
        try
        {
            var symbolic = ReadLogged(git, log, SymbolicHead);
            var head = ReadLogged(git, log, RevHead);
            var mergeHead = ReadLogged(git, log, RevMergeHead);
            var status = ReadLogged(git, log, Status);

            var onBranch = symbolic.Success && symbolic.StdOut.Trim() == BranchGit.HeadsPrefix + o.Into;
            var headHash = head.Success ? head.StdOut.Trim() : null;
            var notMerging = mergeHead.ExitCode == QuietMissing;
            var clean = status.Success && string.IsNullOrWhiteSpace(status.StdOut);
            var expected = ExpectedHead(o, before);

            var verified = onBranch && notMerging && clean && expected is not null && headHash == expected;
            return new MergeVerdict(verified, onBranch, headHash, !notMerging, clean, log);
        }
        catch (Exception)
        {
            // Unknown state: say so, and keep the reads that were made, so the Commands pane shows what was looked at.
            return new MergeVerdict(false, false, null, true, false, log);
        }
    }

    // One read, kept in the log. The read has no timeout beyond GitClient's read timeout and is never cancelled.
    private static GitResult ReadLogged(GitClient git, List<GitResult> log, string[] args)
    {
        var result = git.RunRead(CancellationToken.None, args);
        log.Add(result);
        return result;
    }
}
