using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The finish of each pause, against stops the engine made in throwaway repos: an edit and a reword stop (plans that ask for them), a
/// conflict that is skipped, continued or aborted through the resume operation, and a refused continue. The finish functions are the ones
/// the Branches tab runs on its worker.
/// </summary>
public class RebasePauseFinishTests
{
    private static readonly Style WarnB = new(Ink.Warn, true);

    /// <summary>The default plan of the repo's rebase onto main, with the given rows' actions changed.</summary>
    private static List<RebaseRow> PlanOf(TempRepo repo, params (int Index, RebaseAction Action)[] steps)
    {
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var plan = check.Rows.ToList();
        foreach (var (index, action) in steps)
            plan[index] = plan[index] with { Action = action };
        return plan;
    }

    /// <summary>Starts the rebase with the plan, which must pause, and returns its check and outcome.</summary>
    private static (RebaseCheck Check, SequenceOutcome Outcome) Pause(TempRepo repo, IReadOnlyList<RebaseRow>? plan = null)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main", plan);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        return (check, outcome);
    }

    private static BranchesTab.RebaseEngine Engine(TempRepo repo)
    {
        var reader = new RepoReader(repo.Git);
        return new BranchesTab.RebaseEngine(repo.Git, () => reader.LoadSnapshot(withCounts: true, CancellationToken.None),
            (snap, b) => reader.LoadLayout(snap, b, CancellationToken.None));
    }

    private static RepoSnapshot Snap(TempRepo repo) => new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

    [Fact]
    public void An_edit_stop_is_a_warning_pause_that_exits_0_with_the_edit_caption_and_no_log_line()
    {
        using var repo = SequenceRepos.Feature4();
        var (check, outcome) = Pause(repo, PlanOf(repo, (2, RebaseAction.Edit)));

        var f = BranchesTab.RebaseFinish(outcome, Snap(repo), check, Layouts.Diverged(), Engine(repo));

        Assert.Equal(RebaseText.PausedTitle, f.Result.Title);
        Assert.Equal(RebaseText.EditStop(outcome.Stop!), f.Result.Caption.Text);
        Assert.Empty(f.Log);
        Assert.Equal(0, f.ExitCode);
        Assert.Null(f.ExitLine);
    }

    [Fact]
    public void A_reword_stop_is_a_warning_pause_with_the_message_caption()
    {
        using var repo = SequenceRepos.Feature4();
        var (check, outcome) = Pause(repo, PlanOf(repo, (0, RebaseAction.Reword)));

        var f = BranchesTab.RebaseFinish(outcome, Snap(repo), check, Layouts.Diverged(), Engine(repo));

        Assert.Equal(RebaseText.MessagePause(outcome.Stop!), f.Result.Caption.Text);
        Assert.Equal(WarnB, f.Result.Caption.Style);
        Assert.Equal(0, f.ExitCode);
    }

    [Fact]
    public void Continuing_an_edit_stop_finishes_the_rebase_and_selects_the_onto_branch()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = PlanOf(repo, (2, RebaseAction.Edit));
        var (_, outcome) = Pause(repo, rows);
        var op = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Continue, null, outcome.Stop!.Commit);
        var resumed = op.Run(request, op.Check(request));

        var f = BranchesTab.SequenceFinish(resumed, Snap(repo), rows, Layouts.Diverged(), Engine(repo));

        Assert.Equal(SequenceOutcomeKind.Done, resumed.Kind);
        Assert.True(f.Result.Success);
        Assert.Equal(RebaseText.DoneTitle, f.Result.Title);
        Assert.Equal("main", f.Select);
        Assert.False(SequenceGit.RebaseInProgress(Path.Combine(repo.Dir, ".git")));
    }

    [Fact]
    public void Continuing_a_reword_stop_keeps_its_message_and_finishes()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = PlanOf(repo, (0, RebaseAction.Reword));
        var (_, outcome) = Pause(repo, rows);
        var op = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Message, outcome.Stop!.Message, outcome.Stop.Commit);

        var resumed = op.Run(request, op.Check(request));
        var f = BranchesTab.SequenceFinish(resumed, Snap(repo), rows, Layouts.Diverged(), Engine(repo));

        Assert.Equal(SequenceOutcomeKind.Done, resumed.Kind);
        Assert.Equal(RebaseText.DoneTitle, f.Result.Title);
        Assert.False(SequenceGit.RebaseInProgress(Path.Combine(repo.Dir, ".git")));
    }

    [Fact]
    public void Skipping_a_conflict_finishes_and_the_caption_says_what_was_skipped()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var rows = PlanOf(repo);
        var (_, outcome) = Pause(repo, rows);
        var request = new ResumeRequest(ResumeAction.Skip, null, outcome.Stop!.Commit);
        var resumed = new ResumeSequenceOperation(repo.Git).Run(request, new ResumeSequenceOperation(repo.Git).Check(request));

        var f = BranchesTab.SequenceFinish(resumed, Snap(repo), rows, Layouts.Diverged(), Engine(repo));

        Assert.Equal(SequenceOutcomeKind.Done, resumed.Kind);
        Assert.Contains("1 commit skipped", f.Result.Caption.Text, StringComparison.Ordinal);
        Assert.Equal(0, f.ExitCode);
    }

    [Fact]
    public void Aborting_through_the_resume_operation_gives_the_aborted_finish_with_the_branch_unchanged()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        var rows = PlanOf(repo);
        var (_, outcome) = Pause(repo, rows);
        var request = new ResumeRequest(ResumeAction.Abort, null, outcome.Stop!.Commit);
        var aborted = new ResumeSequenceOperation(repo.Git).Run(request, new ResumeSequenceOperation(repo.Git).Check(request));

        var f = BranchesTab.SequenceFinish(aborted, Snap(repo), rows, Layouts.Diverged(), Engine(repo));

        Assert.Equal(before, repo.Head);
        Assert.Equal(RebaseText.AbortedTitle, f.Result.Title);
        Assert.Equal("✕ Rebase aborted: feature unchanged", f.Result.Caption.Text);
        Assert.Equal(RebaseText.ExitAborted("feature", "main"), f.ExitLine);
        Assert.Equal(0, f.ExitCode);
        Assert.Null(f.Select);
    }

    [Fact]
    public void A_continue_refused_while_the_file_still_has_markers_is_a_refusal_with_the_engine_text()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var rows = PlanOf(repo);
        var (_, outcome) = Pause(repo, rows);
        var request = new ResumeRequest(ResumeAction.Continue, null, outcome.Stop!.Commit);
        var refused = new ResumeSequenceOperation(repo.Git).Run(request, new ResumeSequenceOperation(repo.Git).Check(request));

        var f = BranchesTab.SequenceFinish(refused, Snap(repo), rows, Layouts.Diverged(), Engine(repo));

        Assert.Equal(SequenceOutcomeKind.Refused, refused.Kind);
        Assert.Equal("Refused", f.Result.Title);
        Assert.Equal("graftui: refused: " + refused.Message, f.ExitLine);
        Assert.True(SequenceGit.RebaseInProgress(Path.Combine(repo.Dir, ".git")));
    }
}
