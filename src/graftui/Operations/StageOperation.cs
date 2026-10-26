using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Stages and unstages paths for the Changes tab: Stage, Unstage, StageAll and UnstageAll.
/// Check reads the status and refuses anything that no longer matches the rows the user saw.
/// Run never resets anything: it only runs add, restore or rm, and never touches the work tree.
/// Rollback is none: git writes the index atomically, so a failed add, restore or rm leaves the index unchanged.
/// Verify reads the status again and checks that the moved paths are on the new side.
/// </summary>
public sealed class StageOperation(GitClient git) : IGitOperation<StageRequest, StageCheck, StageOutcome>
{
    private static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    public StageCheck Check(StageRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            var head = Call(log, "rev-parse", "-q", "--verify", "HEAD");
            // Exit 1 is quiet and means an unborn branch; anything else is a real failure.
            if (!head.Success && head.ExitCode != 1)
                return new StageCheck(GitClient.Describe(head), false, [], log);

            var status = Call(log, StatusArgs);
            if (!status.Success)
                return new StageCheck(GitClient.Describe(status), head.Success, [], log);

            var (refusal, moving) = Judge(request, StatusParser.Parse(status.StdOut));
            return new StageCheck(refusal, head.Success, moving, log);
        }
        catch (Exception ex)
        {
            return new StageCheck(ex.Message, false, [], log);
        }
    }

    public StageOutcome Run(StageRequest request, StageCheck check)
    {
        var log = new List<GitResult>();
        var moving = check.Moving;
        try
        {
            var act = Act(log, request.Action, check);
            if (!act.Success)
                return Done(false, request.Action, moving, null, FirstLine(act), log);

            var status = Call(log, StatusArgs);
            if (!status.Success)
                return Done(false, request.Action, moving, null,
                    "could not read the status after the change: " + FirstLine(status), log);

            var after = StatusParser.Parse(status.StdOut);
            var problem = Verify(request.Action, moving, after);
            return problem is null
                ? Done(true, request.Action, moving, after, "", log)
                : Done(false, request.Action, moving, after, problem, log);
        }
        catch (Exception ex)
        {
            return Done(false, request.Action, moving, null, ex.Message, log);
        }
    }

    /// <summary>Picks the rows an action moves, or the refusal when the list has gone stale.</summary>
    private static (string? Refusal, IReadOnlyList<FileChange> Moving) Judge(StageRequest r, WorkingStatus s)
    {
        switch (r.Action)
        {
            case StageAction.Stage:
            {
                if (r.File is null)
                    return (ChangeRefusals.NothingToStage, []);
                var row = s.Unstaged.FirstOrDefault(f => f.Path == r.File.Path);
                if (row is null)
                    return (ChangeRefusals.Stale(r.File.Path), []);
                if (row.Kind == ChangeKind.Conflicted)
                    return (ChangeRefusals.ConflictFile(row.Path), []);
                if (row.IsNestedRepo)
                    return (ChangeRefusals.NestedRepo(row.Path), []);
                return (null, [row]);
            }
            case StageAction.Unstage:
            {
                if (r.File is null)
                    return (ChangeRefusals.NothingIsStaged, []);
                var row = s.Staged.FirstOrDefault(f => f.Path == r.File.Path);
                if (row is null)
                    return (ChangeRefusals.Stale(r.File.Path), []);
                return (null, [row]);
            }
            case StageAction.StageAll:
            {
                if (s.Unstaged.Count == 0)
                    return (ChangeRefusals.NothingToStage, []);
                if (s.HasConflicts)
                    return (ChangeRefusals.Conflicts, []);
                if (s.Unstaged.FirstOrDefault(f => f.IsNestedRepo) is { } nested)
                    return (ChangeRefusals.NestedRepo(nested.Path), []);
                return (null, s.Unstaged);
            }
            default: // UnstageAll
            {
                if (s.Staged.Count == 0)
                    return (ChangeRefusals.NothingIsStaged, []);
                if (s.HasConflicts)
                    return (ChangeRefusals.Conflicts, []);
                return (null, s.Staged);
            }
        }
    }

    private GitResult Act(List<GitResult> log, StageAction action, StageCheck c)
    {
        switch (action)
        {
            case StageAction.Stage:
                return CallLiteral(log, "add", "--", c.Moving[0].Path);
            case StageAction.StageAll:
                return Call(log, "add", "-A");
            case StageAction.Unstage:
            {
                var f = c.Moving[0];
                if (!c.HasHead)
                    return CallLiteral(log, "rm", "--cached", "-q", "-f", "--", f.Path);
                // A staged rename has two paths; both must be restored, or the old name stays staged as a deletion.
                return f.OrigPath is null
                    ? CallLiteral(log, "restore", "--staged", "--", f.Path)
                    : CallLiteral(log, "restore", "--staged", "--", f.Path, f.OrigPath);
            }
            default: // UnstageAll
                return c.HasHead
                    ? CallLiteral(log, "restore", "--staged", "--", ".")
                    : CallLiteral(log, "rm", "--cached", "-r", "-q", "-f", "--", ".");
        }
    }

    /// <summary>Null when the status shows the change took effect; otherwise why it did not.</summary>
    private static string? Verify(StageAction action, IReadOnlyList<FileChange> moving, WorkingStatus after)
    {
        switch (action)
        {
            case StageAction.Stage:
                return after.Unstaged.Any(f => f.Path == moving[0].Path)
                    ? $"{moving[0].Path} still has unstaged changes after git add (edited meanwhile?)"
                    : null;
            case StageAction.Unstage:
            {
                var paths = moving.SelectMany(f => new[] { f.Path, f.OrigPath }).OfType<string>().ToHashSet();
                return after.Staged.Any(f => paths.Contains(f.Path))
                    ? $"{moving[0].Path} is still staged after git restore --staged (edited meanwhile?)"
                    : null;
            }
            case StageAction.StageAll:
                return after.Unstaged.Count > 0
                    ? "still has unstaged changes after git add -A (edited meanwhile?)"
                    : null;
            default:
                return after.Staged.Count > 0
                    ? "still has staged changes after unstaging everything (edited meanwhile?)"
                    : null;
        }
    }

    private static StageOutcome Done(bool ok, StageAction action, IReadOnlyList<FileChange> moved, WorkingStatus? after,
        string message, List<GitResult> log) =>
        new(ok, action, moved, after, message, log);

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private GitResult CallLiteral(List<GitResult> log, params string[] args)
    {
        var r = git.RunLiteral(args);
        log.Add(r);
        return r;
    }

    private static string FirstLine(GitResult r) =>
        r.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        ?? $"git exited with {r.ExitCode}";
}
