using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Read-only queries for the UI: the branch snapshot and the preview layout. Never logged, never changes the repo.
/// Callers run these on worker threads and pass a token so a stale request can be abandoned.
/// </summary>
public sealed class RepoReader(GitClient git)
{
    private const int ListCount = 3;
    private const int ListFetch = ListCount + 1; // one extra row tells whether the list was cut short

    public RepoSnapshot LoadSnapshot(bool withCounts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = git.GetCurrentBranch();
        ct.ThrowIfCancellationRequested();
        var head = git.GetHeadHash();
        ct.ThrowIfCancellationRequested();
        var clean = git.IsWorkingTreeClean();
        ct.ThrowIfCancellationRequested();
        var branches = git.GetBranches();

        var counts = new Dictionary<string, (int Ahead, int Behind)>();
        if (withCounts && head is not null)
        {
            foreach (var b in branches)
            {
                if (b.IsCurrent)
                    continue;
                ct.ThrowIfCancellationRequested();
                if (git.GetAheadBehind(head, b.Hash) is { } ab)
                    counts[b.Name] = ab;
            }
        }

        return new RepoSnapshot(current, head, clean, branches, counts);
    }

    public GraphLayout LoadLayout(RepoSnapshot snap, Branch sel, CancellationToken ct)
    {
        var curTip = snap.HeadHash ?? throw new InvalidOperationException("HEAD has no commits yet");
        var detached = snap.CurrentBranch.Length == 0;

        ct.ThrowIfCancellationRequested();
        var baseHash = git.GetMergeBase(curTip, sel.Hash);

        ct.ThrowIfCancellationRequested();
        var (curOwn, curOwnMore) = Window(git.GetFirstParentChain(curTip, baseHash, ListFetch));
        ct.ThrowIfCancellationRequested();
        var (selOwn, selOwnMore) = Window(git.GetFirstParentChain(sel.Hash, baseHash, ListFetch));

        var (shared, sharedMore) = baseHash is null
            ? (new List<CommitInfo>(), false)
            : Window(git.GetFirstParentChain(baseHash, null, ListFetch));

        ct.ThrowIfCancellationRequested();
        var curTipCommit = git.GetCommit(curTip) ?? throw new InvalidOperationException($"could not read commit {curTip}");
        ct.ThrowIfCancellationRequested();
        var selTipCommit = git.GetCommit(sel.Hash) ?? throw new InvalidOperationException($"could not read commit {sel.Hash}");

        ct.ThrowIfCancellationRequested();
        var (ahead, behind) = git.GetAheadBehind(curTip, sel.Hash)
            ?? throw new InvalidOperationException("could not count commits between the two branches");

        var kind = curTip == sel.Hash ? GraphKind.Same
            : baseHash is null ? GraphKind.Unrelated
            : baseHash == sel.Hash ? GraphKind.UpToDate
            : baseHash == curTip ? GraphKind.FastForward
            : GraphKind.Diverged;

        return new GraphLayout(
            snap.CurrentBranch, detached, sel.Name,
            curTip, sel.Hash, baseHash,
            curOwn, curOwnMore,
            selOwn, selOwnMore,
            shared, sharedMore,
            curTipCommit, selTipCommit,
            ahead, behind, kind);
    }

    /// <summary>Keeps the newest 3 of git's newest-first rows and returns them oldest-first, plus whether more existed.</summary>
    private static (IReadOnlyList<CommitInfo> Items, bool More) Window(IReadOnlyList<CommitInfo> newestFirst) =>
        (newestFirst.Take(ListCount).Reverse().ToList(), newestFirst.Count > ListCount);
}
