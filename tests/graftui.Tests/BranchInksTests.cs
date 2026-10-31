using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class BranchInksTests
{
    [Fact]
    public void A_lane_outside_the_palette_is_the_plain_foreground()
    {
        Assert.Equal(Ink.Fg, BranchInks.Of(BranchLanes.None));
        Assert.Equal(Ink.Fg, BranchInks.Of(BranchLanes.Count));
    }

    [Fact]
    public void Lanes_map_to_the_lane_inks_in_order()
    {
        Assert.Equal(Ink.Lane0, BranchInks.Of(0));
        Assert.Equal(Ink.Lane5, BranchInks.Of(5));
        for (var i = 0; i < BranchLanes.Count; i++)
            Assert.Equal((Ink)((int)Ink.Lane0 + i), BranchInks.Of(i));
    }

    [Fact]
    public void Current_and_selected_read_the_layout_lanes()
    {
        var layout = Layouts.Diverged() with { CurrentLane = 0, SelectedLane = 3 };
        Assert.Equal(Ink.Lane0, BranchInks.Current(layout));
        Assert.Equal(Ink.Lane3, BranchInks.Selected(layout));
    }

    [Fact]
    public void An_unstamped_layout_draws_both_branches_in_the_foreground()
    {
        var layout = Layouts.Diverged();
        Assert.Equal(Ink.Fg, BranchInks.Current(layout));
        Assert.Equal(Ink.Fg, BranchInks.Selected(layout));
    }
}
