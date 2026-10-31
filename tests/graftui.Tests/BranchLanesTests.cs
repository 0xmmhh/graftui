using graftui.Model;

namespace graftui.Tests;

public class BranchLanesTests
{
    // The sandbox's branch list, in git's order.
    private static readonly string[] Sandbox =
    [
        "feature/conflict", "feature/fast-forward", "feature/ff-to-merge", "feature/merge-commit", "feature/picks", "main",
    ];

    private static Branch B(string name, bool current = false) => new(name, "h", "h", current);

    private static List<Branch> Listed(params string[] names) => names.Select(n => B(n)).ToList();

    [Fact]
    public void Fnv1a_of_the_empty_string_is_the_offset_basis()
    {
        Assert.Equal(2166136261u, BranchLanes.Fnv1a(""));
    }

    [Fact]
    public void Fnv1a_of_a_matches_the_standard_vector()
    {
        Assert.Equal(0xe40c292cu, BranchLanes.Fnv1a("a"));
    }

    [Fact]
    public void The_sandbox_branches_get_the_pinned_lanes()
    {
        // Pins the hash: the same colours on every run and every machine.
        Assert.Equal(new[] { 3, 5, 1, 2, 4, 0 }, BranchLanes.Assign(Listed(Sandbox)));
    }

    [Fact]
    public void Main_is_lane_0_wherever_it_is_in_the_list()
    {
        Assert.Equal(0, BranchLanes.LaneOf(Listed("alpha", "main", "zeta"), "main"));
        Assert.Equal(0, BranchLanes.LaneOf(Listed("main"), "main"));
    }

    [Fact]
    public void Master_is_the_trunk_when_there_is_no_main()
    {
        Assert.Equal(new[] { 0, 3 }, BranchLanes.Assign(Listed("master", "topic")));
    }

    [Fact]
    public void Main_wins_over_master_and_master_takes_a_pool_lane()
    {
        Assert.Equal(new[] { 0, 1, 3 }, BranchLanes.Assign(Listed("main", "master", "topic")));
    }

    [Fact]
    public void The_current_branch_never_changes_the_lanes()
    {
        var plain = BranchLanes.Assign(Listed(Sandbox));
        var moved = Sandbox.Select(n => B(n, current: n == "feature/picks")).ToList();
        Assert.Equal(plain, BranchLanes.Assign(moved));
    }

    [Fact]
    public void Up_to_five_non_trunk_branches_all_get_different_lanes()
    {
        var lanes = BranchLanes.Assign(Listed(Sandbox)).Where(l => l != BranchLanes.Trunk).ToArray();
        Assert.Equal(lanes.Length, lanes.Distinct().Count());
    }

    [Fact]
    public void With_more_branches_than_pool_lanes_list_neighbours_never_share_a_lane()
    {
        var names = new List<string>();
        for (var i = 0; i < 12; i++)
            names.Add($"b{i:00}");
        names.Add("main");

        var lanes = BranchLanes.Assign(Listed(names.ToArray()));

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 1, 2, 3, 4, 5, 1, 4, 0 }, lanes);
        for (var i = 1; i < lanes.Length; i++)
            Assert.NotEqual(lanes[i - 1], lanes[i]);
    }

    [Fact]
    public void Adding_a_branch_with_a_free_preferred_lane_keeps_the_others()
    {
        Assert.Equal(3, BranchLanes.LaneOf(Listed("main", "topic"), "topic"));
        Assert.Equal(3, BranchLanes.LaneOf(Listed("feature/picks", "main", "topic"), "topic"));
        Assert.Equal(2, BranchLanes.LaneOf(Listed("feature/picks", "main", "topic"), "feature/picks"));
    }

    [Fact]
    public void A_name_that_is_not_listed_has_no_lane()
    {
        Assert.Equal(BranchLanes.None, BranchLanes.LaneOf(Listed(Sandbox), "nope"));
        Assert.Equal(BranchLanes.None, BranchLanes.LaneOf(Listed(Sandbox), ""));
    }

    [Fact]
    public void An_empty_list_has_no_lanes()
    {
        Assert.Empty(BranchLanes.Assign([]));
        Assert.Equal(BranchLanes.None, BranchLanes.LaneOf([], "main"));
    }

    [Fact]
    public void The_trunk_name_is_main_then_master_then_nothing()
    {
        Assert.Equal("main", BranchLanes.TrunkName(Listed("main", "master")));
        Assert.Equal("master", BranchLanes.TrunkName(Listed("master", "topic")));
        Assert.Null(BranchLanes.TrunkName(Listed("topic")));
    }

    [Fact]
    public void Preferred_is_always_a_pool_lane()
    {
        foreach (var name in Sandbox)
        {
            var p = BranchLanes.Preferred(name);
            Assert.InRange(p, 1, BranchLanes.Count - 1);
        }
    }

    [Fact]
    public void Without_a_trunk_every_branch_is_in_the_pool()
    {
        var lanes = BranchLanes.Assign(Listed("topic", "other"));
        foreach (var lane in lanes)
            Assert.InRange(lane, 1, BranchLanes.Count - 1);
    }
}
