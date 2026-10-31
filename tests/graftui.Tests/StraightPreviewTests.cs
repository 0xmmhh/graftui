using graftui.Model;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;

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

    [Fact]
    public void Setting_a_scene_clears_the_single_line_flag_so_other_scenes_keep_the_normal_height()
    {
        var pane = new ArtPaneView { SingleLine = true };

        pane.Scene = _ => new SceneFrame(new ArtGrid(1, 1), null);

        Assert.False(pane.SingleLine);
    }

    [Fact]
    public void Show_scene_sets_the_height_with_the_scene_in_one_call()
    {
        var pane = new ArtPaneView();
        Func<ArtGeometry, SceneFrame> scene = _ => new SceneFrame(new ArtGrid(1, 1), null);

        pane.ShowScene(scene, singleLine: true);
        Assert.True(pane.SingleLine);
        Assert.Same(scene, pane.Scene);

        pane.ShowScene(scene);
        Assert.False(pane.SingleLine);
    }

    [Fact]
    public void The_single_line_flag_stays_until_the_next_scene_is_set()
    {
        var pane = new ArtPaneView { Scene = _ => new SceneFrame(new ArtGrid(1, 1), null) };

        pane.SingleLine = true;

        Assert.True(pane.SingleLine);
    }
}
