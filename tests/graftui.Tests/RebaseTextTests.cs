using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

public class RebaseTextTests
{
    private const string H40 = "0123456789abcdef0123456789abcdef01234567";

    private static SequenceOutcome Done(int replayed, IReadOnlyList<string>? skipped = null, IReadOnlyList<string>? resolved = null,
        string message = "rebased feature/picks onto main")
    {
        var moves = Enumerable.Range(0, replayed).Select(i => new CommitMove("old" + i, "new" + i)).ToList();
        return new SequenceOutcome(SequenceOutcomeKind.Done, SequenceKind.Rebase, "feature/picks", "main", "oldtip", "newtip", null, moves,
            skipped ?? [], resolved ?? [], [], [], [], message, []);
    }

    private static SequenceStop Conflict(params string[] files) =>
        new(SequenceKind.Rebase, PauseKind.Conflict, "feature/conflict", "main", H40, "0123456", "Change line 1 on branch",
            files.Select(f => new ConflictFile(f, false)).ToList(), 1, 1, "", [], Ours: true);

    [Theory]
    [InlineData(1, "Enter rebases: 1 commit gets new hashes, a backup is saved. Esc cancels.")]
    [InlineData(3, "Enter rebases: 3 commits get new hashes, a backup is saved. Esc cancels.")]
    public void Confirm_says_how_many_commits_get_new_hashes(int n, string text)
    {
        Assert.Equal(text, RebaseText.Confirm(n));
    }

    [Fact]
    public void Done_names_the_replayed_commits_and_the_backup()
    {
        Assert.Equal("✔ Rebased feature/picks onto main: 3 commits replayed, old ones kept as backup",
            RebaseText.Done(Done(3), "feature/picks", "main"));
    }

    [Fact]
    public void Done_uses_the_singular_for_one_commit()
    {
        Assert.Equal("✔ Rebased feature/picks onto main: 1 commit replayed, old ones kept as backup",
            RebaseText.Done(Done(1), "feature/picks", "main"));
    }

    [Fact]
    public void Done_adds_the_skipped_count_before_the_conflict_note()
    {
        var text = RebaseText.Done(Done(0, skipped: ["a1b2c3d"], resolved: ["app.txt"]), "feature/conflict", "main");

        Assert.Equal("✔ Rebased feature/conflict onto main: 0 commits replayed, old ones kept as backup, 1 commit skipped (conflict resolved)",
            text);
    }

    [Fact]
    public void Done_counts_several_skipped_commits_in_the_plural()
    {
        var text = RebaseText.Done(Done(1, skipped: ["a", "b"]), "feature/picks", "main");

        Assert.EndsWith(", 2 commits skipped", text);
    }

    [Fact]
    public void PlainDone_has_the_wording_with_the_count_given()
    {
        Assert.Equal("✔ Rebased feature/picks onto main: 2 commits replayed, old ones kept as backup",
            RebaseText.PlainDone("feature/picks", "main", 2));
    }

    [Fact]
    public void Aborted_says_the_branch_is_unchanged()
    {
        Assert.Equal("✕ Rebase aborted: feature/conflict unchanged", RebaseText.Aborted("feature/conflict"));
    }

    [Fact]
    public void Failed_and_the_exit_line_carry_the_reason()
    {
        Assert.Equal("✕ Rebase failed: git broke", RebaseText.Failed("git broke"));
        Assert.Equal("graftui: rebase of feature/conflict onto main aborted: feature/conflict unchanged",
            RebaseText.ExitAborted("feature/conflict", "main"));
    }

    [Fact]
    public void Refusals_name_the_branches()
    {
        Assert.Equal("cannot rebase feature/picks onto itself", RebaseText.OntoItself("feature/picks"));
        Assert.Equal("main has no commits of its own since feature/fast-forward: nothing to rebase",
            RebaseText.NoOwn("main", "feature/fast-forward"));
    }

    [Fact]
    public void The_early_refusal_and_the_engine_say_the_same_words()
    {
        var early = RebaseText.EarlyRefusal(Layouts.FastForward(), "main", "feature");

        Assert.Equal(SequenceRefusals.NoOwnCommits("main", "feature"), early);
    }

    [Fact]
    public void A_conflicting_rebase_that_cannot_be_aborted_says_so_and_names_the_command()
    {
        Assert.Equal("the rebase stopped at a conflict and could not be aborted: run git rebase --abort", RebaseText.NotAborted);
    }

    [Fact]
    public void A_fast_forward_is_refused_before_git_is_asked()
    {
        var refusal = RebaseText.EarlyRefusal(Layouts.FastForward(), "main", "feature");

        Assert.Equal("main has no commits of its own since feature: nothing to rebase", refusal);
    }

    [Fact]
    public void A_diverged_branch_or_a_missing_layout_is_left_to_the_check()
    {
        Assert.Null(RebaseText.EarlyRefusal(Layouts.Diverged(), "main", "feature"));
        Assert.Null(RebaseText.EarlyRefusal(null, "main", "feature"));
    }

    [Fact]
    public void UndoSaveFailed_reads_the_outcome_message()
    {
        Assert.True(RebaseText.UndoSaveFailed(Done(1, message: "rebased x onto y; could not save the undo step: disk full")));
        Assert.False(RebaseText.UndoSaveFailed(Done(1)));
    }

    [Theory]
    [InlineData(RebaseAction.Pick, "pick")]
    [InlineData(RebaseAction.Reword, "reword")]
    [InlineData(RebaseAction.Edit, "edit")]
    [InlineData(RebaseAction.Squash, "squash")]
    [InlineData(RebaseAction.Fixup, "fixup")]
    [InlineData(RebaseAction.Drop, "drop")]
    public void ActionLabel_names_each_action_as_git_does(RebaseAction action, string label)
    {
        Assert.Equal(label, RebaseText.ActionLabel(action));
    }

    [Fact]
    public void ConflictLine_names_the_commit_its_subject_and_the_file()
    {
        Assert.Equal("Conflict while replaying 0123456 \"Change line 1 on branch\" · app.txt changed on both sides",
            RebaseText.ConflictLine(Conflict("app.txt")));
    }

    [Fact]
    public void Pause_captions_for_an_edit_and_a_message_stop()
    {
        var stop = Conflict("app.txt") with { ShortCommit = "b2c3d4e" };

        Assert.Equal("Stopped at b2c3d4e to edit · change files, stage them, then Amend & continue", RebaseText.EditStop(stop));
        Assert.Equal("Paused at b2c3d4e for its message · Continue opens it again", RebaseText.MessagePause(stop));
    }

    [Fact]
    public void ConflictRow_says_whether_the_file_is_resolved()
    {
        Assert.Equal("✖ app.txt  both modified", RebaseText.ConflictRow(new ConflictFile("app.txt", false)));
        Assert.Equal("✔ app.txt  resolved", RebaseText.ConflictRow(new ConflictFile("app.txt", true)));
    }

    [Fact]
    public void Rebasing_shows_the_step_and_the_total()
    {
        Assert.Equal("(rebasing 2/3)", RebaseText.Rebasing(2, 3));
    }

    [Fact]
    public void Reword_and_combine_boxes_have_their_titles_and_hints()
    {
        Assert.Equal("Reword b2c3d4e", RebaseText.RewordTitle("b2c3d4e"));
        Assert.Equal("Git holds this commit until you confirm its message.", RebaseText.RewordBody);
        Assert.Equal("Enter keeps it · Esc cancels", RebaseText.RewordHint);
        Assert.Equal("Combine messages", RebaseText.CombineTitle);
        Assert.Equal("b2c3d4e folds into a1b2c3d. Keep one message for the combined commit.", RebaseText.CombineBody("b2c3d4e", "a1b2c3d"));
        Assert.Equal("Enter keeps this message · Esc cancels", RebaseText.CombineHint);
    }

    [Fact]
    public void Files_and_Shorts_join_names_the_way_the_screens_do()
    {
        Assert.Equal("app.txt", RebaseText.Files(["app.txt"]));
        Assert.Equal("app.txt +2 more", RebaseText.Files(["app.txt", "b.txt", "c.txt"]));
        Assert.Equal("", RebaseText.Files([]));
        Assert.Equal("0123456, abcdef1", RebaseText.Shorts(["0123456789", "abcdef1234"]));
    }
}
