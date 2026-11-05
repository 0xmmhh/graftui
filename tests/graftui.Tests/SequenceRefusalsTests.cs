using graftui.Operations;

namespace graftui.Tests;

/// <summary>The refusal texts of the rebase and the cherry-pick, word for word. The first lines are the approved wording.</summary>
public class SequenceRefusalsTests
{
    [Fact]
    public void Dirty_text_names_the_first_file_and_counts_the_rest()
    {
        Assert.Equal(
            "you have uncommitted changes to app.txt +2 more: commit or stash them before rebasing",
            SequenceRefusals.RebaseDirty(["app.txt", "b", "c"]));
    }

    [Fact]
    public void Dirty_text_for_one_file_has_no_count()
    {
        Assert.Equal(
            "you have uncommitted changes to app.txt: commit or stash them before rebasing",
            SequenceRefusals.RebaseDirty(["app.txt"]));
    }

    [Fact]
    public void Dirty_pick_text_says_picking_and_counts_the_rest()
    {
        Assert.Equal(
            "you have uncommitted changes to auth.ts +1 more: commit or stash them before picking",
            SequenceRefusals.PickDirty(["auth.ts", "b.ts"]));
    }

    [Fact]
    public void Rebase_approved_refusals_are_word_for_word()
    {
        Assert.Equal("you have uncommitted changes to app.txt: commit or stash them before rebasing",
            SequenceRefusals.RebaseDirty(["app.txt"]));
        Assert.Equal("feature/login is already on top of main: nothing to rebase",
            SequenceRefusals.AlreadyOnTop("feature/login", "main"));
        Assert.Equal("dropping every commit would leave feature/login empty: keep at least one",
            SequenceRefusals.EmptyPlan("feature/login"));
        Assert.Equal("squash needs a commit above it to fold into", SequenceRefusals.FoldFirst("squash"));
        Assert.Equal("fixup needs a commit above it to fold into", SequenceRefusals.FoldFirst("fixup"));
    }

    [Fact]
    public void Cherry_pick_approved_refusals_are_word_for_word()
    {
        Assert.Equal("you have uncommitted changes to auth.ts: commit or stash them before picking",
            SequenceRefusals.PickDirty(["auth.ts"]));
        Assert.Equal("8e1f2a3 is already on main (same change as 5b6c7d8): nothing to pick",
            SequenceRefusals.AlreadyApplied("8e1f2a3", "main", "5b6c7d8"));
        Assert.Equal("these commits are already on main", SequenceRefusals.OnBranch("main"));
    }

    [Fact]
    public void New_rebase_refusals_are_word_for_word()
    {
        Assert.Equal("feature has no commits of its own since main: nothing to rebase",
            SequenceRefusals.NoOwnCommits("feature", "main"));
        Assert.Equal("feature has a merge commit since main: graftui won't rebase over merges",
            SequenceRefusals.HasMerges("feature", "main"));
        Assert.Equal("select another branch to rebase feature onto", SequenceRefusals.OntoSelf("feature"));
        Assert.Equal("feature and orphan share no history: nothing to rebase onto",
            SequenceRefusals.Unrelated("feature", "orphan"));
        Assert.Equal("feature changed since the plan was made; nothing was rebased", SequenceRefusals.PlanStale("feature"));
        Assert.Equal("feature moved since you started; nothing changed", SequenceRefusals.Moved("feature"));
    }

    [Fact]
    public void New_cherry_pick_refusals_are_word_for_word()
    {
        Assert.Equal("8e1f2a3 is a merge commit: graftui won't pick it", SequenceRefusals.MergeCommit("8e1f2a3"));
        Assert.Equal("there is no commit nope", SequenceRefusals.NoCommit("nope"));
        Assert.Equal("select a commit to pick first", SequenceRefusals.NothingSelected);
    }

    [Fact]
    public void New_pause_refusals_are_word_for_word()
    {
        Assert.Equal("nothing is paused: there is no rebase or cherry-pick to continue", SequenceRefusals.NothingPaused);
        Assert.Equal("the paused step changed since you pressed the button; nothing was done", SequenceRefusals.PauseChanged);
        Assert.Equal("app.txt +1 more still has conflict markers: resolve them before continuing",
            SequenceRefusals.Unresolved(["app.txt", "b.txt"]));
        Assert.Equal("app.txt has unstaged changes: stage it first, then Amend & continue", SequenceRefusals.StageFirst("app.txt"));
        Assert.Equal("only a conflicted commit can be skipped", SequenceRefusals.SkipOnlyConflict);
    }
}
