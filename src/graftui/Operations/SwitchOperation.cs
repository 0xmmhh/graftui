using graftui.Git;
using graftui.Model;
using static graftui.Operations.BranchGit;

namespace graftui.Operations;

/// <summary>
/// Switches to another branch (s). Not undoable: a switch moves HEAD and any change that does not collide comes along.
/// Check refuses the current branch, a conflicted index, a merge in progress and, when HEAD is detached, commits that no branch holds.
/// Run runs `git switch --no-guess`, verifies HEAD from the refs, and when git refuses, names the changed paths that would
/// have been overwritten (classified from the paths, never from git's text). Run never throws.
/// </summary>
public sealed class SwitchOperation(GitClient git) : IGitOperation<SwitchRequest, SwitchCheck, SwitchOutcome>
{
    public SwitchCheck Check(SwitchRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request.Branch, log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, request.Branch, log);
        }
    }

    public SwitchOutcome Run(SwitchRequest request, SwitchCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return new SwitchOutcome(SwitchKind.Refused, check.From, check.Target, 0, [], check.Refusal ?? "nothing was switched", log);

        try
        {
            return RunCore(check, log);
        }
        catch (Exception ex)
        {
            return new SwitchOutcome(SwitchKind.Failed, check.From, check.Target, 0, [], $"{ex.Message}; repo state unknown, run git status", log);
        }
    }

    private SwitchCheck CheckCore(string target, List<GitResult> log)
    {
        if (IsUnsafeName(target))
            return Refused(BranchRefusals.InvalidName, target, log);

        // 1. Where HEAD is, and the branch that was asked for.
        var head = SymbolicHead(git, log);
        var from = head.Length == 0 ? "" : GitClient.StripHeads(head);
        var tip = RefOrEmpty(git, log, HeadsPrefix + target);
        if (tip.Length == 0)
            return Refused(BranchRefusals.NoBranch(target), target, log);
        if (target == from)
            return Refused(BranchRefusals.AlreadyOn(target), target, log);

        // 2. The git dir, and no other operation that owns the index.
        var blocked = Blocked(git, log);
        if (blocked is not null)
            return Refused(blocked, target, log);

        // 3. The status: a conflicted index is never switched.
        var st = Call(git, log, StatusArgs);
        if (!st.Success)
            return Refused(GitClient.Describe(st), target, log);
        var status = StatusParser.Parse(st.StdOut);
        if (status.HasConflicts)
            return Refused(ChangeRefusals.Conflicts, target, log);

        // 4. Detached HEAD: commits that no branch holds would be left only in the reflog after the switch.
        var headHash = RefOrEmpty(git, log, "HEAD");
        if (from.Length == 0 && headHash.Length > 0)
        {
            var orphans = Count(git, log, "HEAD", "--not", "--branches");
            if (orphans > 0)
                return Refused(BranchRefusals.DetachedOrphans(orphans), target, log);
        }

        return new SwitchCheck(null, from, target, tip, headHash, status.ChangedPaths, log);
    }

    private SwitchOutcome RunCore(SwitchCheck check, List<GitResult> log)
    {
        var target = check.Target;
        var fromRef = check.From.Length == 0 ? "" : HeadsPrefix + check.From;

        // 1. Nothing moved since the check.
        if (SymbolicHead(git, log) != fromRef || RefOrEmpty(git, log, "HEAD") != check.Head)
            return Done(SwitchKind.Failed, check, "HEAD moved since the check; nothing changed", [], log);
        if (RefOrEmpty(git, log, HeadsPrefix + target) != check.TargetTip)
            return Done(SwitchKind.Failed, check, BranchRefusals.TipMoved(target), [], log);

        // 2. The switch. --no-guess: a name that is not a branch is an error, never a new branch from a remote.
        var sw = Call(git, log, "switch", "--no-guess", target);
        if (sw.Success)
        {
            if (SymbolicHead(git, log) != HeadsPrefix + target || RefOrEmpty(git, log, "HEAD") != check.TargetTip)
                return Done(SwitchKind.Failed, check, "git switch finished, but HEAD is not where it should be; check git status", [], log);

            var after = StatusParser.Parse(Call(git, log, StatusArgs).StdOut);
            return Done(SwitchKind.Switched, check, $"switched to {target}", [], log, after.ChangedPaths);
        }

        // 3. git refused or failed. Nothing may have moved.
        if (SymbolicHead(git, log) != fromRef || RefOrEmpty(git, log, "HEAD") != check.Head)
            return Done(SwitchKind.Failed, check, "git switch failed part-way; check git status", [], log);

        // Without the status the paths cannot be named, so this is a failure, not a classified refusal.
        var st = Call(git, log, StatusArgs);
        if (!st.Success)
            return Done(SwitchKind.Failed, check, $"git switch was refused and the status could not be read: {GitClient.Describe(st)}", [], log);
        var changed = Paths(StatusParser.Parse(st.StdOut)).Distinct().ToHashSet(StringComparer.Ordinal);

        var diff = Call(git, log, "diff", "--name-only", "-z", "--no-renames", "HEAD", HeadsPrefix + target);
        var differs = diff.Success
            ? diff.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var blocking = changed.Where(differs.Contains).Order(StringComparer.Ordinal).ToList();
        if (blocking.Count > 0)
            return Done(SwitchKind.Refused, check, BranchRefusals.WouldOverwrite(blocking), blocking, log);
        return Done(SwitchKind.Refused, check, FirstLine(sw), [], log);
    }

    private static SwitchCheck Refused(string reason, string target, List<GitResult> log) =>
        new(reason, "", target, "", "", 0, log);

    private static SwitchOutcome Done(SwitchKind kind, SwitchCheck check, string message, IReadOnlyList<string> blocking,
        List<GitResult> log, int cameAlong = 0) =>
        new(kind, check.From, check.Target, cameAlong, blocking, message, log);
}
