using System.Text;
using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Interactive rebase of the current branch onto another branch.
/// Check reads the branch's own commits and refuses what the rebase cannot do, and it validates the user's plan. Run writes the plan as
/// a todo list and runs `git rebase -i` with a sequence editor that copies that list, so no editor ever opens. Then it settles the
/// result: done, paused at a stop (conflict, edit, reword or squash), or failed. Run never throws.
/// A reword is an edit stop, and a squash is a fixup followed by a break. The message is written by the resume operation.
/// </summary>
public sealed class RebaseOperation(GitClient git) : IGitOperation<RebaseRequest, RebaseCheck, SequenceOutcome>
{
    /// <summary>Preconditions for rebasing the current branch onto request.Onto. Read-only; every call is logged.</summary>
    public RebaseCheck Check(RebaseRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckInto(request, log);
        }
        catch (Exception ex)
        {
            return new RebaseCheck(ex.Message, "", request.Onto, "", "", [], "", log);
        }
    }

    /// <summary>The rebase. Never throws. Done saves one undo step; a pause keeps the state file for the resume operation.</summary>
    public SequenceOutcome Run(RebaseRequest request, RebaseCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Refused(check, check.Refusal!, log);

        var started = false;
        var gitDir = check.GitDir;
        try
        {
            // Everything must still hold at the moment git starts: the same HEAD, and the plan's own checks run again.
            var again = CheckInto(request with { ExpectedHead = check.Head }, log);
            if (!again.CanRun)
                return Refused(check, again.Refusal!, log);

            var state = new SequenceState(1, SequenceKind.Rebase, check.Branch, check.Onto, check.Head, check.Rows, [], [], [], [], [], []);
            var saved = SequenceStateFile.Save(gitDir, state);
            if (saved is not null)
                return Failed(check, $"could not save the rebase state: {saved}", log);

            var todoPath = Path.Combine(gitDir, SequenceStateFile.TodoFile);
            try
            {
                File.WriteAllText(todoPath, TodoText(check.Rows), new UTF8Encoding(false));
                started = true;
                var env = new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = "cp " + SequenceGit.ShellQuote(todoPath) };
                var last = git.RunWithEnv(env, "rebase", "-i", "--no-autosquash", "--empty=drop", check.OntoTip);
                log.Add(last);
                return SequenceGit.Settle(git, gitDir, state, last, log);
            }
            finally
            {
                SequenceGit.DeleteQuietly(todoPath);
            }
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (!started)
            {
                // Nothing reached git: the state written for this run is not needed.
                SequenceStateFile.Delete(gitDir);
            }
            else if (SequenceGit.RebaseInProgress(gitDir))
            {
                // Git holds a rebase that graftui started: abort it so the branch is exactly as before.
                var abort = BranchGit.Call(git, log, "rebase", "--abort");
                if (abort.Success)
                {
                    SequenceStateFile.Delete(gitDir);
                    message += "; the rebase was aborted";
                }
                else
                {
                    message += "; the rebase could not be aborted: check git status";
                }
            }
            return Failed(check, message, log);
        }
    }

    /// <summary>The checks of Check, run into <paramref name="log"/> so Run can keep one log for the whole operation.</summary>
    private RebaseCheck CheckInto(RebaseRequest request, List<GitResult> log)
    {
        var onto = request.Onto;
        RebaseCheck Refuse(string refusal, string branch = "", string ontoTip = "", string head = "", string gitDir = "") =>
            new(refusal, branch, onto, ontoTip, head, [], gitDir, log);

        var gitDirPath = RepoState.GitDir(git, log);
        if (gitDirPath is null)
            return Refuse("could not find the git dir");

        // Before the detached-HEAD check: a rebase that stopped for an edit has a detached HEAD, and the reason to refuse is the rebase.
        var inProgress = RepoState.InProgress(args => BranchGit.Call(git, log, args), gitDirPath);
        if (inProgress is not null)
            return Refuse(ChangeRefusals.InProgress(inProgress), gitDir: gitDirPath);

        var symbolic = BranchGit.SymbolicHead(git, log);
        if (symbolic.Length == 0)
            return Refuse(ChangeRefusals.Detached, gitDir: gitDirPath);
        var branch = GitClient.StripHeads(symbolic);

        var dirtyProblem = SequenceGit.Dirty(git, log, out var dirtyPaths);
        if (dirtyProblem is not null)
            return Refuse(dirtyProblem, branch, gitDir: gitDirPath);
        if (dirtyPaths.Count > 0)
            return Refuse(SequenceRefusals.RebaseDirty(dirtyPaths), branch, gitDir: gitDirPath);

        if (BranchGit.IsUnsafeName(onto))
            return Refuse(BranchRefusals.NoBranch(onto), branch, gitDir: gitDirPath);
        if (onto == branch)
            return Refuse(SequenceRefusals.OntoSelf(branch), branch, gitDir: gitDirPath);

        var ontoTip = BranchGit.RefOrEmpty(git, log, BranchGit.HeadsPrefix + onto + "^{commit}");
        if (ontoTip.Length == 0)
            return Refuse(BranchRefusals.NoBranch(onto), branch, gitDir: gitDirPath);

        var head = BranchGit.RefOrEmpty(git, log, "HEAD");
        // An unborn branch has no commits of its own to rebase.
        if (head.Length == 0)
            return Refuse(SequenceRefusals.NoOwnCommits(branch, onto), branch, ontoTip, head, gitDirPath);
        if (request.ExpectedHead is not null && request.ExpectedHead != head)
            return Refuse(SequenceRefusals.Moved(branch), branch, ontoTip, head, gitDirPath);

        if (BranchGit.IsAncestor(git, log, ontoTip, head))
            return Refuse(SequenceRefusals.AlreadyOnTop(branch, onto), branch, ontoTip, head, gitDirPath);

        var mergeBase = BranchGit.Call(git, log, "merge-base", ontoTip, head);
        if (!mergeBase.Success && mergeBase.ExitCode != 1)
            throw new InvalidOperationException(GitClient.Describe(mergeBase));
        if (!mergeBase.Success)
            return Refuse(SequenceRefusals.Unrelated(branch, onto), branch, ontoTip, head, gitDirPath);

        var range = ontoTip + ".." + head;
        if (BranchGit.Count(git, log, range) == 0)
            return Refuse(SequenceRefusals.NoOwnCommits(branch, onto), branch, ontoTip, head, gitDirPath);
        if (BranchGit.Count(git, log, "--merges", range) > 0)
            return Refuse(SequenceRefusals.HasMerges(branch, onto), branch, ontoTip, head, gitDirPath);

        var rows = ReadRows(range, log);
        if (request.Plan is not null)
        {
            if (!SameHashes(request.Plan, rows))
                return Refuse(SequenceRefusals.PlanStale(branch), branch, ontoTip, head, gitDirPath);

            var planRefusal = PlanRefusal(request.Plan, branch);
            if (planRefusal is not null)
                return Refuse(planRefusal, branch, ontoTip, head, gitDirPath);
        }

        return new RebaseCheck(null, branch, onto, ontoTip, head, request.Plan ?? rows, gitDirPath, log);
    }

    /// <summary>The commits of a range, oldest first, each a pick. These are the default plan. Throws when git fails.</summary>
    private List<RebaseRow> ReadRows(string range, List<GitResult> log)
    {
        var r = BranchGit.Call(git, log, "log", "--reverse", "--no-show-signature", "--format=%H%x1f%h%x1f%s", range, "--");
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));
        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\x1f', 3))
            .Where(f => f.Length == 3)
            .Select(f => new RebaseRow(f[0], f[1], f[2], RebaseAction.Pick))
            .ToList();
    }

    /// <summary>The plan's refusal, or null. The plan walks oldest first: a fold needs a kept commit above it, and at least one commit must be kept.</summary>
    private static string? PlanRefusal(IReadOnlyList<RebaseRow> plan, string branch)
    {
        var kept = 0;
        foreach (var row in plan)
        {
            switch (row.Action)
            {
                case RebaseAction.Drop:
                    break;
                case RebaseAction.Squash:
                case RebaseAction.Fixup:
                    if (kept == 0)
                        return SequenceRefusals.FoldFirst(row.Action == RebaseAction.Squash ? "squash" : "fixup");
                    break;
                default:
                    kept++;
                    break;
            }
        }
        return kept == 0 ? SequenceRefusals.EmptyPlan(branch) : null;
    }

    private static bool SameHashes(IReadOnlyList<RebaseRow> plan, IReadOnlyList<RebaseRow> rows) =>
        plan.Select(r => r.Hash).Order(StringComparer.Ordinal).SequenceEqual(rows.Select(r => r.Hash).Order(StringComparer.Ordinal));

    /// <summary>
    /// The todo list for git. Pick is pick; reword and edit are edit (a stop); squash and fixup are fixup. After the last fold of a
    /// group that holds a squash comes a break, so git stops there and graftui writes the combined message.
    /// </summary>
    private static string TodoText(IReadOnlyList<RebaseRow> rows)
    {
        var lines = new List<string>();
        var squash = false;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            lines.Add(TodoWord(row.Action) + " " + row.Hash);
            if (!IsFold(row.Action))
                continue;

            squash |= row.Action == RebaseAction.Squash;
            var endsGroup = i + 1 >= rows.Count || !IsFold(rows[i + 1].Action);
            if (endsGroup)
            {
                if (squash)
                    lines.Add("break");
                squash = false;
            }
        }
        return string.Join('\n', lines) + "\n";
    }

    private static string TodoWord(RebaseAction action) => action switch
    {
        RebaseAction.Pick => "pick",
        RebaseAction.Reword or RebaseAction.Edit => "edit",
        RebaseAction.Squash or RebaseAction.Fixup => "fixup",
        _ => "drop",
    };

    private static bool IsFold(RebaseAction action) => action is RebaseAction.Squash or RebaseAction.Fixup;

    private static SequenceOutcome Refused(RebaseCheck check, string refusal, List<GitResult> log) =>
        SequenceGit.Outcome(SequenceOutcomeKind.Refused, SequenceKind.Rebase, check.Branch, check.Onto, check.Head, refusal, log);

    private static SequenceOutcome Failed(RebaseCheck check, string message, List<GitResult> log) =>
        SequenceGit.Outcome(SequenceOutcomeKind.Failed, SequenceKind.Rebase, check.Branch, check.Onto, check.Head, message, log);
}
