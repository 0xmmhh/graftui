using graftui.Git;
using graftui.Model;
using static graftui.Operations.BranchGit;

namespace graftui.Operations;

/// <summary>
/// Renames any local branch (R), the current one included: `git branch -m`, which moves HEAD with it and carries the
/// branch's config (an upstream follows the new name). Check validates the new name, refuses the same name, and checks the
/// old one exists. Run re-checks the tip, runs the rename, verifies the refs and saves a RenameBranch undo step. Run never throws.
/// </summary>
public sealed class RenameBranchOperation(GitClient git) : IGitOperation<RenameBranchRequest, RenameBranchCheck, BranchChangeOutcome>
{
    public RenameBranchCheck Check(RenameBranchRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request, log);
        }
        catch (Exception ex)
        {
            return new RenameBranchCheck(ex.Message, false, request.Old, request.New, "", false, log);
        }
    }

    public BranchChangeOutcome Run(RenameBranchRequest request, RenameBranchCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Failed(check.New, check.Old, "", check.Refusal ?? "nothing was renamed", log);

        try
        {
            return RunCore(check, log);
        }
        catch (Exception ex)
        {
            return Failed(check.New, check.Old, check.Tip, $"{ex.Message}; repo state unknown, run git status", log);
        }
    }

    private RenameBranchCheck CheckCore(RenameBranchRequest request, List<GitResult> log)
    {
        var old = request.Old;
        var nu = request.New;

        // 1. The branch being renamed exists.
        if (IsUnsafeName(old))
            return Refused(BranchRefusals.InvalidName, false, old, nu, log);
        var tip = RefOrEmpty(git, log, HeadsPrefix + old);
        if (tip.Length == 0)
            return Refused(BranchRefusals.NoBranch(old), false, old, nu, log);

        // 2. The new name: different from the old, valid, and not taken.
        if (nu == old)
            return Refused(BranchRefusals.SameName, true, old, nu, log);
        var problem = BranchNames.Problem(git, nu, null, log);
        if (problem is not null)
            return Refused(problem, true, old, nu, log);

        // 3. The git dir, and no other operation that owns the index.
        var blocked = Blocked(git, log);
        if (blocked is not null)
            return Refused(blocked, false, old, nu, log);

        var head = SymbolicHead(git, log);
        return new RenameBranchCheck(null, false, old, nu, tip, head == HeadsPrefix + old, log);
    }

    private BranchChangeOutcome RunCore(RenameBranchCheck check, List<GitResult> log)
    {
        var old = check.Old;
        var nu = check.New;

        // 1. The tip is the one the user was shown, and the new name is still free.
        if (RefOrEmpty(git, log, HeadsPrefix + old) != check.Tip)
            return Failed(nu, old, check.Tip, BranchRefusals.TipMoved(old), log);
        var problem = BranchNames.Problem(git, nu, null, log);
        if (problem is not null)
            return Failed(nu, old, check.Tip, problem, log);

        // 2. Rename. A failure is classified from the refs, not from git's text.
        var mv = Call(git, log, "branch", "-m", old, nu);
        if (!mv.Success)
        {
            var stillThere = RefOrEmpty(git, log, HeadsPrefix + old) == check.Tip;
            return Failed(nu, old, check.Tip,
                stillThere ? $"{FirstLine(mv)}; nothing was renamed" : $"{FirstLine(mv)}; check git branch", log);
        }

        // 3. Verify: the new name has the same tip, the old name is gone, and HEAD follows when it was on the old name.
        var headOk = !check.IsCurrent || SymbolicHead(git, log) == HeadsPrefix + nu;
        if (RefOrEmpty(git, log, HeadsPrefix + nu) != check.Tip
            || RefOrEmpty(git, log, HeadsPrefix + old).Length > 0
            || !headOk)
            return Failed(nu, old, check.Tip, $"renamed {old} to {nu}, but it could not be verified: check git status", log);

        // 4. The undo step, saved only now. The rename stands even when its step cannot be saved.
        var undo = new UndoRecord(UndoKind.RenameBranch, $"rename of {old}", [], Branch: nu, Head: check.Tip, OtherBranch: old);
        var message = $"renamed {old} to {nu}";
        var problemSaving = new UndoLog(git).Push(undo, log);
        if (problemSaving is not null)
            return new BranchChangeOutcome(BranchChangeKind.Done, nu, old, check.Tip,
                $"{message}; could not save the undo step: {problemSaving}", log);
        return new BranchChangeOutcome(BranchChangeKind.Done, nu, old, check.Tip, message, log, undo);
    }

    private static RenameBranchCheck Refused(string reason, bool nameProblem, string old, string nu, List<GitResult> log) =>
        new(reason, nameProblem, old, nu, "", false, log);

    private static BranchChangeOutcome Failed(string name, string oldName, string tip, string message, List<GitResult> log) =>
        new(BranchChangeKind.Failed, name, oldName, tip, message, log);
}
