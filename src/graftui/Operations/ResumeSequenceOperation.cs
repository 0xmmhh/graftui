using System.Text;
using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Continues, skips, aborts or re-words a rebase that graftui paused.
/// Check reads the pause and refuses whatever the pause does not allow: unresolved conflicts, unstaged changes at an edit stop, a skip
/// that is not a conflict, a conflicted file with no markers that the user has not said to keep, a message for a commit that is not
/// waiting for one. Run runs the check again, makes the git call the action needs, saves the state and settles the result with the
/// classifier that Run uses. Run never throws.
/// A sequence that graftui did not start is never touched: it is refused as "a rebase is in progress".
/// </summary>
public sealed class ResumeSequenceOperation(GitClient git) : IGitOperation<ResumeRequest, ResumeCheck, SequenceOutcome>
{
    /// <summary>What Check found: the check itself, the state file it read (null when there is none) and the sequence's kind.</summary>
    private sealed record Probe(ResumeCheck Check, SequenceState? State, SequenceKind Op);

    /// <summary>Preconditions for the action on the paused step. Read-only apart from the logged reads; never throws.</summary>
    public ResumeCheck Check(ResumeRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return Inspect(request, log).Check;
        }
        catch (Exception ex)
        {
            return new ResumeCheck(ex.Message, null, "", log);
        }
    }

    /// <summary>The action. Re-runs Check first. Never throws.</summary>
    public SequenceOutcome Run(ResumeRequest request, ResumeCheck check)
    {
        var log = new List<GitResult>();
        Probe probe;
        try
        {
            probe = Inspect(request, log);
        }
        catch (Exception ex)
        {
            return Failed(null, SequenceKind.Rebase, ex.Message, log);
        }

        var state = probe.State;
        if (!probe.Check.CanRun || state is null)
            return Refused(state, probe.Op, probe.Check.Refusal ?? SequenceRefusals.NothingPaused, log);

        try
        {
            return Act(request, probe, state, log);
        }
        catch (Exception ex)
        {
            return Failed(state, probe.Op, ex.Message, log);
        }
    }

    /// <summary>The action itself, after Check has allowed it.</summary>
    private SequenceOutcome Act(ResumeRequest request, Probe probe, SequenceState state, List<GitResult> log)
    {
        var gitDir = probe.Check.GitDir;
        if (request.Action == ResumeAction.Abort)
            return Abort(state, gitDir, log);

        var stop = probe.Check.Stop;
        if (stop is null)
            return Refused(state, probe.Op, ChangeRefusals.InProgress(WordFor(probe.Op)), log);

        GitResult last;
        switch (request.Action)
        {
            case ResumeAction.Continue when stop.Pause == PauseKind.Conflict:
            {
                // Check let these through only once no text file has markers, and every file without markers is one the user said to keep.
                // A file the work tree holds is staged as it is; a file it does not hold is staged as a deletion (git rm).
                var files = stop.Files.Select(f => f.Path).ToList();
                var present = files.Where(f => SequenceGit.InWorkTree(Path.Combine(git.RepoPath, f))).ToList();
                var absent = files.Where(f => !present.Contains(f)).ToList();
                if (present.Count > 0)
                {
                    var add = git.RunLiteral(["add", "--", .. present]);
                    log.Add(add);
                    if (!add.Success)
                        return Failed(state, probe.Op, BranchGit.FirstLine(add), log);
                }
                if (absent.Count > 0)
                {
                    var remove = git.RunLiteral(["rm", "-q", "--", .. absent]);
                    log.Add(remove);
                    if (!remove.Success)
                        return Failed(state, probe.Op, BranchGit.FirstLine(remove), log);
                }
                state.Resolved.AddRange(files.Where(f => !state.Resolved.Contains(f)));
                last = SequenceCall(log, probe.Op, "continue");
                break;
            }
            case ResumeAction.Continue:
            {
                // An edit stop: git amends the staged changes itself when the sequence continues.
                if (HasStagedChanges(log))
                    state.Amended.Add(stop.Commit);
                last = SequenceCall(log, probe.Op, "continue");
                break;
            }
            case ResumeAction.AmendAndContinue:
            {
                if (HasStagedChanges(log))
                {
                    var amend = BranchGit.Call(git, log, "commit", "--amend", "--no-edit");
                    if (!amend.Success)
                        return Failed(state, probe.Op, BranchGit.FirstLine(amend), log);
                    state.Amended.Add(stop.Commit);
                }
                last = SequenceCall(log, probe.Op, "continue");
                break;
            }
            case ResumeAction.Skip:
            {
                last = SequenceCall(log, probe.Op, "skip");
                // A pick or a rebase can skip and then stop on the next commit, which exits 1 too: the skip happened all the same.
                var skipped = last.Success
                    || (state.Kind == SequenceKind.CherryPick
                        ? SequenceGit.PickMovedOn(git, log, stop.Commit)
                        : SequenceGit.RebaseMovedOn(git, gitDir, state, log, stop.Commit));
                if (skipped)
                    state.Skipped.Add(stop.Commit);
                break;
            }
            case ResumeAction.Message:
            {
                var (subject, body, blank) = CommitMessage.Split(request.Message ?? "");
                var amend = AmendMessage(gitDir, CommitMessage.Build(subject, body, blank), log);
                if (!amend.Success)
                    return Failed(state, probe.Op, BranchGit.FirstLine(amend), log);
                if (stop.Pause == PauseKind.Reword)
                    state.Reworded.Add(stop.Commit);
                last = SequenceCall(log, probe.Op, "continue");
                break;
            }
            default:
                return Refused(state, probe.Op, SequenceRefusals.PauseChanged, log);
        }

        // Settle saves the state again while the sequence is paused, or deletes it when the sequence ends.
        return SequenceGit.Settle(git, gitDir, state, last, log);
    }

    /// <summary>
    /// Ends the sequence: git's abort, then the branch and HEAD are verified against the state. The state file goes only when both match.
    /// </summary>
    private SequenceOutcome Abort(SequenceState state, string gitDir, List<GitResult> log)
    {
        var abort = SequenceCall(log, state.Kind, "abort");
        if (!abort.Success)
            return Failed(state, state.Kind, BranchGit.FirstLine(abort), log);

        var symbolic = BranchGit.SymbolicHead(git, log);
        var head = BranchGit.RefOrEmpty(git, log, "HEAD");
        if (symbolic != BranchGit.HeadsPrefix + state.Branch || head != state.OldTip)
            return Failed(state, state.Kind, $"the abort did not restore {state.Branch}; check git status", log);

        SequenceStateFile.Delete(gitDir);
        return SequenceGit.Outcome(SequenceOutcomeKind.Aborted, state.Kind, state.Branch, state.Onto, state.OldTip,
            $"{WordFor(state.Kind)} aborted", log, newTip: head, s: state);
    }

    /// <summary>
    /// Reads the pause and the checks of every action. Nothing is written; the git calls here are reads (the pause, status, the staged diff).
    /// </summary>
    private Probe Inspect(ResumeRequest request, List<GitResult> log)
    {
        Probe Refuse(string refusal, SequenceKind op, string gitDir) =>
            new(new ResumeCheck(refusal, null, gitDir, log), null, op);

        var gitDirPath = RepoState.GitDir(git, log);
        if (gitDirPath is null)
            return Refuse("could not find the git dir", SequenceKind.Rebase, "");

        var kind = RepoState.InProgress(args => BranchGit.Call(git, log, args), gitDirPath);
        var op = kind == "cherry-pick" ? SequenceKind.CherryPick : SequenceKind.Rebase;
        if (kind is null)
            return Refuse(SequenceRefusals.NothingPaused, op, gitDirPath);

        // Not graftui's sequence (no state, another kind, or git's files do not match the state): graftui does not touch it.
        var state = SequenceOwnership.Owned(gitDirPath, kind);
        if (state is null)
            return Refuse(ChangeRefusals.InProgress(kind), op, gitDirPath);

        var stop = state.Kind == SequenceKind.CherryPick
            ? SequenceGit.ReadPickStop(git, state, log)
            : SequenceGit.ReadRebaseStop(git, gitDirPath, state, log);
        var refusal = Gate(request, stop, op, log, out var ask);
        return new Probe(new ResumeCheck(refusal, stop, gitDirPath, log, ask), state, op);
    }

    /// <summary>
    /// What the pause allows. Null when the action may run. Ask is set only when the refusal is the question about marker-less files: the
    /// files whose version the user must say to keep.
    /// </summary>
    private string? Gate(ResumeRequest request, SequenceStop? stop, SequenceKind op, List<GitResult> log, out IReadOnlyList<KeptFile>? ask)
    {
        ask = null;
        if (request.Action == ResumeAction.Abort)
            return null;
        if (stop is null)
            return ChangeRefusals.InProgress(WordFor(op));
        if (request.ExpectedCommit is not null && request.ExpectedCommit != stop.Commit)
            return SequenceRefusals.PauseChanged;

        switch (request.Action)
        {
            case ResumeAction.Continue when stop.Pause == PauseKind.Conflict:
            {
                // A submodule or a folder is refused first: no question is asked about either, and git add would stage a folder's contents.
                var stuck = UnstageableRefusal(stop);
                if (stuck is not null)
                    return stuck;

                // Marker files are refused next: the question about the others is asked only once every text file is free of markers.
                var marked = MarkedPaths(stop);
                if (marked.Count > 0)
                    return SequenceRefusals.Unresolved(marked);
                return KeepGate(request, stop, log, out ask);
            }
            case ResumeAction.Continue when stop.Pause == PauseKind.Edit:
            case ResumeAction.AmendAndContinue when stop.Pause == PauseKind.Edit:
            {
                var (problem, unstaged) = Unstaged(log);
                if (problem is not null)
                    return problem;
                return unstaged.Count > 0 ? SequenceRefusals.StageFirst(unstaged[0]) : null;
            }
            case ResumeAction.Skip when stop.Pause == PauseKind.Conflict:
                return null;
            case ResumeAction.Skip:
                return SequenceRefusals.SkipOnlyConflict;
            case ResumeAction.Message when stop.Pause is PauseKind.Reword or PauseKind.Squash:
            {
                var subject = CommitMessage.Split(request.Message ?? "").Subject;
                return subject.Length == 0 ? ChangeRefusals.EmptySubject : null;
            }
            default:
                // Continue or amend at a reword or squash pause, or a message at any other pause: the screen never sends these.
                return SequenceRefusals.PauseChanged;
        }
    }

    /// <summary>
    /// The refusal for a conflicted path that cannot be staged as a file: a submodule, or a folder in the work tree. Null when every path
    /// can be staged (or removed). No question is asked about these.
    /// </summary>
    private string? UnstageableRefusal(SequenceStop stop)
    {
        foreach (var f in stop.Files)
        {
            if (f.Kind == ConflictKind.Submodule)
                return SequenceRefusals.Submodule(f.Path);
            if (SequenceGit.IsFolder(Path.Combine(git.RepoPath, f.Path)))
                return SequenceRefusals.Folder(f.Path);
        }
        return null;
    }

    /// <summary>
    /// The conflicted files that still hold conflict markers, whatever their kind: git can write markers into a file whose pair diffs as
    /// binary (a <c>-diff</c> attribute), and a user can leave markers in a one-sided file. Those are refused before any question.
    /// HasMarkers skips missing paths, folders and links.
    /// </summary>
    private List<string> MarkedPaths(SequenceStop stop)
    {
        var marked = new List<string>();
        foreach (var f in stop.Files)
        {
            if (f.Kind != ConflictKind.Submodule && SequenceGit.HasMarkers(Path.Combine(git.RepoPath, f.Path)))
                marked.Add(f.Path);
        }
        return marked;
    }

    /// <summary>
    /// The files that need a version chosen, each with the side the work tree holds now: every file that is not text (binary, a link, a
    /// one-sided add or a delete), and every text file the work tree holds as one side's bytes or not at all (a merge=binary, a custom driver,
    /// or a file deleted by hand). A text file edited to something else is resolved and is not asked about. Nothing is asked when there are
    /// none and no answer was given, or when the answer (request.Keep) names exactly these sides and versions. An answer that no longer
    /// matches is never used: with files still to ask about, ask is set to them; with none left (the user resolved them by hand after the
    /// question), the step is refused so the next Enter decides on what is there now.
    /// </summary>
    private string? KeepGate(ResumeRequest request, SequenceStop stop, List<GitResult> log, out IReadOnlyList<KeptFile>? ask)
    {
        ask = null;
        var fresh = new List<KeptFile>();
        foreach (var f in stop.Files)
        {
            var side = SequenceGit.WorkTreeSide(git, log, f, out var version);
            if (f.Kind != ConflictKind.Text || side != TreeSide.Edited)
                fresh.Add(new KeptFile(f.Path, f.Kind, side, PinnedVersion(f.Path, side, version)));
        }
        if (Confirmed(request.Keep, fresh))
            return null;
        if (fresh.Count == 0)
        {
            if (request.Keep is null || request.Keep.Count == 0)
                return null;
            return SequenceRefusals.KeepChanged;
        }

        ask = fresh;
        return SequenceRefusals.NoMarkers(fresh.Select(k => k.Path).ToList());
    }

    /// <summary>
    /// What an answer is pinned to besides the side: the entry type of a link ("link:"), an Edited entry's content, and a regular file's
    /// executable bit ("+x"). git add records all three, so a change to any of them after the question is asked about again. "" for a
    /// plain, non-executable Ours, Theirs or Deleted file.
    /// </summary>
    private string PinnedVersion(string path, TreeSide side, string version)
    {
        var full = Path.Combine(git.RepoPath, path);
        var pinned = "";
        if (SequenceGit.IsLink(full))
            pinned = "link:";
        if (side == TreeSide.Edited)
            pinned += version;
        if (IsExecutable(full))
            pinned += "+x";
        return pinned;
    }

    /// <summary>True for a regular file (not a link) whose owner may run it. Always false on Windows, where git does not use the bit.</summary>
    private static bool IsExecutable(string fullPath)
    {
        if (OperatingSystem.IsWindows() || !SequenceGit.HoldsFile(fullPath))
            return false;
        return (File.GetUnixFileMode(fullPath) & UnixFileMode.UserExecute) != 0;
    }

    /// <summary>
    /// True when the answer names exactly the sides that the work tree holds now. A side that moved since the question (the user replaced the
    /// file) makes it false, so the question is asked again with the new side.
    /// </summary>
    private static bool Confirmed(IReadOnlyList<KeptFile>? keep, IReadOnlyList<KeptFile> fresh)
    {
        if (keep is null || keep.Count == 0 || keep.Count != fresh.Count)
            return false;
        foreach (var k in fresh)
        {
            if (!keep.Contains(k))
                return false;
        }
        return true;
    }

    private static SequenceOutcome Refused(SequenceState? state, SequenceKind op, string refusal, List<GitResult> log) =>
        SequenceGit.Outcome(SequenceOutcomeKind.Refused, op, state?.Branch ?? "", state?.Onto ?? "", state?.OldTip ?? "", refusal, log, s: state);

    private static SequenceOutcome Failed(SequenceState? state, SequenceKind op, string message, List<GitResult> log) =>
        SequenceGit.Outcome(SequenceOutcomeKind.Failed, op, state?.Branch ?? "", state?.Onto ?? "", state?.OldTip ?? "", message, log, s: state);

    /// <summary>
    /// The paths with an unstaged side (edits, deletes, untracked files), in git's order. Problem is git's text when status cannot be read.
    /// </summary>
    private (string? Problem, IReadOnlyList<string> Paths) Unstaged(List<GitResult> log)
    {
        var r = BranchGit.Call(git, log, BranchGit.StatusArgs);
        if (!r.Success)
            return (GitClient.Describe(r), []);
        return (null, StatusParser.Parse(r.StdOut).Unstaged.Select(f => f.Path).ToList());
    }

    /// <summary>True when the index differs from HEAD. The diff runs through the log like every other call.</summary>
    private bool HasStagedChanges(List<GitResult> log) => BranchGit.Call(git, log, "diff", "--cached", "--quiet").ExitCode == 1;

    /// <summary>`git rebase --continue` (or skip, abort) with the sequence's own word, through the logged call.</summary>
    private GitResult SequenceCall(List<GitResult> log, SequenceKind op, string verb) =>
        BranchGit.Call(git, log, op == SequenceKind.Rebase ? "rebase" : "cherry-pick", "--" + verb);

    /// <summary>
    /// Writes the message to a temporary file in the git dir and amends the commit the sequence stopped at. The file is removed
    /// whatever happens.
    /// </summary>
    private GitResult AmendMessage(string gitDir, string text, List<GitResult> log)
    {
        var path = Path.Combine(gitDir, SequenceStateFile.MessageFile);
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return BranchGit.Call(git, log, "commit", "--amend", "--only", "--cleanup=whitespace", "-F", RepoState.ArgPath(git, path));
        }
        finally
        {
            SequenceGit.DeleteQuietly(path);
        }
    }

    private static string WordFor(SequenceKind op) => op == SequenceKind.Rebase ? "rebase" : "cherry-pick";
}
