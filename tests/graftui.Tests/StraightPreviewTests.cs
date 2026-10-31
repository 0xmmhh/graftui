using graftui.Model;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class StraightPreviewTests
{
    [Fact]
    public void Fast_forward_up_to_date_and_the_same_commit_are_straight()
    {
        Assert.True(BranchesTab.IsStraight(Layouts.FastForward()));
        Assert.True(BranchesTab.IsStraight(Layouts.UpToDate()));
        Assert.True(BranchesTab.IsStraight(Layouts.FastForward() with { Kind = GraphKind.Same }));
    }

    [Fact]
    public void A_diverged_or_unrelated_pair_is_not_straight()
    {
        Assert.False(BranchesTab.IsStraight(Layouts.Diverged()));
        Assert.False(BranchesTab.IsStraight(Layouts.Diverged() with { Kind = GraphKind.Unrelated }));
    }

    [Fact]
    public void A_detached_head_is_never_straight()
    {
        Assert.False(BranchesTab.IsStraight(Layouts.FastForward() with { Detached = true }));
    }
}
