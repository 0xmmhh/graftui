using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The one plain line the simplified Commands pane shows for an undo: for each undo kind the line comes from the step's own
/// description, the plumbing the undo ran is hidden there (a failure excepted), and the exact pane (v) shows the same
/// commands with and without the hint.
/// </summary>
public class PlainUndoLineTests
{
    private static readonly CommandHints UndoHints = new(Undo: true);

    /// <summary>The undo ran and undid a step: assert the pane contract for its outcome.</summary>
    private static void CheckPane(UndoOutcome o, string note)
    {
        Assert.Equal(UndoOutcomeKind.Undone, o.Kind);
        Assert.Equal(note, UndoText.PlainLine(o.Step!));

        // The simplified pane shows the undo as the one plain line: every command it ran is hidden there.
        foreach (var r in o.Commands)
        {
            if (CommandDisplay.IsFailure(r))
                continue;
            Assert.Null(CommandDisplay.Simplify(r, UndoHints));
        }
        // Without the hint nothing changes: the plumbing of an undo shows as any other command's does.
        Assert.Contains(o.Commands, r => CommandDisplay.Simplify(r, CommandHints.None) is not null);

        // The exact pane is unchanged: the same exact lines with and without the hint.
        foreach (var r in o.Commands)
            Assert.Equal(CommandDisplay.Entry(r, CommandHints.None, false).Exact,
                CommandDisplay.Entry(r, UndoHints, false).Exact);

        // The note itself is simplified-only: no exact line, one dim message line.
        var entry = CommandDisplay.UndoNote(note);
        Assert.Empty(entry.Exact);
        var line = Assert.Single(entry.Simple);
        Assert.Equal(LogKind.Message, line.Kind);
        Assert.Equal(note, line.Text);
    }

    private static UndoOutcome Undo(TempRepo repo)
    {
        var op = new UndoOperation(repo.Git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new UndoRequest(check.StepRef), check);
    }

    private static FileChange UnstagedRow(TempRepo repo, string path)
    {
        var rows = new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Status.Rows;
        foreach (var row in rows)
        {
            if (row.Side == ChangeSide.Unstaged && row.Path == path)
                return row;
        }
        throw new InvalidOperationException("no unstaged row for " + path);
    }

    private static CommitOutcome Commit(TempRepo repo, string subject, bool amend = false)
    {
        var op = new CommitOperation(repo.Git);
        var request = new CommitRequest(amend, subject, "", null, false);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_commit()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write("app.txt", "b\n");
        repo.Run("add", "app.txt");
        Assert.Equal(CommitKind.Committed, Commit(repo, "Change app").Kind);

        CheckPane(Undo(repo), "↶ undo: the commit");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_an_amend()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write("app.txt", "b\n");
        repo.Run("add", "app.txt");
        Assert.Equal(CommitKind.Committed, Commit(repo, "Change app").Kind);
        Assert.Equal(CommitKind.Amended, Commit(repo, "Change app again", amend: true).Kind);

        CheckPane(Undo(repo), "↶ undo: the amend");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_merge()
    {
        using var repo = TempRepo.FastForwardable();
        var op = new MergeOperation(repo.Git);
        var check = op.Check(new MergeRequest("feature"));
        Assert.True(check.CanMerge, check.Refusal);
        Assert.Equal(MergeKind.FastForward, op.Run(new MergeRequest("feature"), check).Kind);

        CheckPane(Undo(repo), "↶ undo: the merge of feature");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_stash()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write("app.txt", "stashed\n");
        var stash = new StashOperation(repo.Git);
        var stashCheck = stash.Check(new StashRequest(null));
        Assert.True(stashCheck.CanRun, stashCheck.Refusal);
        Assert.Equal(StashKind.Stashed, stash.Run(new StashRequest(null), stashCheck).Kind);

        CheckPane(Undo(repo), "↶ undo: brought the stashed changes back");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_pop()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write("app.txt", "stashed\n");
        var stash = new StashOperation(repo.Git);
        var stashCheck = stash.Check(new StashRequest(null));
        Assert.True(stashCheck.CanRun, stashCheck.Refusal);
        Assert.Equal(StashKind.Stashed, stash.Run(new StashRequest(null), stashCheck).Kind);
        var pop = new PopOperation(repo.Git);
        var popCheck = pop.Check(new PopRequest(null));
        Assert.True(popCheck.CanRun, popCheck.Refusal);
        Assert.Equal(PopKind.Popped, pop.Run(new PopRequest(null), popCheck).Kind);

        CheckPane(Undo(repo), "↶ undo: put the stash entry back");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_stash_merge()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Write("a.txt", "edited\n");
        var op = new StashMergeOperation(repo.Git);
        var check = op.Check(new StashMergeRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(StashMergeKind.Merged, op.Run(new StashMergeRequest("feature"), check).Kind);

        CheckPane(Undo(repo), "↶ undo: put the stash-merge back");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_discard()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write("app.txt", "discarded\n");
        var op = new DiscardOperation(repo.Git);
        var request = new DiscardRequest(UnstagedRow(repo, "app.txt"));
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(DiscardKind.Discarded, op.Run(request, check).Kind);

        CheckPane(Undo(repo), "↶ undo: the discard of app.txt");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_branch_creation()
    {
        using var repo = TempRepo.FastForwardable();
        var op = new CreateBranchOperation(repo.Git);
        var check = op.Check(new CreateBranchRequest("topic"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new CreateBranchRequest("topic"), check).Kind);

        CheckPane(Undo(repo), "↶ undo: the creation of topic");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_branch_rename()
    {
        using var repo = TempRepo.FastForwardable();
        var create = new CreateBranchOperation(repo.Git);
        var createCheck = create.Check(new CreateBranchRequest("topic"));
        Assert.True(createCheck.CanRun, createCheck.Refusal);
        Assert.Equal(BranchChangeKind.Done, create.Run(new CreateBranchRequest("topic"), createCheck).Kind);
        var op = new RenameBranchOperation(repo.Git);
        var request = new RenameBranchRequest("topic", "renamed");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(request, check).Kind);

        CheckPane(Undo(repo), "↶ undo: the rename of topic");
    }

    [Fact]
    public void The_pane_shows_one_plain_line_for_the_undo_of_a_branch_deletion()
    {
        using var repo = TempRepo.FastForwardable();
        var delete = new DeleteBranchOperation(repo.Git);
        var deleteCheck = delete.Check(new DeleteBranchRequest("feature", null, true));
        Assert.True(deleteCheck.CanRun, deleteCheck.Refusal);
        Assert.Equal(BranchChangeKind.Done, delete.Run(new DeleteBranchRequest("feature", null, true), deleteCheck).Kind);

        CheckPane(Undo(repo), "↶ undo: the deletion of feature");
    }

    [Fact]
    public void A_failure_inside_an_undo_is_still_shown_exactly()
    {
        var r = new GitResult("git reset --hard deadbeef", 128, "", "fatal: ambiguous argument") { Args = ["reset", "--hard", "deadbeef"] };

        var shown = CommandDisplay.Simplify(r, UndoHints);

        Assert.NotNull(shown);
        Assert.True(shown.Exact);
        Assert.Equal(r.Command, shown.Text);
    }

    [Fact]
    public void The_plain_line_of_the_other_kinds_comes_from_their_own_description()
    {
        // Unstage steps, and the kinds the real flows above do not make: the line is "the " + the step's own description.
        Assert.Equal("↶ undo: the unstage of app.txt",
            UndoText.PlainLine(new UndoRecord(UndoKind.Unstage, "unstage of app.txt", [])));
        // A rebase saves its step as a merge-kind one whose description names the rebase.
        Assert.Equal("↶ undo: the rebase of topic onto feature",
            UndoText.PlainLine(new UndoRecord(UndoKind.Merge, "rebase of topic onto feature", [])));
        Assert.Equal("↶ undo: the cherry-pick of abc1234",
            UndoText.PlainLine(new UndoRecord(UndoKind.Merge, "cherry-pick of abc1234", [])));
    }
}
