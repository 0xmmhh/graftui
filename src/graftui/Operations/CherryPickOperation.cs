using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Copies the selected commits onto the current branch (cherry-pick). Check reads each commit and refuses what a pick cannot do:
/// a commit already on the branch, a change the branch already has under another hash, a merge commit, or a dirty tree. The picks are
/// ordered oldest first, whatever order they were selected in. Run writes the state, runs `git cherry-pick` on the picks and settles the
/// result: done, paused on a conflict, or failed. An empty pick is skipped and reported. Run never throws.
/// </summary>
public sealed class CherryPickOperation(GitClient git) : IGitOperation<CherryPickRequest, CherryPickCheck, SequenceOutcome>
{
    /// <summary>Preconditions for picking the commits onto the current branch. Reads git, except that it removes a stale state file; every call is logged.</summary>
    public CherryPickCheck Check(CherryPickRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckInto(request, log);
        }
        catch (Exception ex)
        {
            return new CherryPickCheck(ex.Message, "", "", [], "", log);
        }
    }

    /// <summary>The picks. Never throws. Done saves one undo step; a pause keeps the state file for the resume operation.</summary>
    public SequenceOutcome Run(CherryPickRequest request, CherryPickCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Refused(check, check.Refusal!, log);

        var started = false;
        var gitDir = check.GitDir;
        try
        {
            // Everything must still hold at the moment git starts: the same HEAD, and the picks are checked again.
            var again = CheckInto(request with { ExpectedHead = check.Head }, log);
            if (!again.CanRun)
                return Refused(check, again.Refusal!, log);

            var picks = again.Ordered.Select(c => c.Hash).ToList();
            var state = new SequenceState(1, SequenceKind.CherryPick, again.Branch, "", again.Head, [], picks, [], [], [], [], []);
            var saved = SequenceStateFile.Save(gitDir, state);
            if (saved is not null)
                return Failed(check, $"could not save the cherry-pick state: {saved}", log);

            started = true;
            var last = BranchGit.Call(git, log, ["cherry-pick", .. picks]);
            return SequenceGit.Settle(git, gitDir, state, last, log);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (!started)
            {
                // Nothing reached git: the state written for this run is not needed.
                SequenceStateFile.Delete(gitDir);
            }
            else if (SequenceGit.PickInProgress(gitDir))
            {
                // Git holds a pick that graftui started: abort it so the branch is exactly as before.
                var abort = BranchGit.Call(git, log, "cherry-pick", "--abort");
                if (abort.Success)
                {
                    SequenceStateFile.Delete(gitDir);
                    message += "; the cherry-pick was aborted";
                }
                else
                {
                    message += "; the cherry-pick could not be aborted: check git status";
                }
            }
            return Failed(check, message, log);
        }
    }

    /// <summary>The checks of Check, run into <paramref name="log"/> so Run can keep one log for the whole operation.</summary>
    private CherryPickCheck CheckInto(CherryPickRequest request, List<GitResult> log)
    {
        CherryPickCheck Refuse(string refusal, string branch = "", string head = "", string gitDir = "") =>
            new(refusal, branch, head, [], gitDir, log);

        var gitDirPath = RepoState.GitDir(git, log);
        if (gitDirPath is null)
            return Refuse("could not find the git dir");

        // Before the detached-HEAD check, as in the rebase: a rebase that stopped for an edit has a detached HEAD, and the reason is the rebase.
        var inProgress = RepoState.InProgress(args => BranchGit.Call(git, log, args), gitDirPath);
        if (inProgress is not null)
            return Refuse(ChangeRefusals.InProgress(inProgress), gitDir: gitDirPath);

        // A state file left by a sequence that is no longer in progress is stale: it goes before these picks start.
        SequenceStateFile.Delete(gitDirPath);

        var symbolic = BranchGit.SymbolicHead(git, log);
        if (symbolic.Length == 0)
            return Refuse(ChangeRefusals.Detached, gitDir: gitDirPath);
        var branch = GitClient.StripHeads(symbolic);

        var dirtyProblem = SequenceGit.Dirty(git, log, out var dirtyPaths);
        if (dirtyProblem is not null)
            return Refuse(dirtyProblem, branch, gitDir: gitDirPath);
        if (dirtyPaths.Count > 0)
            return Refuse(SequenceRefusals.PickDirty(dirtyPaths), branch, gitDir: gitDirPath);

        var head = BranchGit.RefOrEmpty(git, log, "HEAD");
        if (request.Commits.Count == 0)
            return Refuse(SequenceRefusals.NothingSelected, branch, head, gitDirPath);

        // Each selected commit must exist, be off the branch, and not be a merge. Repeats of one commit count once.
        var infos = new Dictionary<string, CommitInfo>(StringComparer.Ordinal);
        var fulls = new List<string>();
        foreach (var text in request.Commits)
        {
            var full = BranchGit.RefOrEmpty(git, log, text + "^{commit}");
            var info = full.Length > 0 ? git.GetCommit(full) : null;
            if (info is null)
                return Refuse(SequenceRefusals.NoCommit(text), branch, head, gitDirPath);
            if (BranchGit.IsAncestor(git, log, full, head))
                return Refuse(SequenceRefusals.OnBranch(branch), branch, head, gitDirPath);
            if (info.IsMerge)
                return Refuse(SequenceRefusals.MergeCommit(BranchGit.Short(full)), branch, head, gitDirPath);

            infos[full] = info;
            if (!fulls.Contains(full))
                fulls.Add(full);
        }

        // A commit whose change the branch already has is refused as a whole, so one twin stops the whole selection.
        foreach (var full in fulls)
        {
            var twin = AlreadyAppliedTwin(full, head, infos[full].ParentCount > 0, log);
            if (twin is not null)
                return Refuse(SequenceRefusals.AlreadyApplied(BranchGit.Short(full), branch, BranchGit.Short(twin)), branch, head, gitDirPath);
        }

        // Oldest first: the commits git lists for the range, in topological order, keeping only the selected ones.
        var listed = BranchGit.Call(git, log, ["rev-list", "--topo-order", "--reverse", .. fulls, "--not", "HEAD"]);
        if (!listed.Success)
            throw new InvalidOperationException(GitClient.Describe(listed));
        var wanted = fulls.ToHashSet(StringComparer.Ordinal);
        var ordered = listed.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(wanted.Contains)
            .Select(h => infos[h])
            .ToList();

        if (request.ExpectedHead is not null && request.ExpectedHead != head)
            return Refuse(SequenceRefusals.Moved(branch), branch, head, gitDirPath);

        return new CherryPickCheck(null, branch, head, ordered, gitDirPath, log);
    }

    /// <summary>
    /// The commit on the branch whose change is the same as <paramref name="full"/>'s, or null. git marks such a commit with '=' when the
    /// patches match. The commit's own parent is left out, so only its change is compared. Throws when git fails.
    /// </summary>
    private string? AlreadyAppliedTwin(string full, string head, bool hasParent, List<GitResult> log)
    {
        var args = new List<string> { "rev-list", "--left-only", "--cherry-mark", "--no-merges", head + "..." + full };
        if (hasParent)
            args.Add("^" + full + "^");

        var r = BranchGit.Call(git, log, [.. args]);
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));

        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith('='))?[1..];
    }

    private static SequenceOutcome Refused(CherryPickCheck check, string refusal, List<GitResult> log) =>
        SequenceGit.Outcome(SequenceOutcomeKind.Refused, SequenceKind.CherryPick, check.Branch, "", check.Head, refusal, log);

    private static SequenceOutcome Failed(CherryPickCheck check, string message, List<GitResult> log) =>
        SequenceGit.Outcome(SequenceOutcomeKind.Failed, SequenceKind.CherryPick, check.Branch, "", check.Head, message, log);
}
