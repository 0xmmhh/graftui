using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Stash for the Changes tab (z): `git stash push -u` takes every tracked and untracked change, so the tree is clean afterwards.
/// The message is passed with -m only when the user typed one; a blank message gives git's default.
/// Check reads the repo and refuses what git cannot stash cleanly (no commit, a merge in progress, conflicts, a nested repo,
/// a submodule, an intent-to-add file) and a clean tree.
/// Run runs the stash, then verifies from the stash ref, its parent and the status. Run never throws.
/// Rollback: none. A stash that did not happen changes nothing. A stash that happened is never dropped, because it holds
/// the user's changes: when the tree is not clean afterwards the message says where the changes are.
/// </summary>
public sealed class StashOperation(GitClient git) : IGitOperation<StashRequest, StashCheck, StashOutcome>
{
    private const string StashRef = "refs/stash";
    // The name the undo step shows on screen. A label, not a git argument.
    private const string StashStepLabel = "stash";
    private static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    public StashCheck Check(StashRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, log);
        }
    }

    public StashOutcome Run(StashRequest request, StashCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Done(StashKind.Failed, [], null, check.Refusal ?? "nothing was stashed", log);

        try
        {
            return RunCore(request, check, log);
        }
        catch (Exception ex)
        {
            return Done(StashKind.Failed, [], null, $"{ex.Message}; repo state unknown, run git status", log);
        }
    }

    private StashCheck CheckCore(List<GitResult> log)
    {
        // 1. A commit must exist: the stash is made on top of HEAD.
        var head = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        if (!head.Success)
            return Refused(head.ExitCode == 1 ? ChangeRefusals.NoCommitYet : GitClient.Describe(head), log);

        // 2. The git dir, and no other operation that owns the index.
        var gitDir = RepoState.GitDir(git, log);
        if (gitDir is null)
            return Refused("could not find the git dir", log);
        var inProgress = RepoState.InProgress(a => Call(log, a), gitDir);
        if (inProgress is not null)
            return Refused(ChangeRefusals.InProgress(inProgress), log);

        // 3. The status: nothing to stash, or something git cannot stash safely.
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Refused(GitClient.Describe(st), log);
        var status = StatusParser.Parse(st.StdOut);
        var refusal = Refusal(status);
        if (refusal is not null)
            return Refused(refusal, log);

        return new StashCheck(null, status.Rows, head.StdOut.Trim(), gitDir, log);
    }

    private StashOutcome RunCore(StashRequest request, StashCheck check, List<GitResult> log)
    {
        // 1. HEAD is still the commit the check saw, and the tree still has something to stash.
        var head = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        if (!head.Success || head.StdOut.Trim() != check.Head)
            return Done(StashKind.Failed, [], null, "HEAD moved since the check; nothing was stashed", log);

        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Done(StashKind.Failed, [], null, GitClient.Describe(st) + "; nothing was stashed", log);
        var before = StatusParser.Parse(st.StdOut);
        var refusal = Refusal(before);
        if (refusal is not null)
            return Done(StashKind.Failed, [], null, refusal, log);

        // 2. Stash. A new entry is the one that moved refs/stash.
        var below = RevParse(log, StashRef);
        if (below is null)
            return Done(StashKind.Failed, [], null, "could not read the stash; nothing was stashed", log);
        var message = request.Message?.Trim();
        var args = string.IsNullOrEmpty(message)
            ? new[] { GitArgs.Stash, "push", "-u" }
            : new[] { GitArgs.Stash, "push", "-u", "-m", message };
        var push = Call(log, args);

        var top = RevParse(log, StashRef);
        if (top is null || top.Length == 0 || top == below)
        {
            var reason = push.Success ? "nothing was stashed" : FirstLine(push) + "; nothing was stashed";
            return Done(StashKind.Failed, [], null, reason, log);
        }

        // 3. Verify from the stash commit and the tree. The entry must sit on HEAD, and the tree must now be clean.
        var parent = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, top + "^1");
        if (!parent.Success || parent.StdOut.Trim() != check.Head)
            return Done(StashKind.Failed, [], null,
                "the stash entry was made, but it is not on top of HEAD; check git stash list", log);

        var after = Call(log, StatusArgs);
        if (!after.Success)
            return Done(StashKind.Failed, [], null,
                "your changes were saved in stash@{0}; could not read the status: " + GitClient.Describe(after), log);
        if (StatusParser.Parse(after.StdOut).Rows.Count > 0)
            return Done(StashKind.Failed, [], null,
                "your changes were saved in stash@{0} and may also still be in the working tree", log);

        // 4. The undo step, saved only now. The stash stands even when its step cannot be saved.
        var label = StashLabel(log);
        var undo = new UndoRecord(UndoKind.Stash, StashStepLabel, [], Head: check.Head, StashCommit: top, StashBelow: below);
        var undoProblem = new UndoLog(git).Push(undo, log);
        var count = before.ChangedPaths;
        var stashed = $"stashed {count} {(count == 1 ? "file" : "files")} as stash@{{0}}";
        return undoProblem is null
            ? Done(StashKind.Stashed, before.Rows, label, stashed, log, undo)
            : Done(StashKind.Stashed, before.Rows, label,
                $"{stashed}; could not save the undo step ({undoProblem}): u can't bring it back automatically", log);
    }

    /// <summary>What blocks a stash: a clean tree, conflicts, a nested repo or a submodule. Null when the tree may be stashed.</summary>
    internal static string? Refusal(WorkingStatus status)
    {
        if (status.HasConflicts)
            return ChangeRefusals.Conflicts;
        if (status.Rows.Count == 0)
            return ChangeRefusals.NothingToStash;
        if (status.Rows.FirstOrDefault(f => f.IsNestedRepo) is { } nested)
            return ChangeRefusals.NestedRepoStash(nested.Path);
        if (status.Rows.FirstOrDefault(f => f.IsSubmodule) is { } sub)
            return ChangeRefusals.Submodule(sub.Path);
        // git add -N makes an index entry with no content, which `git stash push -u` cannot save: git fails with "Entry not uptodate".
        if (status.Unstaged.FirstOrDefault(f => f.Kind == ChangeKind.Added) is { } intent)
            return ChangeRefusals.IntentToAddStash(intent.Path);
        return null;
    }

    /// <summary>The stash's own subject, such as "On main: wip", or null when git cannot say.</summary>
    private string? StashLabel(List<GitResult> log)
    {
        var r = Call(log, GitArgs.Stash, "list", "-n", "1", "--format=%gs");
        if (!r.Success)
            return null;
        var line = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? null : line;
    }

    /// <summary>The hash of a ref, "" when the ref does not exist, or null when git fails.</summary>
    private string? RevParse(List<GitResult> log, string name)
    {
        var r = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, name);
        if (r.Success)
            return r.StdOut.Trim();
        return r.ExitCode == 1 ? "" : null;
    }

    private static StashCheck Refused(string reason, List<GitResult> log) =>
        new(reason, [], "", "", log);

    private static StashOutcome Done(StashKind kind, IReadOnlyList<FileChange> moved, string? label, string message, List<GitResult> log,
        UndoRecord? undo = null) =>
        new(kind, moved, label, message, log, undo);

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private static string FirstLine(GitResult r)
    {
        var lines = r.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault() ?? $"git exited with {r.ExitCode}";
    }
}
