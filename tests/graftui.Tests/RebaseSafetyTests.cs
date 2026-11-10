using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// RebaseOperation against throwaway repos: the refusals (word for word), the plan rules, the stops it leaves, the one undo step
/// and a todo that git rejects. Feature4 is main + four feature commits (rows 0..3 = Add login form, Fix typo in form,
/// Add password check, WIP debug logging).
/// </summary>
public class RebaseSafetyTests
{
    private static RebaseCheck CheckOnly(TempRepo repo, string onto, IReadOnlyList<RebaseRow>? plan = null) =>
        new RebaseOperation(repo.Git).Check(new RebaseRequest(onto, plan));

    /// <summary>Check, then Run when the check allows it. A refused check fails the test: use CheckOnly for refusals.</summary>
    private static SequenceOutcome Rebase(TempRepo repo, string onto, IReadOnlyList<RebaseRow>? plan = null)
    {
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest(onto, plan));
        if (!check.CanRun)
            throw new InvalidOperationException("the check refused: " + check.Refusal);
        return op.Run(new RebaseRequest(onto, plan), check);
    }

    /// <summary>The default plan's rows, with each listed row's action set. Steps are (index in the default plan, action), in plan order.</summary>
    private static List<RebaseRow> PlanOf(RebaseCheck check, params (int Index, RebaseAction Action)[] steps) =>
        steps.Select(s => check.Rows[s.Index] with { Action = s.Action }).ToList();

    private static bool StateFileExists(TempRepo repo) => File.Exists(Path.Combine(repo.Dir, ".git", SequenceStateFile.FileName));

    private static bool RebaseDirExists(TempRepo repo) => Directory.Exists(Path.Combine(repo.Dir, ".git", "rebase-merge"));

    [Fact]
    public void Check_lists_the_branch_commits_oldest_first_as_picks()
    {
        using var repo = SequenceRepos.Feature4();

        var check = CheckOnly(repo, "main");

        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal("feature", check.Branch);
        Assert.Equal(new[] { "Add login form", "Fix typo in form", "Add password check", "WIP debug logging" },
            check.Rows.Select(r => r.Subject));
        Assert.All(check.Rows, r => Assert.Equal(RebaseAction.Pick, r.Action));
        Assert.Equal(repo.Run("rev-parse", "main"), check.OntoTip);
    }

    [Fact]
    public void Check_refuses_a_dirty_tree_naming_the_file_and_changes_nothing()
    {
        using var repo = SequenceRepos.Feature4();
        repo.Write("app.txt", "changed\n");
        var head = repo.Head;

        var check = CheckOnly(repo, "main");

        Assert.False(check.CanRun);
        Assert.Equal("you have uncommitted changes to app.txt: commit or stash them before rebasing", check.Refusal);
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void Check_refuses_when_already_on_top()
    {
        using var repo = SequenceRepos.Feature4();
        repo.Run("branch", "base", repo.Run("rev-parse", "feature~4"));

        var check = CheckOnly(repo, "base");

        Assert.Equal("feature is already on top of base: nothing to rebase", check.Refusal);
    }

    [Fact]
    public void Check_refuses_detached_HEAD()
    {
        using var repo = SequenceRepos.Feature4();
        repo.Run("checkout", "-q", "--detach");

        var check = CheckOnly(repo, "main");

        Assert.Equal(ChangeRefusals.Detached, check.Refusal);
    }

    [Fact]
    public void Check_refuses_during_a_merge()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Git.Run("merge", "--no-edit", "feature");
        Assert.True(repo.Merging);

        var check = CheckOnly(repo, "feature");

        Assert.Equal("a merge is in progress: finish or abort it first", check.Refusal);
    }

    [Fact]
    public void Check_refuses_during_a_rebase_that_graftui_did_not_start()
    {
        using var repo = SequenceRepos.Feature4();
        var started = SequenceRepos.RebaseByHand(repo, "main", "edit " + repo.Run("rev-parse", "feature~3"));
        Assert.True(started.Success, started.StdErr);

        var check = CheckOnly(repo, "main");

        Assert.Equal("a rebase is in progress: finish or abort it first", check.Refusal);
    }

    [Fact]
    public void Check_refuses_a_branch_with_no_commits_of_its_own()
    {
        using var repo = SequenceRepos.Feature4();
        repo.Run("branch", "behind", repo.Run("rev-parse", "feature~4"));
        repo.Run("switch", "-q", "behind");

        var check = CheckOnly(repo, "main");

        Assert.Equal("behind has no commits of its own since main: nothing to rebase", check.Refusal);
    }

    [Fact]
    public void Check_refuses_a_range_with_a_merge_commit()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("f1.txt", "1\n", "Add login form");
        repo.Run("switch", "-q", "-c", "side", "main");
        repo.Commit("s1.txt", "s\n", "Add side");
        repo.Run("switch", "-q", "feature");
        repo.Run("merge", "--no-ff", "-q", "-m", "Merge side", "side");
        repo.Run("switch", "-q", "main");
        repo.Commit("m.txt", "m\n", "Add m on main");
        repo.Run("switch", "-q", "feature");

        var check = CheckOnly(repo, "main");

        Assert.Equal("feature has a merge commit since main: graftui won't rebase over merges", check.Refusal);
    }

    [Fact]
    public void Check_refuses_rebasing_onto_itself()
    {
        using var repo = SequenceRepos.Feature4();

        Assert.Equal("select another branch to rebase feature onto", CheckOnly(repo, "feature").Refusal);
    }

    [Fact]
    public void Check_refuses_an_unknown_onto_branch()
    {
        using var repo = SequenceRepos.Feature4();

        Assert.Equal("there is no branch named nope", CheckOnly(repo, "nope").Refusal);
    }

    [Fact]
    public void Check_refuses_a_plan_that_drops_every_commit()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var refusal = CheckOnly(repo, "main", PlanOf(check, (0, RebaseAction.Drop), (1, RebaseAction.Drop),
            (2, RebaseAction.Drop), (3, RebaseAction.Drop))).Refusal;

        Assert.Equal("dropping every commit would leave feature empty: keep at least one", refusal);
    }

    [Fact]
    public void Check_refuses_a_squash_on_the_first_kept_row()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var refusal = CheckOnly(repo, "main", PlanOf(check, (0, RebaseAction.Squash), (1, RebaseAction.Pick),
            (2, RebaseAction.Pick), (3, RebaseAction.Pick))).Refusal;

        Assert.Equal("squash needs a commit above it to fold into", refusal);
    }

    [Fact]
    public void Check_refuses_a_fixup_on_the_first_kept_row()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var refusal = CheckOnly(repo, "main", PlanOf(check, (0, RebaseAction.Fixup), (1, RebaseAction.Pick),
            (2, RebaseAction.Pick), (3, RebaseAction.Pick))).Refusal;

        Assert.Equal("fixup needs a commit above it to fold into", refusal);
    }

    [Fact]
    public void Check_refuses_a_squash_after_only_drops()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var refusal = CheckOnly(repo, "main", PlanOf(check, (0, RebaseAction.Drop), (1, RebaseAction.Squash),
            (2, RebaseAction.Pick), (3, RebaseAction.Pick))).Refusal;

        Assert.Equal("squash needs a commit above it to fold into", refusal);
    }

    [Fact]
    public void Check_refuses_a_plan_missing_a_commit_as_stale()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var refusal = CheckOnly(repo, "main", PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick),
            (2, RebaseAction.Pick))).Refusal;

        Assert.Equal("feature changed since the plan was made; nothing was rebased", refusal);
    }

    [Fact]
    public void Run_refuses_when_HEAD_moved_after_the_check_and_starts_nothing()
    {
        using var repo = SequenceRepos.Feature4();
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest("main"));
        repo.Commit("g.txt", "g\n", "Add g");

        var outcome = op.Run(new RebaseRequest("main"), check);

        Assert.Equal(SequenceOutcomeKind.Refused, outcome.Kind);
        Assert.Equal("feature moved since you started; nothing changed", outcome.Message);
        Assert.False(RebaseDirExists(repo));
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void Reorder_and_drop_rewrite_the_branch_in_plan_order()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var outcome = Rebase(repo, "main", PlanOf(check, (0, RebaseAction.Pick), (2, RebaseAction.Pick),
            (1, RebaseAction.Pick), (3, RebaseAction.Drop)));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("Add login form\nAdd password check\nFix typo in form", repo.Run("log", "--reverse", "--format=%s", "main..feature"));
        Assert.DoesNotContain("f4.txt", repo.Run("ls-tree", "-r", "--name-only", "feature").Split('\n'));
        Assert.False(StateFileExists(repo));
        Assert.False(RebaseDirExists(repo));
        Assert.Contains(outcome.Commands, c => c.Command.StartsWith("git rebase -i --no-autosquash --empty=drop ", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixup_folds_without_a_pause_and_keeps_the_target_message()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var outcome = Rebase(repo, "main", PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Fixup),
            (2, RebaseAction.Pick), (3, RebaseAction.Pick)));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("Add login form\nAdd password check\nWIP debug logging", repo.Run("log", "--reverse", "--format=%s", "main..feature"));
        Assert.Contains("f2.txt", repo.Run("ls-tree", "--name-only", "feature~2").Split('\n'));
    }

    [Fact]
    public void Squash_pauses_with_the_combined_message_before_it_is_written()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");
        var target = repo.Run("rev-parse", "feature~1");
        var folded = repo.Run("rev-parse", "feature");

        var outcome = Rebase(repo, "main", PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick),
            (2, RebaseAction.Pick), (3, RebaseAction.Squash)));

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(outcome.Stop);
        Assert.Equal(PauseKind.Squash, outcome.Stop.Pause);
        Assert.Equal(target, outcome.Stop.Commit);
        Assert.Equal("Add password check\n\nWIP debug logging", outcome.Stop.Message);
        Assert.Equal(new[] { folded }, outcome.Stop.Folded);
        Assert.True(StateFileExists(repo));
    }

    [Fact]
    public void Reword_pauses_with_the_old_message()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var outcome = Rebase(repo, "main", PlanOf(check, (0, RebaseAction.Reword), (1, RebaseAction.Pick),
            (2, RebaseAction.Pick), (3, RebaseAction.Pick)));

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(outcome.Stop);
        Assert.Equal(PauseKind.Reword, outcome.Stop.Pause);
        Assert.Equal("Add login form", outcome.Stop.Message);
        Assert.Equal(repo.Run("rev-parse", "feature~3"), outcome.Stop.Commit);
        Assert.Equal(1, outcome.Stop.Step);
        Assert.Equal(4, outcome.Stop.Total);
    }

    [Fact]
    public void Edit_pauses_at_its_row_and_reports_the_place()
    {
        using var repo = SequenceRepos.Feature4();
        var check = CheckOnly(repo, "main");

        var outcome = Rebase(repo, "main", PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick),
            (2, RebaseAction.Edit), (3, RebaseAction.Pick)));

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(outcome.Stop);
        Assert.Equal(PauseKind.Edit, outcome.Stop.Pause);
        Assert.Equal("Add password check", outcome.Stop.Subject);
        Assert.Equal(3, outcome.Stop.Step);
        Assert.Equal(4, outcome.Stop.Total);
    }

    [Fact]
    public void A_conflict_pauses_with_the_file_listed()
    {
        using var repo = SequenceRepos.FeatureConflict();

        var outcome = Rebase(repo, "main");

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(outcome.Stop);
        Assert.Equal(PauseKind.Conflict, outcome.Stop.Pause);
        Assert.Equal(new[] { ("app.txt", false, ConflictKind.Text) }, outcome.Stop.Files.Select(f => (f.Path, f.Resolved, f.Kind)));
        Assert.Equal("Add password check", outcome.Stop.Subject);
        Assert.Equal(3, outcome.Stop.Step);
        Assert.Equal(4, outcome.Stop.Total);
        Assert.True(outcome.Stop.Ours);
    }

    [Fact]
    public void Done_saves_one_merge_kind_undo_step_and_undo_restores_the_old_tip()
    {
        using var repo = SequenceRepos.Feature4();
        var before = repo.Head;

        var outcome = Rebase(repo, "main");

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.Merge, outcome.Undo.Kind);
        Assert.Equal("rebase of feature onto main", outcome.Undo.What);
        Assert.False(StateFileExists(repo));

        var undo = new UndoOperation(repo.Git);
        var undoCheck = undo.Check(new UndoRequest(null));
        Assert.True(undoCheck.CanRun, undoCheck.Refusal);
        var undone = undo.Run(new UndoRequest(undoCheck.StepRef), undoCheck);

        Assert.Equal(UndoOutcomeKind.Undone, undone.Kind);
        Assert.Equal(before, repo.Head);
        Assert.Equal("refs/heads/feature", repo.Run("symbolic-ref", "-q", "HEAD"));
    }

    [Fact]
    public void Undo_is_refused_when_the_branch_moved_after_the_rebase()
    {
        using var repo = SequenceRepos.Feature4();
        Rebase(repo, "main");
        repo.Commit("g.txt", "g\n", "Add g");

        var undoCheck = new UndoOperation(repo.Git).Check(new UndoRequest(null));

        Assert.False(undoCheck.CanRun);
        Assert.Equal(ChangeRefusals.UndoHeadMoved("feature"), ChangeRefusals.Short(undoCheck.Refusal!));
    }

    [Fact]
    public void A_todo_git_rejects_is_aborted_and_reported()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = CheckOnly(repo, "main").Rows;
        var before = repo.Head;
        var gitDir = Path.Combine(repo.Dir, ".git");

        var started = SequenceRepos.RebaseByHand(repo, "main", "frobnicate " + rows[0].Hash);
        Assert.False(started.Success);
        Assert.True(SequenceGit.RebaseInProgress(gitDir));

        var state = new SequenceState(1, SequenceKind.Rebase, "feature", "main", before, rows, [], [], [], [], [], []);
        var outcome = SequenceGit.Settle(repo.Git, gitDir, state, started, []);

        Assert.Equal(SequenceOutcomeKind.Failed, outcome.Kind);
        Assert.False(SequenceGit.RebaseInProgress(gitDir));
        Assert.False(StateFileExists(repo));
        Assert.Equal(before, repo.Head);
        Assert.Equal("refs/heads/feature", repo.Run("symbolic-ref", "-q", "HEAD"));
    }
}
