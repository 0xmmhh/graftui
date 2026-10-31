using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Discard for the Changes tab (x): the unstaged change to one path is saved as a stash entry, then the file is put back
/// (tracked: from the index) or removed (untracked). The index is never changed, so a partly staged file keeps its staged part.
/// Check reads the repo and the row, and refuses what cannot be backed up safely.
/// Run re-judges the row, writes the backup commits, stores them as the top stash entry, checks them again, restores or
/// removes the file, and verifies the result. Run never throws.
/// Rollback: a failure before restore or clean changes nothing in the work tree. A failure after it keeps the stash entry,
/// so the change is always in stash@{0}. The undo step is saved only after a verified success.
/// </summary>
public sealed class DiscardOperation(GitClient git) : IGitOperation<DiscardRequest, DiscardCheck, DiscardOutcome>
{
    private const string StashRef = "refs/stash";
    private const string DetachedName = "(no branch)";
    private static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    public DiscardCheck Check(DiscardRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request, log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, log);
        }
    }

    public DiscardOutcome Run(DiscardRequest request, DiscardCheck check)
    {
        var log = new List<GitResult>();
        var path = request.File.Path;
        if (!check.CanRun)
            return Done(DiscardKind.Failed, path, null, check.Refusal ?? "nothing was discarded", null, log);

        try
        {
            return RunCore(request.File, check, log);
        }
        catch (Exception ex)
        {
            return Done(DiscardKind.Failed, path, null, $"{ex.Message}; repo state unknown, run git status", null, log);
        }
    }

    private DiscardCheck CheckCore(DiscardRequest request, List<GitResult> log)
    {
        // 1. A commit must exist: the backup is a child of HEAD.
        var head = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        if (!head.Success)
            return Refused(head.ExitCode == 1 ? ChangeRefusals.NoCommitYet : GitClient.Describe(head), log);

        // 2. A detached HEAD is fine: the backup is made from HEAD's commit, not from a branch.
        var sym = Call(log, "symbolic-ref", "-q", "HEAD");
        if (!sym.Success && sym.ExitCode != 1)
            return Refused(GitClient.Describe(sym), log);
        var branch = sym.Success ? GitClient.StripHeads(sym.StdOut.Trim()) : DetachedName;

        // 3. The git dir, and no other operation that owns the index.
        var gitDir = RepoState.GitDir(git, log);
        if (gitDir is null)
            return Refused("could not find the git dir", log);
        var inProgress = RepoState.InProgress(a => Call(log, a), gitDir);
        if (inProgress is not null)
            return Refused(ChangeRefusals.InProgress(inProgress), log);

        // 4. The row must still be a discardable unstaged row.
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Refused(GitClient.Describe(st), log);
        var (refusal, row) = Judge(request.File, StatusParser.Parse(st.StdOut));
        if (refusal is not null)
            return Refused(refusal, log);

        return new DiscardCheck(null, row, head.StdOut.Trim(), branch, gitDir, log);
    }

    private DiscardOutcome RunCore(FileChange file, DiscardCheck check, List<GitResult> log)
    {
        var path = file.Path;
        var gitDir = check.GitDir;
        var head = check.Head;
        var branch = check.Branch;
        var untracked = check.Row!.Kind == ChangeKind.Untracked;

        // 1. HEAD is still the commit the check saw.
        var now = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        if (!now.Success || now.StdOut.Trim() != head)
            return Done(DiscardKind.Failed, path, null, "HEAD moved since the list was read; nothing was discarded", null, log);

        // 2. The row is still there, with the same kind.
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Done(DiscardKind.Failed, path, null, GitClient.Describe(st) + "; nothing was discarded", null, log);
        var (refusal, _) = Judge(file, StatusParser.Parse(st.StdOut));
        if (refusal is not null)
            return Done(DiscardKind.Failed, path, null, refusal, null, log);

        // 3. What the index holds for the path, and the trees that describe the staged state and the work.
        var index = CallLiteral(log, "ls-files", "-s", "-z", "--", path);
        if (!index.Success)
            return Done(DiscardKind.Failed, path, null, GitClient.Describe(index) + "; nothing was discarded", null, log);
        var indexBefore = RepoState.ParseIndex(index.StdOut);
        var indexTree = Hash(Call(log, "write-tree"));
        if (indexTree is null)
            return Done(DiscardKind.Failed, path, null, "could not read the index; nothing was discarded", null, log);
        var state = new WorkState(git, gitDir);
        var workTree = state.PathTree(path, untracked, log);
        if (workTree is null)
            return Done(DiscardKind.Failed, path, null, $"could not read {path}; nothing was discarded", null, log);

        // 4. The backup: B is the staged state on HEAD, I is the index commit, U holds untracked files, W is the stash.
        var subjectLine = Call(log, "log", "-1", "--no-show-signature", "--format=%h %s", head, "--");
        var subject = subjectLine.Success ? subjectLine.StdOut.Trim() : head[..Math.Min(7, head.Length)];
        var b = Hash(Call(log, GitArgs.CommitTree, GitArgs.NoGpgSign, indexTree, "-p", head, "-m", $"graftui: staged state on {branch}"));
        var i = b is null ? null
            : Hash(Call(log, GitArgs.CommitTree, GitArgs.NoGpgSign, indexTree, "-p", b, "-m", $"index on {branch}: {subject}"));
        var u = i is null || !untracked ? null
            : Hash(Call(log, GitArgs.CommitTree, GitArgs.NoGpgSign, workTree, "-m", $"untracked files on {branch}: {subject}"));
        string? w = null;
        if (b is not null && i is not null && (!untracked || u is not null))
        {
            var args = new List<string> { GitArgs.CommitTree, GitArgs.NoGpgSign, untracked ? indexTree : workTree, "-p", b, "-p", i };
            if (u is not null)
                args.AddRange(["-p", u]);
            args.AddRange(["-m", $"On {branch}: graftui: discarded {path}"]);
            w = Hash(Call(log, [.. args]));
        }
        if (w is null)
            return Done(DiscardKind.Failed, path, null, "could not save the discarded change; nothing was discarded", null, log);

        // 5. Store W as the newest stash entry. Every later step can find it by its hash.
        var below = RevParse(log, StashRef);
        if (below is null)
            return Done(DiscardKind.Failed, path, null, "could not read the stash; nothing was discarded", null, log);
        var store = Call(log, GitArgs.Stash, "store", "-m", $"graftui: discarded {path}", w);
        if (!store.Success)
            return Done(DiscardKind.Failed, path, null, GitClient.Describe(store) + "; nothing was discarded", null, log);

        // 6. The file must not have changed since the copy was taken. If it did, the copy is taken back out.
        if (state.PathTree(path, untracked, log) != workTree)
        {
            if (RevParse(log, StashRef) == w)
                Call(log, GitArgs.Stash, "drop", "-q", GitArgs.StashTop);
            var kept = RevParse(log, StashRef) == w ? "; a copy is in stash@{0}" : "";
            return Done(DiscardKind.Failed, path, kept.Length > 0 ? GitArgs.StashTop : null,
                $"{path} changed while it was being saved; nothing was discarded{kept}", null, log);
        }

        // 7. Put the file back, or remove it. From here on a failure keeps the stash entry.
        var put = untracked
            ? CallLiteral(log, "clean", "-f", "-q", "--", path)
            : CallLiteral(log, "restore", "-q", "--worktree", "--", path);
        if (!put.Success)
            return Done(DiscardKind.Failed, path, GitArgs.StashTop,
                $"{GitClient.Describe(put)}; nothing was discarded; a copy is in stash@{{0}}", null, log);

        // 8. Verify: the row is gone, the index is as it was, and the copy is the top entry.
        var problem = Verify(log, path, indexTree, indexBefore, w, out var after);
        if (problem is not null)
            return Done(DiscardKind.Failed, path, GitArgs.StashTop, $"{problem}; a copy of your changes is in stash@{{0}}", after, log);

        // 9. The undo step, saved only now. If it cannot be saved the discard still stands, with a note.
        var undo = new UndoRecord(UndoKind.Discard, "discard of " + path, [], Branch: branch, Head: head,
            StashCommit: w, StashBelow: below, Path: path, Backup: untracked ? u : w, Untracked: untracked, IndexBefore: indexBefore);
        var undoProblem = new UndoLog(git).Push(undo, log);
        var saved = $"discarded {path}: saved as stash \"graftui: discarded {path}\"";
        return undoProblem is null
            ? Done(DiscardKind.Discarded, path, GitArgs.StashTop, saved, after, log, undo)
            : Done(DiscardKind.Discarded, path, GitArgs.StashTop,
                $"{saved}; could not save the undo step ({undoProblem}): u can't bring it back automatically; it is in stash@{{0}}", after, log);
    }

    /// <summary>
    /// Null when the discard took effect: no unstaged row for the path, the index tree and the path's index entry are as
    /// they were, and the copy is the top stash entry. Otherwise what is wrong.
    /// </summary>
    private string? Verify(List<GitResult> log, string path, string indexTree, IReadOnlyList<IndexEntry> indexBefore, string copy,
        out WorkingStatus? after)
    {
        after = null;
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return "could not read the status after the discard: " + GitClient.Describe(st);
        after = StatusParser.Parse(st.StdOut);
        if (after.Unstaged.Any(f => f.Path == path))
            return $"{path} still has unstaged changes after the discard";

        if (Hash(Call(log, "write-tree")) != indexTree)
            return "the index changed during the discard";

        var index = CallLiteral(log, "ls-files", "-s", "-z", "--", path);
        if (!index.Success || !RepoState.ParseIndex(index.StdOut).SequenceEqual(indexBefore))
            return $"the index entry for {path} changed during the discard";

        return RevParse(log, StashRef) == copy ? null : "the stash entry is not the copy that was saved";
    }

    /// <summary>The rows the discard may act on, or the refusal. Used by Check and again by Run.</summary>
    private static (string? Refusal, FileChange? Row) Judge(FileChange file, WorkingStatus status)
    {
        var path = file.Path;
        // A staged row never discards the unstaged part of the same path: that would be a different row than the one shown.
        var row = file.Side == ChangeSide.Unstaged ? status.Unstaged.FirstOrDefault(f => f.Path == path) : null;
        if (row is null)
            return (ChangeRefusals.NoUnstaged(path), null);
        if (row.Kind == ChangeKind.Conflicted)
            return (ChangeRefusals.ConflictFile(path), null);
        if (status.HasConflicts)
            return (ChangeRefusals.Conflicts, null);
        if (row.IsSubmodule)
            return (ChangeRefusals.Submodule(path), null);
        if (row.IsNestedRepo)
            return (ChangeRefusals.NestedRepoDiscard(path), null);
        if (row.Kind == ChangeKind.Added)
            return (ChangeRefusals.IntentToAdd(path), null);
        if (row.Kind != file.Kind)
            return (ChangeRefusals.Stale(path), null);
        return (null, row);
    }

    /// <summary>The hash of a ref, "" when the ref does not exist, or null when git fails.</summary>
    private string? RevParse(List<GitResult> log, string name)
    {
        var r = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, name);
        if (r.Success)
            return r.StdOut.Trim();
        return r.ExitCode == 1 ? "" : null;
    }

    private static DiscardCheck Refused(string reason, List<GitResult> log) =>
        new(reason, null, "", "", "", log);

    private static DiscardOutcome Done(DiscardKind kind, string path, string? stashRef, string message, WorkingStatus? after,
        List<GitResult> log, UndoRecord? undo = null) =>
        new(kind, path, stashRef, message, after, log, undo);

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

    private static string? Hash(GitResult r)
    {
        var hash = r.StdOut.Trim();
        return r.Success && hash.Length > 0 ? hash : null;
    }
}
