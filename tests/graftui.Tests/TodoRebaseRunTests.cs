using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The interactive rebase run in throwaway repos: the plan the todo list makes is what git replays. Feature4 is main + four feature commits
/// (rows 0..3 = Add login form, Fix typo in form, Add password check, WIP debug logging). The commits of feature after its rebase are read
/// oldest first with log --reverse.
/// </summary>
public class TodoRebaseRunTests
{
    private static RebaseCheck CheckOf(TempRepo repo, IReadOnlyList<RebaseRow>? plan = null) =>
        new RebaseOperation(repo.Git).Check(new RebaseRequest("main", plan));

    /// <summary>The default rows with each row's action set, in order (the plan the todo list would send).</summary>
    private static IReadOnlyList<RebaseRow> Plan(RebaseCheck check, params RebaseAction[] actions) =>
        check.Rows.Select((r, i) => r with { Action = actions[i] }).ToList();

    private static SequenceOutcome Run(TempRepo repo, IReadOnlyList<RebaseRow> plan)
    {
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest("main", plan));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new RebaseRequest("main", plan), check);
    }

    private static string Subjects(TempRepo repo) => repo.Run("log", "--reverse", "--format=%s", "main..feature").Replace('\n', '|');

    [Fact]
    public void A_reordered_plan_with_a_drop_replays_the_kept_commits_in_the_plans_order()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);
        var plan = RebasePlan.Move(check.Rows, 1, 0).ToList();
        plan = RebasePlan.WithAction(plan, 3, RebaseAction.Drop).ToList();

        var o = Run(repo, plan);

        Assert.Equal(SequenceOutcomeKind.Done, o.Kind);
        Assert.Equal("Fix typo in form|Add login form|Add password check", Subjects(repo));
        Assert.Equal("✔ Rebased feature: 3 kept, 0 squashed, 1 dropped, backup saved", RebaseText.Interactive(o, "feature", plan));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_fixup_folds_its_commit_into_the_one_above_and_leaves_the_rest()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);
        var plan = Plan(check, RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Pick, RebaseAction.Drop);

        var o = Run(repo, plan);

        Assert.Equal(SequenceOutcomeKind.Done, o.Kind);
        Assert.Equal("Add login form|Add password check", Subjects(repo));
        Assert.Equal("✔ Rebased feature: 2 kept, 1 squashed, 1 dropped, backup saved", RebaseText.Interactive(o, "feature", plan));
    }

    [Fact]
    public void An_edit_stop_is_paused_and_amend_and_continue_puts_the_staged_file_into_that_commit()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);
        var paused = Run(repo, Plan(check, RebaseAction.Edit, RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick));
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        Assert.Equal(PauseKind.Edit, paused.Stop!.Pause);
        Assert.Equal(check.Rows[0].Hash, paused.Stop!.Commit);

        repo.Write("extra.txt", "extra\n");
        repo.Run("add", "extra.txt");
        var resume = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.AmendAndContinue, null, paused.Stop.Commit);
        var done = resume.Run(request, resume.Check(request));

        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        Assert.Equal("Add login form|Fix typo in form|Add password check|WIP debug logging", Subjects(repo));
        Assert.Contains("extra.txt", repo.Run("show", "--name-only", "--format=", "feature~3"));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_reword_stop_keeps_the_message_when_it_is_sent_unchanged()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);
        var paused = Run(repo, Plan(check, RebaseAction.Reword, RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Drop));
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        Assert.Equal(PauseKind.Reword, paused.Stop!.Pause);
        Assert.Equal("Add login form", paused.Stop.Message);

        var resume = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Message, paused.Stop.Message, paused.Stop.Commit);
        var done = resume.Run(request, resume.Check(request));

        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        Assert.Equal("Add login form|Fix typo in form|Add password check", Subjects(repo));
        Assert.Equal("Add login form", repo.Run("log", "-1", "--format=%B", "feature~2").Trim());
        Assert.Equal(new[] { check.Rows[0].Hash }, done.Reworded);
    }

    [Fact]
    public void A_plan_that_starts_with_a_fixup_is_refused_by_the_engine_with_its_words()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);

        var refused = CheckOf(repo, Plan(check, RebaseAction.Fixup, RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick));

        Assert.False(refused.CanRun);
        Assert.Equal("fixup needs a commit above it to fold into", refused.Refusal);
        Assert.Equal("Add login form|Fix typo in form|Add password check|WIP debug logging", repo.Run("log", "--reverse", "--format=%s", "main..feature").Replace('\n', '|'));
    }

    [Fact]
    public void A_plan_that_starts_with_a_squash_is_refused_by_the_engine_with_its_words()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);

        var refused = CheckOf(repo, Plan(check, RebaseAction.Drop, RebaseAction.Squash, RebaseAction.Pick, RebaseAction.Pick));

        Assert.False(refused.CanRun);
        Assert.Equal("squash needs a commit above it to fold into", refused.Refusal);
    }

    [Fact]
    public void A_plan_that_drops_every_commit_is_refused_by_the_engine_and_changes_nothing()
    {
        using var repo = SequenceRepos.Feature4();
        var head = repo.Head;
        var check = CheckOf(repo);

        var refused = CheckOf(repo, Plan(check, RebaseAction.Drop, RebaseAction.Drop, RebaseAction.Drop, RebaseAction.Drop));

        Assert.False(refused.CanRun);
        Assert.Equal("dropping every commit would leave feature empty: keep at least one", refused.Refusal);
        Assert.Equal(head, repo.Head);
    }

    [Theory]
    [InlineData("dddd")]
    [InlineData("ddfp")]
    [InlineData("sppp")]
    [InlineData("fppp")]
    [InlineData("dspp")]
    [InlineData("dfpd")]
    [InlineData("pddd")]
    [InlineData("ppsd")]
    [InlineData("pppp")]
    public void The_start_refusal_agrees_with_the_engines_plan_check(string codes)
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOf(repo);
        var plan = Plan(check, codes.Select(c => RebasePlan.ActionForKey(c)!.Value).ToArray());

        var engineRefuses = !CheckOf(repo, plan).CanRun;

        Assert.Equal(engineRefuses, new TodoState(plan).StartRefusal() is not null);
    }
}
