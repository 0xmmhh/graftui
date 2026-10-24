using Graft.Model;
using Graft.Ui.Scenes;

namespace Graft.Tests;

public class MergeAnimationTests
{
    [Fact]
    public void While_git_has_not_answered_the_title_is_merging()
    {
        var anim = new MergeAnimation(Layouts.Diverged());

        Assert.Equal("Merging", anim.Title(0));
    }

    [Fact]
    public void A_conflict_finishes_after_grow_flash_and_retract()
    {
        var anim = new MergeAnimation(Layouts.Diverged());
        var conflict = new MergeOutcome(MergeKind.Conflict, "feature", "main", null, ["app.txt"], "merge aborted", []);

        anim.SetOutcome(conflict, 1200);

        Assert.False(anim.IsDone(2449));
        Assert.True(anim.IsDone(2450));
        Assert.Equal("Aborted", anim.Title(2450));
    }

    [Fact]
    public void A_wrong_prediction_jumps_to_the_real_result()
    {
        // The layout predicts a fast-forward, git reports a merge commit.
        var anim = new MergeAnimation(Layouts.FastForward());
        var merged = new MergeOutcome(MergeKind.MergeCommit, "feature", "main", "e333333333", [], "merge commit created", []);

        anim.SetOutcome(merged, 500);

        Assert.True(anim.IsDone(1150));
        Assert.Equal("Merged", anim.Title(1150));
    }
}
