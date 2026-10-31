using System.Text;
using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Commit and amend for the Changes tab. The message is written to GRAFTUI_MSG in the git dir and passed with -F,
/// so no editor can open. Hooks run as usual.
/// Check: CheckRepo (the repo state) plus the subject, the expected HEAD and the pushed-amend confirmation.
/// Run: re-checks HEAD and the pushed state, writes the message file, runs git commit, deletes the file, then verifies
/// the result from HEAD and its parents. Run never throws.
/// Rollback: never resets the index or HEAD. git commit either creates the commit and moves the ref, or does neither,
/// so the only cleanup is the message file. Afterwards, a hook that changed files is reported.
/// </summary>
public sealed class CommitOperation(GitClient git) : IGitOperation<CommitRequest, CommitCheck, CommitOutcome>
{
    public const string MessageFileName = "GRAFTUI_MSG";

    private const string RemotePrefix = "refs/remotes/";
    private static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    /// <summary>The checks that depend only on the repo: branch, HEAD, in-progress operations, and the staged rows.</summary>
    public CommitCheck CheckRepo(bool amend)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckRepoCore(amend, log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, log);
        }
    }

    public CommitCheck Check(CommitRequest request)
    {
        var c = CheckRepo(request.Amend);
        if (!c.CanRun)
            return c;

        var refusal = RefusalFor(request, c);
        return refusal is null ? c : c with { Refusal = refusal };
    }

    /// <summary>The first reason the commit must not run, in the order the screen reports them; null when it may run.</summary>
    private static string? RefusalFor(CommitRequest request, CommitCheck c)
    {
        if (request.Subject.Trim().Length == 0)
            return ChangeRefusals.EmptySubject;
        if (request.ExpectedHead is not null && request.ExpectedHead != c.OldHead)
            return ChangeRefusals.HeadMoved;
        if (request.Amend && c.PushedTo.Count > 0 && !request.PushedConfirmed)
            return ChangeRefusals.PushedNotConfirmed(c.PushedTo[0]);
        return null;
    }

    /// <summary>
    /// Whether the new commit has the parents it should. An amend keeps the old commit's parents. A new commit has the old
    /// HEAD as its only parent, or no parent when the branch had no commits yet.
    /// </summary>
    private static bool ParentsAsExpected(CommitRequest request, CommitCheck check, string[] newParents)
    {
        if (request.Amend)
            return newParents.SequenceEqual(check.OldParents);
        if (check.OldHead is null)
            return newParents.Length == 0;
        return newParents.Length > 0 && newParents[0] == check.OldHead;
    }

    public CommitOutcome Run(CommitRequest request, CommitCheck check)
    {
        var log = new List<GitResult>();
        var branch = check.Branch;
        CommitOutcome Done(CommitKind kind, string? newHead, CommitInfo? newCommit, string message, UndoRecord? undo = null) =>
            new(kind, branch, check.OldHead, newHead, newCommit, message, log, undo);

        if (!check.CanRun)
            return Done(CommitKind.Failed, null, null, check.Refusal ?? "nothing was committed");

        try
        {
            // 1. HEAD must still be where the check saw it. Nothing has been written yet.
            var head = Call(log, "rev-parse", "-q", "--verify", "HEAD");
            var current = head.Success ? head.StdOut.Trim() : null;
            if (current != check.OldHead)
                return Done(CommitKind.Failed, null, null, ChangeRefusals.HeadMoved);

            // 2. An amend must still be of an unpushed commit, or of one the user has confirmed.
            if (request.Amend)
            {
                var pushed = PushedTo(log);
                if (pushed is null)
                    return Done(CommitKind.Failed, null, null, "could not check whether the commit was pushed");
                if (pushed.Count > 0 && check.PushedTo.Count == 0)
                    return Done(CommitKind.Failed, null, null, "this commit was pushed meanwhile; press A again");
                if (pushed.Count > 0 && !request.PushedConfirmed)
                    return Done(CommitKind.Failed, null, null, ChangeRefusals.PushedNotConfirmed(pushed[0]));
            }

            // 3. The message goes to a file in the git dir. The path is shown relative to the repo, as .git/GRAFTUI_MSG.
            //    --cleanup=whitespace keeps every line that was typed, so a commit.cleanup=strip setting cannot drop a "#" line.
            var messagePath = Path.Combine(check.GitDir, MessageFileName);
            var messageArg = Path.GetRelativePath(git.RepoPath, messagePath);
            GitResult? commit = null;
            string? writeError = null;
            bool deleteFailed;
            try
            {
                File.WriteAllText(messagePath, CommitMessage.Build(request.Subject, request.Body, request.BlankAfterSubject), new UTF8Encoding(false));
                commit = request.Amend
                    ? Call(log, "commit", "--amend", "--cleanup=whitespace", "-F", messageArg)
                    : Call(log, "commit", "--cleanup=whitespace", "-F", messageArg);
            }
            catch (Exception ex)
            {
                writeError = ex.Message;
            }
            finally
            {
                deleteFailed = !TryDelete(messagePath);
            }
            var deleteNote = deleteFailed ? $"could not delete {messageArg}" : null;

            if (commit is null)
                return Done(CommitKind.Failed, null, null, Combine($"could not run git commit: {writeError}", deleteNote));

            // 4. Verify from HEAD and its parents, never from git's text.
            var after = Call(log, "rev-list", "--parents", "-n", "1", "HEAD");
            string[] tokens = after.Success ? after.StdOut.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries) : [];
            if (tokens.Length == 0)
                return Done(CommitKind.Failed, null, null, Combine("could not read HEAD after git commit; check git status", deleteNote));

            var newHead = tokens[0];
            var newParents = tokens.Skip(1).ToArray();
            var moved = newHead != check.OldHead;
            var shapeOk = moved && ParentsAsExpected(request, check, newParents);
            var kind = request.Amend ? CommitKind.Amended : CommitKind.Committed;

            if (commit.Success)
            {
                if (!moved)
                    return Done(CommitKind.Failed, null, null, Combine("git commit reported success but HEAD did not move", deleteNote));
                if (!shapeOk)
                    return Done(CommitKind.Failed, newHead, null, Combine($"HEAD moved to an unexpected commit {Short(newHead)}", deleteNote));
                return Succeeded(kind, branch, check, newHead, Combine(null, deleteNote), request.Amend, log);
            }

            if (shapeOk)
            {
                // The commit exists; only git's exit status is off (for example, interrupted after the ref moved).
                return Succeeded(kind, branch, check, newHead, Combine($"git exited {commit.ExitCode} after committing", deleteNote), request.Amend, log);
            }

            if (moved)
                return Done(CommitKind.Failed, newHead, null,
                    Combine($"git commit exited {commit.ExitCode} and HEAD moved to an unexpected commit {Short(newHead)}", deleteNote));

            var reason = $"{FirstLine(commit)}; nothing was committed, your staged changes are untouched";
            var st = Call(log, StatusArgs);
            if (st.Success && st.StdOut != check.RawStatus)
                reason += " (a hook changed files: check them)";
            return Done(CommitKind.Failed, null, null, Combine(reason, deleteNote));
        }
        catch (Exception ex)
        {
            return Done(CommitKind.Failed, null, null, ex.Message + "; repo state unknown, run git status");
        }
    }

    private CommitCheck CheckRepoCore(bool amend, List<GitResult> log)
    {
        // 1. Branch: symbolic-ref exits 1 quietly on a detached HEAD.
        var sym = Call(log, "symbolic-ref", "-q", "HEAD");
        if (sym.ExitCode == 1)
            return Refused(ChangeRefusals.Detached, log);
        if (!sym.Success)
            return Refused(GitClient.Describe(sym), log);
        var branch = GitClient.StripHeads(sym.StdOut.Trim());
        if (branch.Length == 0)
            return Refused(ChangeRefusals.Detached, log);

        // 2. HEAD: null on an unborn branch.
        var headResult = Call(log, "rev-parse", "-q", "--verify", "HEAD");
        if (!headResult.Success && headResult.ExitCode != 1)
            return Refused(GitClient.Describe(headResult), log, branch);
        var oldHead = headResult.Success ? headResult.StdOut.Trim() : null;
        if (amend && oldHead is null)
            return Refused(ChangeRefusals.NoCommitToAmend, log, branch);

        // 3. The git dir, relative to the repo root when git prints it that way.
        var gitDirResult = Call(log, "rev-parse", "--git-dir");
        if (!gitDirResult.Success)
            return Refused(GitClient.Describe(gitDirResult), log, branch);
        var gitDirOut = gitDirResult.StdOut.Trim();
        var gitDir = Path.IsPathRooted(gitDirOut) ? gitDirOut : Path.Combine(git.RepoPath, gitDirOut);

        // 4. Another operation that owns the index must finish first. Commits never create merge or rebase state.
        var inProgress = RepoState.InProgress(a => Call(log, a), gitDir);
        if (inProgress is not null)
            return Refused(ChangeRefusals.InProgress(inProgress), log, branch);

        // 5. Status: conflicts block everything; a commit needs something staged (an amend may change only the message).
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Refused(GitClient.Describe(st), log, branch);
        var status = StatusParser.Parse(st.StdOut);
        if (status.HasConflicts)
            return Refused(ChangeRefusals.Conflicts, log, branch);
        if (!amend && status.Staged.Count == 0)
            return Refused(ChangeRefusals.NothingStaged, log, branch);

        // 6. Amend only: the parents, the old message, and the remote branches that already have the commit.
        var oldParents = new List<string>();
        var oldMessage = "";
        var pushedTo = new List<string>();
        CommitInfo? oldCommit = null;
        if (amend)
        {
            var parents = Call(log, "rev-list", "--parents", "-n", "1", "HEAD");
            if (!parents.Success)
                return Refused(GitClient.Describe(parents), log, branch);
            oldParents = parents.StdOut.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

            var message = Call(log, "log", "-n", "1", "--no-show-signature", "--format=%B", "HEAD", "--");
            if (!message.Success)
                return Refused(GitClient.Describe(message), log, branch);
            oldMessage = message.StdOut;

            var remotes = PushedTo(log);
            if (remotes is null)
                return Refused("could not check which remote branches contain HEAD", log, branch);
            pushedTo = remotes;

            // A read for the screen, not a git command of the operation, so it is not logged.
            oldCommit = git.GetCommit(oldHead!);
        }

        return new CommitCheck(null, branch, oldHead, oldParents, oldCommit, oldMessage, pushedTo,
            status.Staged, st.StdOut, gitDir, log);
    }

    /// <summary>Remote-tracking branches that contain HEAD, as "origin/main". Null when git fails.</summary>
    private List<string>? PushedTo(List<GitResult> log)
    {
        var r = Call(log, "branch", "-r", "--contains", "HEAD", "--format=%(refname)%00%(symref)");
        if (!r.Success)
            return null;

        // Symbolic refs (origin/HEAD) have a non-empty second field and are skipped: they are not a second copy of a branch.
        var names = new List<string>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\0');
            if (parts.Length >= 2 && parts[1].Length > 0)
                continue;
            var name = parts[0];
            names.Add(name.StartsWith(RemotePrefix, StringComparison.Ordinal) ? name[RemotePrefix.Length..] : name);
        }
        return names;
    }

    /// <summary>
    /// A commit that landed. Its undo step names the one ref that moved and is saved now. If saving fails, the commit still
    /// stands: the note goes on the message and the outcome carries no undo step.
    /// </summary>
    private CommitOutcome Succeeded(CommitKind kind, string branch, CommitCheck check, string newHead, string message,
        bool amend, List<GitResult> log)
    {
        var newCommit = git.GetCommit(newHead);
        var undo = new UndoRecord(amend ? UndoKind.Amend : UndoKind.Commit, amend ? "amend" : "commit",
            [new RefChange("refs/heads/" + branch, check.OldHead ?? "", newHead)], Branch: branch, Head: newHead);
        var problem = new UndoLog(git).Push(undo, log);
        return problem is null
            ? new CommitOutcome(kind, branch, check.OldHead, newHead, newCommit, message, log, undo)
            : new CommitOutcome(kind, branch, check.OldHead, newHead, newCommit,
                Combine(message, $"could not save the undo step: {problem}"), log, null);
    }

    private static CommitCheck Refused(string reason, List<GitResult> log, string branch = "") =>
        new(reason, branch, null, [], null, "", [], [], "", "", log);

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FirstLine(GitResult r)
    {
        var lines = r.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault() ?? $"git commit exited {r.ExitCode}";
    }

    private static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash;

    private static string Combine(string? first, string? second) =>
        string.Join("; ", new[] { first, second }.Where(s => !string.IsNullOrEmpty(s)));
}
