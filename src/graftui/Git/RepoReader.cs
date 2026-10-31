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
    private const int HeadCount = 4;
    private const int HeadFetch = HeadCount + 1; // one extra commit tells whether history was cut short
    private const long MaxPreviewBytes = 1_000_000;

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

        ct.ThrowIfCancellationRequested();
        return new RepoSnapshot(current, head, clean, branches, counts, git.HasUndoSteps());
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

        var kind = KindOf(curTip, sel.Hash, baseHash);

        return new GraphLayout(
            snap.CurrentBranch, detached, sel.Name,
            curTip, sel.Hash, baseHash,
            curOwn, curOwnMore,
            selOwn, selOwnMore,
            shared, sharedMore,
            curTipCommit, selTipCommit,
            ahead, behind, kind);
    }

    /// <summary>Changes-tab read: the status rows, the current branch with its last four commits, and the stash list. Never logged.</summary>
    public ChangesSnapshot LoadChanges(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var status = git.Run("status", "--porcelain=v2", "-z", "--untracked-files=all");
        if (!status.Success)
            throw new InvalidOperationException(GitClient.Describe(status));
        var working = StatusParser.Parse(status.StdOut);

        ct.ThrowIfCancellationRequested();
        var branch = git.GetCurrentBranch();
        ct.ThrowIfCancellationRequested();
        var head = git.GetHeadHash();

        IReadOnlyList<CommitInfo> newestFirst = [];
        if (head is not null)
        {
            ct.ThrowIfCancellationRequested();
            newestFirst = git.GetFirstParentChain(head, null, HeadFetch);
        }
        var recent = newestFirst.Take(HeadCount).Reverse().ToList();
        var line = new HeadLine(branch, head, recent, newestFirst.Count > HeadCount);

        ct.ThrowIfCancellationRequested();
        var stashes = git.Run(GitArgs.Stash, "list", "--format=%H%x1f%gs");
        if (!stashes.Success)
            throw new InvalidOperationException(GitClient.Describe(stashes));

        ct.ThrowIfCancellationRequested();
        return new ChangesSnapshot(working, line, status.StdOut, StashSummaryOf(stashes.StdOut), git.HasUndoSteps());
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
            return new FileDiff(f, DiffOf(git.RunLiteral([.. args])));
        }

        if (f.Kind == ChangeKind.Untracked)
            return new FileDiff(f, UntrackedDiff(f));

        return new FileDiff(f, DiffOf(git.RunLiteral("diff", "--no-color", "--no-ext-diff", "--", f.Path)));
    }

    private IReadOnlyList<DiffLine> UntrackedDiff(FileChange f)
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
        var r = git.RunLiteral("diff", "--no-color", "--no-ext-diff", "--no-index", "--", "/dev/null", f.Path);
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
