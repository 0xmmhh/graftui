using graftui.Git;
using graftui.Model;
using static graftui.Operations.BranchGit;

namespace graftui.Operations;

/// <summary>
/// Creates a branch at HEAD and switches to it (n): `git switch -c`, with no start point, so the working tree never changes.
/// Check validates the name (the name box uses it too), and refuses a detached HEAD and a repo with no commit.
/// Run re-checks the name and HEAD, runs the switch, verifies the refs and saves an undo step (u removes the branch again).
/// Run never throws.
/// </summary>
public sealed class CreateBranchOperation(GitClient git) : IGitOperation<CreateBranchRequest, CreateBranchCheck, BranchChangeOutcome>
{
    public CreateBranchCheck Check(CreateBranchRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request.Name, log);
        }
        catch (Exception ex)
        {
            return new CreateBranchCheck(ex.Message, false, request.Name, "", "", log);
        }
    }

    public BranchChangeOutcome Run(CreateBranchRequest request, CreateBranchCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Failed(check.Name, "", check.Refusal ?? "nothing was created", log);

        try
        {
            return RunCore(check, log);
        }
        catch (Exception ex)
        {
            return Failed(check.Name, check.Tip, $"{ex.Message}; repo state unknown, run git status", log);
        }
    }

    private CreateBranchCheck CheckCore(string name, List<GitResult> log)
    {
        // 1. The name: valid, and not taken (or taken by a case-only twin on a case-insensitive disk).
        var problem = BranchNames.Problem(git, name, null, log);
        if (problem is not null)
            return new CreateBranchCheck(problem, true, name, "", "", log);

        // 2. HEAD must be on a branch whose tip the new branch starts from, and must have a commit.
        var head = SymbolicHead(git, log);
        if (head.Length == 0)
            return new CreateBranchCheck(BranchRefusals.Detached, false, name, "", "", log);
        var tip = RefOrEmpty(git, log, "HEAD");
        if (tip.Length == 0)
            return new CreateBranchCheck(BranchRefusals.NoCommitYet, false, name, "", "", log);

        // 3. The git dir, and no other operation that owns the index.
        var blocked = Blocked(git, log);
        if (blocked is not null)
            return new CreateBranchCheck(blocked, false, name, "", "", log);

        return new CreateBranchCheck(null, false, name, GitClient.StripHeads(head), tip, log);
    }

    private BranchChangeOutcome RunCore(CreateBranchCheck check, List<GitResult> log)
    {
        var name = check.Name;
        var from = check.From;

        // 1. The name is still free, and HEAD is still the tip the user was shown.
        var problem = BranchNames.Problem(git, name, null, log);
        if (problem is not null)
            return Failed(name, check.Tip, problem, log);
        if (SymbolicHead(git, log) != HeadsPrefix + from || RefOrEmpty(git, log, "HEAD") != check.Tip)
            return Failed(name, check.Tip, "HEAD moved since the check; nothing was created", log);

        // 2. Create and switch. git refuses before it writes the ref, so a failure leaves no branch behind.
        var sw = Call(git, log, "switch", "-c", name);
        if (!sw.Success)
        {
            var exists = RefOrEmpty(git, log, HeadsPrefix + name).Length > 0;
            return Failed(name, check.Tip,
                exists ? $"git reported an error, but {name} exists: check git branch" : FirstLine(sw), log);
        }

        // 3. Verify: the new branch is checked out at the same commit as the one it started from.
        if (SymbolicHead(git, log) != HeadsPrefix + name
            || RefOrEmpty(git, log, HeadsPrefix + name) != check.Tip
            || RefOrEmpty(git, log, HeadsPrefix + from) != check.Tip)
            return Failed(name, check.Tip, $"{name} was created, but it could not be verified: check git status", log);

        // 4. The undo step, saved only now. The branch stands even when its step cannot be saved.
        var undo = new UndoRecord(UndoKind.CreateBranch, $"creation of {name}", [new RefChange(HeadsPrefix + name, "", check.Tip)],
            Branch: name, Head: check.Tip, OtherBranch: from);
        var message = $"created {name} at {Short(check.Tip)} and switched to it";
        var problemSaving = new UndoLog(git).Push(undo, log);
        if (problemSaving is not null)
            return new BranchChangeOutcome(BranchChangeKind.Done, name, null, check.Tip,
                $"{message}; could not save the undo step: {problemSaving}", log);
        return new BranchChangeOutcome(BranchChangeKind.Done, name, null, check.Tip, message, log, undo);
    }

    private static BranchChangeOutcome Failed(string name, string tip, string message, List<GitResult> log) =>
        new(BranchChangeKind.Failed, name, null, tip, message, log);
}
