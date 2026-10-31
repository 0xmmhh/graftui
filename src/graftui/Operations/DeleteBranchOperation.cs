using graftui.Git;
using graftui.Model;
using static graftui.Operations.BranchGit;

namespace graftui.Operations;

/// <summary>
/// Deletes a branch (D). A merged branch is deleted with `git branch -d`. An unmerged one only with Force, which runs
/// `git branch -D`; the UI asks twice before it sets Force. The current branch is refused.
/// Check records the tip, whether the branch is merged into HEAD, how many commits HEAD lacks (Ahead), and how many commits
/// no other local branch holds (Lost). Run re-checks the tip and HEAD, deletes, verifies the ref is gone, and saves a
/// DeleteBranch undo step whose parent is the tip, so the commits stay reachable. Run never throws.
/// </summary>
public sealed class DeleteBranchOperation(GitClient git) : IGitOperation<DeleteBranchRequest, DeleteBranchCheck, BranchChangeOutcome>
{
    public DeleteBranchCheck Check(DeleteBranchRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request, log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, request.Name, log);
        }
    }

    public BranchChangeOutcome Run(DeleteBranchRequest request, DeleteBranchCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Failed(check.Name, "", check.Refusal ?? "nothing was deleted", log);

        try
        {
            return RunCore(request, check, log);
        }
        catch (Exception ex)
        {
            return Failed(check.Name, check.Tip, $"{ex.Message}; repo state unknown, run git status", log);
        }
    }

    private DeleteBranchCheck CheckCore(DeleteBranchRequest request, List<GitResult> log)
    {
        var name = request.Name;
        if (IsUnsafeName(name))
            return Refused(BranchRefusals.InvalidName, name, log);

        // 1. The branch exists, and it is the tip the user was asked about.
        var tip = RefOrEmpty(git, log, HeadsPrefix + name);
        if (tip.Length == 0)
            return Refused(BranchRefusals.NoBranch(name), name, log);
        if (request.ExpectedTip is { } expected && expected != tip)
            return Refused(BranchRefusals.TipMoved(name), name, log);

        // 2. Not the current branch. A detached HEAD is not a branch, so it cannot be the one.
        var head = SymbolicHead(git, log);
        if (head == HeadsPrefix + name)
            return Refused(BranchRefusals.Current(name), name, log);
        var current = head.Length == 0 ? "" : GitClient.StripHeads(head);

        // 3. The git dir, and no other operation that owns the index.
        var blocked = Blocked(git, log);
        if (blocked is not null)
            return Refused(blocked, name, log);

        // 4. Merged into HEAD, and what it would leave out. With no commit on HEAD nothing is merged.
        var headHash = RefOrEmpty(git, log, "HEAD");
        var merged = headHash.Length > 0 && IsAncestor(git, log, tip, "HEAD");
        var ahead = headHash.Length > 0 ? Count(git, log, "HEAD.." + tip) : Count(git, log, tip);

        // 5. Commits that no other local branch holds. --exclude takes the short name.
        var lost = Count(git, log, tip, "--not", "--exclude=" + name, "--branches");

        return new DeleteBranchCheck(null, name, current, tip, merged, ahead, lost, log);
    }

    private BranchChangeOutcome RunCore(DeleteBranchRequest request, DeleteBranchCheck check, List<GitResult> log)
    {
        var name = check.Name;
        var reference = HeadsPrefix + name;

        // 1. The tip is still the one checked (and the one the user was asked about), and the branch is not HEAD.
        if (RefOrEmpty(git, log, reference) != check.Tip || (request.ExpectedTip is { } expected && expected != check.Tip))
            return Failed(name, check.Tip, BranchRefusals.TipMoved(name), log);
        if (SymbolicHead(git, log) == reference)
            return Failed(name, check.Tip, BranchRefusals.Current(name), log);

        // 2. Merged now? An unmerged branch needs Force, which the user gave by answering both questions.
        var headHash = RefOrEmpty(git, log, "HEAD");
        var merged = headHash.Length > 0 && IsAncestor(git, log, check.Tip, "HEAD");
        if (!merged && !request.Force)
            return Failed(name, check.Tip, BranchRefusals.NotMergedNow(name), log);

        // 3. Delete. -d only when merged; -D loses the commits no other branch holds, so only with Force.
        var del = Call(git, log, "branch", merged ? "-d" : "-D", name);
        if (!del.Success)
            return Failed(name, check.Tip, $"git refused to delete {name} ({FirstLine(del)}); nothing was deleted", log);

        // 4. Verify the ref is gone.
        if (RefOrEmpty(git, log, reference).Length > 0)
            return Failed(name, check.Tip, $"{name} is still there after git branch; check git branch", log);

        // 5. The undo step, saved only now. The deletion stands even when its step cannot be saved.
        var undo = new UndoRecord(UndoKind.DeleteBranch, $"deletion of {name}", [new RefChange(reference, check.Tip, "")],
            Branch: name, Head: check.Tip);
        var message = $"deleted {name} (was {Short(check.Tip)})";
        var problemSaving = new UndoLog(git).Push(undo, log);
        if (problemSaving is not null)
            return new BranchChangeOutcome(BranchChangeKind.Done, name, null, check.Tip,
                $"deleted {name}, but could not save the undo step ({problemSaving}): restore it with git branch {name} {check.Tip}",
                log, Lost: check.Lost);
        return new BranchChangeOutcome(BranchChangeKind.Done, name, null, check.Tip, message, log, undo, Lost: check.Lost);
    }

    private static DeleteBranchCheck Refused(string reason, string name, List<GitResult> log) =>
        new(reason, name, "", "", false, 0, 0, log);

    private static BranchChangeOutcome Failed(string name, string tip, string message, List<GitResult> log) =>
        new(BranchChangeKind.Failed, name, null, tip, message, log);
}
