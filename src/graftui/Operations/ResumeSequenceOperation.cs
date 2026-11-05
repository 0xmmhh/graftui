using System.Text;
using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Continues, skips, aborts or re-words a rebase that graftui paused.
/// Check reads the pause and refuses whatever the pause does not allow: unresolved conflicts, unstaged changes at an edit stop, a skip
/// that is not a conflict, a message for a commit that is not waiting for one. Run runs the check again, makes the git call the action
/// needs, saves the state and settles the result with the classifier that Run uses. Run never throws.
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
                var files = stop.Files.Select(f => f.Path).ToList();
                if (files.Count > 0)
                {
                    // Check let these through only once no file has conflict markers, so the resolved text is what gets added.
                    var add = git.RunLiteral(["add", "--", .. files]);
                    log.Add(add);
                    if (!add.Success)
                        return Failed(state, probe.Op, BranchGit.FirstLine(add), log);
                    state.Resolved.AddRange(files.Where(f => !state.Resolved.Contains(f)));
                }
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

        // A state for another sequence, or none: graftui does not touch a sequence it did not start.
        var state = SequenceStateFile.Load(gitDirPath);
        var expected = kind switch
        {
            "rebase" => SequenceKind.Rebase,
            "cherry-pick" => SequenceKind.CherryPick,
            _ => (SequenceKind?)null,
        };
        if (state is null || expected is null || state.Kind != expected)
            return Refuse(ChangeRefusals.InProgress(kind), op, gitDirPath);

        var stop = state.Kind == SequenceKind.CherryPick
            ? SequenceGit.ReadPickStop(git, state, log)
            : SequenceGit.ReadRebaseStop(git, gitDirPath, state, log);
        var refusal = Gate(request, stop, log);
        return new Probe(new ResumeCheck(refusal, stop, gitDirPath, log), state, op);
    }

    /// <summary>What the pause allows. Null when the action may run.</summary>
    private string? Gate(ResumeRequest request, SequenceStop? stop, List<GitResult> log)
    {
        if (request.Action == ResumeAction.Abort)
            return null;
        if (stop is null)
            return ChangeRefusals.InProgress("rebase");
        if (request.ExpectedCommit is not null && request.ExpectedCommit != stop.Commit)
            return SequenceRefusals.PauseChanged;

        switch (request.Action)
        {
            case ResumeAction.Continue when stop.Pause == PauseKind.Conflict:
            {
                var unresolved = stop.Files.Where(f => !f.Resolved).Select(f => f.Path).ToList();
                return unresolved.Count > 0 ? SequenceRefusals.Unresolved(unresolved) : null;
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
