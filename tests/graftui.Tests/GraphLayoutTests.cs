using graftui.Model;

namespace graftui.Tests;

public class GraphLayoutTests
{
    [Fact]
    public void Minimal_layout_is_diverged_and_stubs_tips_to_seven_characters()
    {
        var layout = GraphLayout.Minimal("main", false, "feature", "c111111111", "d222222222");

        Assert.Equal(GraphKind.Diverged, layout.Kind);
        Assert.Equal("main", layout.Current);
        Assert.Equal("feature", layout.Selected);
        Assert.Equal("c111111", layout.CurrentTipCommit.ShortHash);
        Assert.Equal("d222222", layout.SelectedTipCommit.ShortHash);
    }

    private static Branch B(string name, bool current = false) => new(name, "h", "h", current);

    [Fact]
    public void A_new_layout_has_no_lanes_until_it_is_stamped()
    {
        var layout = Layouts.Diverged();
        Assert.Equal(BranchLanes.None, layout.CurrentLane);
        Assert.Equal(BranchLanes.None, layout.SelectedLane);
    }

    [Fact]
    public void With_lanes_reads_both_lanes_from_the_list()
    {
        var branches = new[] { B("feature", current: true), B("main") };
        var layout = Layouts.Diverged() with { Current = "main", Selected = "feature" };

        var stamped = layout.WithLanes(branches);

        Assert.Equal(BranchLanes.LaneOf(branches, "main"), stamped.CurrentLane);
        Assert.Equal(BranchLanes.LaneOf(branches, "feature"), stamped.SelectedLane);
        Assert.Equal(0, stamped.CurrentLane);
    }

    [Fact]
    public void With_lanes_keeps_the_rest_of_the_layout()
    {
        var layout = Layouts.Diverged() with { SelectedNew = [Layouts.C(Layouts.Sel)] };
        var stamped = layout.WithLanes([B("main"), B("feature")]);

        Assert.Equal(layout.SelectedNew, stamped.SelectedNew);
        Assert.Equal(layout.Kind, stamped.Kind);
        Assert.Equal(layout.Selected, stamped.Selected);
    }

    [Fact]
    public void A_detached_head_has_no_current_lane()
    {
        var layout = Layouts.Diverged() with { Current = "", Detached = true, Selected = "feature" };
        var stamped = layout.WithLanes([B("main"), B("feature")]);

        Assert.Equal(BranchLanes.None, stamped.CurrentLane);
        Assert.Equal(BranchLanes.LaneOf([B("main"), B("feature")], "feature"), stamped.SelectedLane);
    }

    [Fact]
    public void A_selected_name_that_is_not_listed_has_no_lane()
    {
        var stamped = Layouts.Diverged().WithLanes([B("main")]);

        Assert.Equal(BranchLanes.None, stamped.SelectedLane);
    }
}
