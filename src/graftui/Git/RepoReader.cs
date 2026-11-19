using graftui.Model;
using graftui.Operations;

namespace graftui.Git;

/// <summary>
/// Read-only queries for the UI: the branch snapshot and the preview layout. Never logged, never changes the repo.
/// Callers run these on worker threads and pass a token so a stale request can be abandoned.
/// </summary>
public sealed class RepoReader(GitClient git)
{
    // The top-level client: a paused sequence's paths are relative to the top level, so it reads the pause from there.
    private GitClient? _root;

    private const int ListCount = 3;
    private const int ListFetch = ListCount + 1; // one extra row tells whether the list was cut short
    private const int NewListMax = 50; // the commit list shows up to this many of the commits the counts count
    private const int HeadCount = 4;
    private const int HeadFetch = HeadCount + 1; // one extra commit tells whether history was cut short
    private const int MaxTips = 500; // branch tips on one git command line: each is a 40-character hash
    private const long MaxPreviewBytes = 1_000_000;

    public RepoSnapshot LoadSnapshot(bool withCounts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = git.GetCurrentBranch(ct);
        ct.ThrowIfCancellationRequested();
        var head = git.GetHeadHash(ct);
        ct.ThrowIfCancellationRequested();
        var clean = git.IsWorkingTreeClean(ct);
        ct.ThrowIfCancellationRequested();
        var branches = git.GetBranches(ct);

        var counts = new Dictionary<string, (int Ahead, int Behind)>();
        if (withCounts && head is not null)
        {
            foreach (var b in branches)
            {
                if (b.IsCurrent)
                    continue;
                ct.ThrowIfCancellationRequested();
                var ab = git.GetAheadBehind(head, b.Hash, ct);
                if (ab is not null)
                    counts[b.Name] = ab.Value;
            }
        }

        ct.ThrowIfCancellationRequested();
        var read = new SequenceReader(RootClient(ct), ct).ReadSequence();
        return new RepoSnapshot(current, head, clean, branches, counts, git.HasUndoSteps(ct))
        {
            Paused = read.Stop,
            PausedOldTip = read.Stop is { Ours: true } ? read.OldTip : null,
        };
    }

    private GitClient RootClient(CancellationToken ct) => _root ??= new GitClient(git.GetTopLevel(ct) ?? git.RepoPath, git.Timeouts);

    public GraphLayout LoadLayout(RepoSnapshot snap, Branch sel, CancellationToken ct)
    {
        var curTip = snap.HeadHash ?? throw new InvalidOperationException("HEAD has no commits yet");
        return ReadPair(snap.CurrentBranch, snap.CurrentBranch.Length == 0, curTip, sel, snap.Branches, ct);
    }

    /// <summary>
    /// The lane graph of every local branch against the current branch (the trunk when HEAD is detached). Never logged.
    /// LaneGraph.Empty when there are no commits or no branches. Throws OperationCanceledException when ct is cancelled.
    /// </summary>
    public LaneGraph LoadLaneGraph(RepoSnapshot snap, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var head = snap.HeadHash;
        if (head is null || snap.Branches.Count == 0)
            return LaneGraph.Empty;

        var branches = snap.Branches;
        var onBranch = snap.CurrentBranch.Length > 0;
        var baseName = onBranch ? snap.CurrentBranch : (BranchLanes.TrunkName(branches) ?? branches[0].Name);
        // The tip comes from the snapshot, not from a new read, so the graph matches the branch list the screen shows.
        var baseTip = onBranch ? head : branches.First(b => b.Name == baseName).Hash;

        ct.ThrowIfCancellationRequested();
        var baseRead = git.GetGraphCommits([baseTip], null, LaneGraphBuilder.BaseMax + 1, firstParent: true, ct);
        var baseChainMore = baseRead.Count > LaneGraphBuilder.BaseMax;
        var baseChain = baseRead.Take(LaneGraphBuilder.BaseMax).ToList();

        var tips = TipsOf(branches, baseName, baseTip, MaxTips, out var tipsCut);
        IReadOnlyList<GraphCommit> others = [];
        var othersCut = tipsCut;
        if (tips.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var othersRead = git.GetGraphCommits(tips, baseTip, LaneGraphBuilder.OthersMax + 1, firstParent: false, ct);
            // Past OthersMax the newest commits are kept. A branch whose commits are all cut shows as a pointer at column -1, and
            // OthersCut tells the screen that history was left out.
            othersCut = othersCut || othersRead.Count > LaneGraphBuilder.OthersMax;
            others = othersRead.Take(LaneGraphBuilder.OthersMax).ToList();
        }

        ct.ThrowIfCancellationRequested();
        return LaneGraphBuilder.Build(new LaneGraphInput(baseName, baseChain, baseChainMore, branches, others, othersCut));
    }

    /// <summary>
    /// The tips of the other branches for the lane graph read: one per distinct commit, in the branch list's order, and none for
    /// the base or for a branch on the base tip. At most <paramref name="max"/> are kept; <paramref name="cut"/> says whether any
    /// were left out. The cap keeps the command line well under Windows' limit (one hash per tip).
    /// </summary>
    internal static IReadOnlyList<string> TipsOf(IReadOnlyList<Branch> branches, string baseName, string baseTip, int max, out bool cut)
    {
        var tips = new List<string>();
        var seen = new HashSet<string>();
        cut = false;
        foreach (var branch in branches)
        {
            if (branch.Name == baseName || branch.Hash == baseTip || !seen.Add(branch.Hash))
                continue;
            if (tips.Count == max)
            {
                cut = true;
                break;
            }

            tips.Add(branch.Hash);
        }

        return tips;
    }

    /// <summary>
    /// The layout of a paused rebase's branch against the onto it is rebased onto, read from the branch's old tip. HEAD is detached while the
    /// rebase runs, so the snapshot cannot give the branch's own commits; the old tip gives the same layout the rebase started from.
    /// Throws when the onto is not listed or a read fails.
    /// </summary>
    public GraphLayout LoadRebaseLayout(string branch, string oldTip, string onto, RepoSnapshot snap, CancellationToken ct)
    {
        var sel = snap.Branches.FirstOrDefault(b => b.Name == onto)
            ?? throw new InvalidOperationException($"{onto} is not listed");
        return ReadPair(branch, false, oldTip, sel, snap.Branches, ct);
    }

    // The layout of two branch tips: the current one (its name, whether HEAD is detached, and its tip) against the selected branch.
    private GraphLayout ReadPair(string curName, bool detached, string curTip, Branch sel, IReadOnlyList<Branch> branches, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var baseHash = git.GetMergeBase(curTip, sel.Hash, ct);

        ct.ThrowIfCancellationRequested();
        var (curOwn, curOwnMore) = Window(git.GetFirstParentChain(curTip, baseHash, ListFetch, ct));
        ct.ThrowIfCancellationRequested();
        var (selOwn, selOwnMore) = Window(git.GetFirstParentChain(sel.Hash, baseHash, ListFetch, ct));

        var (shared, sharedMore) = baseHash is null
            ? (new List<CommitInfo>(), false)
            : Window(git.GetFirstParentChain(baseHash, null, ListFetch, ct));

        ct.ThrowIfCancellationRequested();
        var curTipCommit = git.GetCommit(curTip, ct) ?? throw new InvalidOperationException($"could not read commit {curTip}");
        ct.ThrowIfCancellationRequested();
        var selTipCommit = git.GetCommit(sel.Hash, ct) ?? throw new InvalidOperationException($"could not read commit {sel.Hash}");

        ct.ThrowIfCancellationRequested();
        var (ahead, behind) = git.GetAheadBehind(curTip, sel.Hash, ct)
            ?? throw new InvalidOperationException("could not count commits between the two branches");

        var kind = KindOf(curTip, sel.Hash, baseHash);

        ct.ThrowIfCancellationRequested();
        var selNew = git.GetCommitsNotIn(sel.Hash, curTip, NewListMax, ct);
        ct.ThrowIfCancellationRequested();
        var curNew = git.GetCommitsNotIn(curTip, sel.Hash, NewListMax, ct);

        return new GraphLayout(
            curName, detached, sel.Name,
            curTip, sel.Hash, baseHash,
            curOwn, curOwnMore,
            selOwn, selOwnMore,
            shared, sharedMore,
            curTipCommit, selTipCommit,
            ahead, behind, kind)
        {
            SelectedNew = selNew,
            CurrentNew = curNew,
        }.WithLanes(branches);
    }

    /// <summary>Changes-tab read: the status rows, the current branch with its last four commits, and the stash list. Never logged.</summary>
    public ChangesSnapshot LoadChanges(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var status = git.RunRead(ct, "status", "--porcelain=v2", "-z", "--untracked-files=all");
        // A read cancelled by the UI must not turn into a failed status: this throws first.
        ct.ThrowIfCancellationRequested();
        if (!status.Success)
            throw new InvalidOperationException(GitClient.Describe(status));
        var working = StatusParser.Parse(status.StdOut);

        ct.ThrowIfCancellationRequested();
        var branch = git.GetCurrentBranch(ct);
        ct.ThrowIfCancellationRequested();
        var head = git.GetHeadHash(ct);

        IReadOnlyList<CommitInfo> newestFirst = [];
        if (head is not null)
        {
            ct.ThrowIfCancellationRequested();
            newestFirst = git.GetFirstParentChain(head, null, HeadFetch, ct);
        }
        var recent = newestFirst.Take(HeadCount).Reverse().ToList();
        var line = new HeadLine(branch, head, recent, newestFirst.Count > HeadCount);

        ct.ThrowIfCancellationRequested();
        var stashes = git.RunRead(ct, GitArgs.Stash, "list", "--format=%H%x1f%gs");
        ct.ThrowIfCancellationRequested();
        if (!stashes.Success)
            throw new InvalidOperationException(GitClient.Describe(stashes));

        ct.ThrowIfCancellationRequested();
        return new ChangesSnapshot(working, line, status.StdOut, StashSummaryOf(stashes.StdOut), git.HasUndoSteps(ct));
    }

    /// <summary>The stash list's lines: their number, and the newest entry's commit and reflog subject (line 1).</summary>
    private static StashSummary StashSummaryOf(string stdout)
    {
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return StashSummary.None;

        var top = lines[0].Split('\x1f', 2);
        return new StashSummary(lines.Length, top[0], top.Length > 1 ? top[1] : "");
    }

    /// <summary>
    /// The diff of one row, through literal pathspecs. Untracked files are diffed against /dev/null; nested repos and
    /// very large files are not read. A failed git diff becomes a note, so this never throws for git's own errors.
    /// </summary>
    public FileDiff LoadDiff(FileChange f, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (f.Side == ChangeSide.Staged)
        {
            var args = new List<string> { "diff", "--cached", "--no-color", "--no-ext-diff", "-M", "--", f.Path };
            if (f.OrigPath is not null)
                args.Add(f.OrigPath);
            var staged = git.RunReadLiteral(ct, [.. args]);
            ct.ThrowIfCancellationRequested();
            return new FileDiff(f, DiffOf(staged));
        }

        if (f.Kind == ChangeKind.Untracked)
            return new FileDiff(f, UntrackedDiff(f, ct));

        var unstaged = git.RunReadLiteral(ct, "diff", "--no-color", "--no-ext-diff", "--", f.Path);
        ct.ThrowIfCancellationRequested();
        return new FileDiff(f, DiffOf(unstaged));
    }

    private IReadOnlyList<DiffLine> UntrackedDiff(FileChange f, CancellationToken ct)
    {
        if (f.IsNestedRepo)
            return [new DiffLine(DiffLineKind.Note, "a separate git repository; not previewed")];

        var file = new FileInfo(Path.Combine(git.RepoPath, f.Path));
        if (file.Exists && file.Length > MaxPreviewBytes)
            return [new DiffLine(DiffLineKind.Note, $"new file, {file.Length} bytes: too large to preview")];
        if (file.Exists && file.Length == 0)
            return [new DiffLine(DiffLineKind.Note, "(empty file)")];

        // git diff --no-index exits 1 when the files differ, which is the normal case here. A binary file gives git's
        // "Binary files ... differ" note, which DiffParser keeps.
        var r = git.RunReadLiteral(ct, "diff", "--no-color", "--no-ext-diff", "--no-index", "--", "/dev/null", f.Path);
        ct.ThrowIfCancellationRequested();
        if (r.ExitCode is not (0 or 1))
            return [new DiffLine(DiffLineKind.Note, FailedNote(r))];
        return DiffParser.Parse(r.StdOut);
    }

    private static IReadOnlyList<DiffLine> DiffOf(GitResult r) =>
        r.Success ? DiffParser.Parse(r.StdOut) : [new DiffLine(DiffLineKind.Note, FailedNote(r))];

    private static string FailedNote(GitResult r)
    {
        var first = r.StdErr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? $"exit {r.ExitCode}";
        return "git diff failed: " + first;
    }

    /// <summary>Keeps the newest 3 of git's newest-first rows and returns them oldest-first, plus whether more existed.</summary>
    private static (IReadOnlyList<CommitInfo> Items, bool More) Window(IReadOnlyList<CommitInfo> newestFirst) =>
        (newestFirst.Take(ListCount).Reverse().ToList(), newestFirst.Count > ListCount);

    /// <summary>How the selected branch relates to the current one: the same commit, unrelated, up to date, a fast-forward or diverged.</summary>
    private static GraphKind KindOf(string curTip, string selHash, string? baseHash)
    {
        if (curTip == selHash)
            return GraphKind.Same;
        if (baseHash is null)
            return GraphKind.Unrelated;
        if (baseHash == selHash)
            return GraphKind.UpToDate;
        if (baseHash == curTip)
            return GraphKind.FastForward;
        return GraphKind.Diverged;
    }
}
