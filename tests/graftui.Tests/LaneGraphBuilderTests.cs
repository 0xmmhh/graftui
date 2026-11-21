using graftui.Model;

namespace graftui.Tests;

/// <summary>The lane graph builder on hand-made history. No git: each commit is a line in a test.</summary>
public sealed class LaneGraphBuilderTests
{
    // A commit whose subject is "s <id>".
    private static GraphCommit C(string id, params string[] parents) => Commit(id, "s " + id, parents);

    private static GraphCommit Commit(string id, string subject, params string[] parents) => new(id, id, parents, subject);

    // A first-parent chain, newest first: each commit's only parent is the next one down.
    private static List<GraphCommit> Chain(params string[] newestFirst)
    {
        var chain = new List<GraphCommit>();
        for (var i = 0; i < newestFirst.Length; i++)
        {
            var parents = i + 1 < newestFirst.Length ? new[] { newestFirst[i + 1] } : Array.Empty<string>();
            chain.Add(C(newestFirst[i], parents));
        }

        return chain;
    }

    // Count ids with the prefix, newest first: Ids(3) is a3, a2, a1.
    private static string[] Ids(int count, string prefix = "a")
    {
        var ids = new string[count];
        for (var i = 0; i < count; i++)
            ids[i] = prefix + (count - i);
        return ids;
    }

    // A line of count commits: prefix1 sits on from, prefix2 on prefix1, and so on. The tip is prefix + count.
    private static List<GraphCommit> Line(string prefix, int count, string from)
    {
        var line = new List<GraphCommit>();
        var parent = from;
        for (var i = 1; i <= count; i++)
        {
            var id = prefix + i;
            line.Add(C(id, parent));
            parent = id;
        }

        return line;
    }

    private static Branch Br(string name, string tip) => new(name, tip, tip, false);

    private static LaneGraph Build(IReadOnlyList<GraphCommit> baseChain, IReadOnlyList<GraphCommit> others, params Branch[] branches) =>
        LaneGraphBuilder.Build(new LaneGraphInput("main", baseChain, false, branches, others, false));

    private static Lane LaneNamed(LaneGraph graph, string name) => graph.Lanes.Single(l => l.Name == name);

    private static string[] Shorts(Lane lane) => lane.Commits.Select(c => c.ShortHash).ToArray();

    private static int[] Columns(Lane lane) => lane.Commits.Select(c => c.Column).ToArray();

    private static string[] Names(LaneGraph graph) => graph.Lanes.Select(l => l.Name).ToArray();

    // Base a3, a2, a1 with feature b1 on a3, s1 on b1, and a merge m of s1 into feature.
    private static LaneGraph SideMergeGraph() =>
        Build(Chain("a3", "a2", "a1"),
            [C("b1", "a3"), C("s1", "b1"), Commit("m", "Merge branch 'side-for-ffm' into feature/ff-to-merge", "b1", "s1")],
            Br("feature/ff-to-merge", "m"));

    // Feature with a merge m of two side lanes s1 and t1, both on b1.
    private static LaneGraph OctopusGraph() =>
        Build(Chain("a3", "a2", "a1"),
            [C("b1", "a3"), C("s1", "b1"), C("t1", "b1"), Commit("m", "Octopus merge", "b1", "s1", "t1")],
            Br("feature", "m"));

    // Feature has b1 on a1, then merges the base commit a3 in.
    private static LaneGraph BaseMergeGraph() =>
        Build(Chain("a3", "a2", "a1"),
            [C("b1", "a1"), Commit("m", "Merge main into feature", "b1", "a3")],
            Br("feature", "m"));

    // x and y both fork from a3; y merges x in.
    private static LaneGraph CrossMergeGraph() =>
        Build(Chain("a3", "a2", "a1"),
            [C("x1", "a3"), C("y1", "a3"), Commit("ym", "Merge x into y", "y1", "x1")],
            Br("x", "x1"),
            Br("y", "ym"));

    [Fact]
    public void Base_only_gives_one_lane_with_columns_0_to_n()
    {
        var graph = Build(Chain("a3", "a2", "a1"), []);

        var main = Assert.Single(graph.Lanes);
        Assert.Equal("main", main.Name);
        Assert.Equal(LaneKind.Base, main.Kind);
        Assert.Equal(new[] { "a1", "a2", "a3" }, Shorts(main));
        Assert.Equal(new[] { 0, 1, 2 }, Columns(main));
        Assert.Equal(3, graph.Columns);
        Assert.Empty(graph.Links);
    }

    [Fact]
    public void A_branch_forks_from_the_commit_it_was_made_on()
    {
        var graph = Build(Chain("a3", "a2", "a1"), [C("b1", "a2")], Br("feature", "b1"));

        var feature = LaneNamed(graph, "feature");
        Assert.Equal(new[] { "b1" }, Shorts(feature));
        Assert.Equal(new[] { 2 }, Columns(feature));
        Assert.Equal(new LaneLink(LinkKind.Fork, 0, 1, 1, 2), Assert.Single(graph.Links));
    }

    [Fact]
    public void A_fast_forward_branch_starts_after_the_tip()
    {
        var graph = Build(Chain("a3", "a2", "a1"), [C("b1", "a3")], Br("feature", "b1"));

        Assert.Equal(new[] { 3 }, Columns(LaneNamed(graph, "feature")));
        Assert.Equal(4, graph.Columns);
    }

    [Fact]
    public void The_base_window_starts_one_commit_before_the_oldest_fork()
    {
        var graph = Build(Chain(Ids(10)), [C("b1", "a8")], Br("feature", "b1"));

        var main = LaneNamed(graph, "main");
        Assert.Equal(new[] { "a7", "a8", "a9", "a10" }, Shorts(main));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Columns(main));
        Assert.True(graph.BaseMore);
    }

    [Fact]
    public void Without_forks_the_base_shows_its_newest_three()
    {
        var graph = Build(Chain(Ids(10)), []);

        Assert.Equal(new[] { "a8", "a9", "a10" }, Shorts(LaneNamed(graph, "main")));
        Assert.True(graph.BaseMore);
    }

    [Fact]
    public void A_fork_older_than_the_window_comes_from_the_left()
    {
        var input = new LaneGraphInput("main", Chain(Ids(12)), true, [Br("feature", "b1")], [C("b1", "z0")], false);

        var graph = LaneGraphBuilder.Build(input);

        var feature = LaneNamed(graph, "feature");
        Assert.Equal(new LaneLink(LinkKind.Fork, 0, -1, 1, 1), Assert.Single(graph.Links));
        Assert.Equal(0, feature.Gap);
        Assert.Equal(new[] { 1 }, Columns(feature));
        Assert.True(graph.BaseMore);
    }

    [Fact]
    public void A_long_branch_keeps_its_newest_eight()
    {
        var graph = Build(Chain("a3", "a2", "a1"), Line("b", 11, "a3"), Br("feature", "b11"));

        var feature = LaneNamed(graph, "feature");
        Assert.Equal(8, feature.Commits.Count);
        Assert.Equal(3, feature.Hidden);
        Assert.Equal(3, feature.Gap);
        Assert.Equal("b4", feature.Commits[0].ShortHash);
        Assert.Equal(4, feature.Commits[0].Column);
    }

    [Fact]
    public void A_stacked_branch_forks_from_the_shorter_one()
    {
        var others = new[] { C("x1", "a3"), C("x2", "x1"), C("y1", "x2") };

        var graph = Build(Chain("a3", "a2", "a1"), others, Br("a", "x2"), Br("b", "y1"));

        Assert.Equal(new[] { "main", "a", "b" }, Names(graph));
        Assert.Equal(1, graph.Lanes[2].Parent);
        Assert.Equal(1, graph.LaneOf("a"));
        Assert.Equal(new[] { 1, 2 }, graph.IncomingLanes("b").ToArray());
        Assert.Equal(new[] { 1 }, graph.IncomingLanes("a").ToArray());
    }

    [Fact]
    public void Two_branches_on_one_commit_give_one_lane_and_a_pointer()
    {
        var graph = Build(Chain("a3", "a2", "a1"), [C("b1", "a2")], Br("alpha", "b1"), Br("beta", "b1"));

        Assert.Equal(new[] { "main", "alpha" }, Names(graph));
        Assert.Equal(new BranchPointer("beta", 1, 2), Assert.Single(graph.Pointers));
    }

    [Fact]
    public void A_branch_at_the_base_tip_is_a_pointer_on_the_base()
    {
        var graph = Build(Chain("a3", "a2", "a1"), [], Br("same", "a3"), Br("old", "a2"));

        Assert.Equal(new BranchPointer("same", 0, 2), graph.Pointers[0]);
        Assert.Equal(new BranchPointer("old", 0, 1), graph.Pointers[1]);
        Assert.Equal((0, 2), graph.TipOf("same"));
        Assert.Equal((0, 1), graph.TipOf("old"));
    }

    [Fact]
    public void A_pointer_older_than_the_window_has_column_minus_one()
    {
        // Fourteen base commits: the window shows the newest twelve, so a1 is out of it.
        var graph = Build(Chain(Ids(14)), [], Br("ancient", "a1"));

        var pointer = Assert.Single(graph.Pointers);
        Assert.Equal(-1, pointer.Column);
        Assert.Equal((-1, -1), graph.TipOf("ancient"));
    }

    [Fact]
    public void A_merged_side_branch_gets_its_own_lane()
    {
        var graph = SideMergeGraph();

        var feature = LaneNamed(graph, "feature/ff-to-merge");
        Assert.Equal(new[] { "b1", "m" }, Shorts(feature));
        Assert.Equal(new[] { 3, 5 }, Columns(feature));

        var side = graph.Lanes[2];
        Assert.Equal(LaneKind.Side, side.Kind);
        Assert.Equal("side-for-ffm", side.Name);
        Assert.Equal(1, side.Parent);
        Assert.Equal(feature.ColorLane, side.ColorLane);
        Assert.Equal(new[] { "s1" }, Shorts(side));
        Assert.Equal(new[] { 4 }, Columns(side));

        Assert.Contains(new LaneLink(LinkKind.Fork, 1, 3, 2, 4), graph.Links);
        Assert.Contains(new LaneLink(LinkKind.Merge, 2, 4, 1, 5), graph.Links);
        Assert.Equal(new[] { 1, 2 }, graph.IncomingLanes("feature/ff-to-merge").ToArray());
    }

    [Fact]
    public void An_octopus_merge_gives_one_side_lane_per_extra_parent()
    {
        var graph = OctopusGraph();

        Assert.Equal(2, graph.Lanes.Count(l => l.Kind == LaneKind.Side));
        var merge = Columns(LaneNamed(graph, "feature"))[1];
        Assert.Equal(5, merge);
        Assert.Equal(2, graph.Links.Count(l => l.Kind == LinkKind.Merge && l.ToLane == 1 && l.ToColumn == merge));
    }

    [Fact]
    public void Merging_the_base_into_a_branch_links_from_the_base()
    {
        var graph = BaseMergeGraph();

        Assert.Equal(2, graph.Lanes.Count);
        Assert.Equal(new[] { 1 }, Columns(LaneNamed(graph, "feature")).Take(1).ToArray());
        Assert.Equal(new LaneLink(LinkKind.Merge, 0, 2, 1, 3), graph.Links.Single(l => l.Kind == LinkKind.Merge));
    }

    [Fact]
    public void A_branch_merged_into_another_links_between_lanes()
    {
        var graph = CrossMergeGraph();

        Assert.Equal(3, graph.Lanes.Count);
        Assert.Equal(LaneKind.Branch, graph.Lanes[1].Kind);
        Assert.Contains(new LaneLink(LinkKind.Merge, 1, 3, 2, 4), graph.Links);
    }

    // anc: p1 on a3, m1 merges sA into it (column 4), p2, then m2 merges sB into it (column 7). feat forks from anc at m1 (column 4), so
    // sA (merged at 4, on the fork point) is in feat's history and sB (merged later) is not.
    private static LaneGraph AncestorSideGraph() =>
        Build(Chain("a3", "a2", "a1"),
            [C("p1", "a3"), C("sA", "a3"), Commit("m1", "Merge branch 'sA' into anc", "p1", "sA"), C("p2", "m1"),
             C("sB", "p2"), Commit("m2", "Merge branch 'sB' into anc", "p2", "sB"), C("f1", "m1"), C("f2", "f1")],
            Br("anc", "m2"),
            Br("feat", "f2"));

    private static string[] IncomingNames(LaneGraph graph, string branch) =>
        graph.IncomingLanes(branch).Select(i => graph.Lanes[i].Name).ToArray();

    [Fact]
    public void A_side_lane_merged_into_an_ancestor_at_the_fork_point_is_incoming()
    {
        var graph = AncestorSideGraph();

        // Rows in order: the ancestor anc, feat, then sA. sB is not there.
        Assert.Equal(new[] { "anc", "feat", "sA" }, IncomingNames(graph, "feat"));
    }

    [Fact]
    public void A_side_lane_merged_into_an_ancestor_after_the_fork_point_is_not_incoming()
    {
        var names = IncomingNames(AncestorSideGraph(), "feat");

        Assert.DoesNotContain("sB", names);
    }

    [Fact]
    public void An_unrelated_branch_has_no_fork_and_goes_last()
    {
        var others = new[] { C("b1", "a3"), C("r2", "r1"), C("r1") };

        var graph = Build(Chain("a3", "a2", "a1"), others, Br("feature", "b1"), Br("r", "r2"));

        var unrelated = graph.Lanes[2];
        Assert.Equal("r", unrelated.Name);
        Assert.Equal(-1, unrelated.Parent);
        Assert.Equal(new[] { 0, 1 }, Columns(unrelated));
        Assert.Single(graph.Links);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_missing_parent_is_drawn_from_the_left(bool othersCut)
    {
        var input = new LaneGraphInput("main", Chain("a3", "a2", "a1"), false, [Br("feature", "b1")], [C("b1", "z0")], othersCut);

        var graph = LaneGraphBuilder.Build(input);

        Assert.Equal(new LaneLink(LinkKind.Fork, 0, -1, 1, 1), Assert.Single(graph.Links));
        Assert.Equal(0, LaneNamed(graph, "feature").Gap);
        Assert.Equal(othersCut, graph.OthersCut);
    }

    [Fact]
    public void Rows_go_by_fork_column_then_name()
    {
        var others = new[] { C("pa", "a2"), C("pb", "a2"), C("pc", "a3") };

        var graph = Build(Chain("a3", "a2", "a1"), others, Br("b", "pb"), Br("a", "pa"), Br("c", "pc"));

        Assert.Equal(new[] { "main", "c", "a", "b" }, Names(graph));
    }

    [Fact]
    public void Columns_rise_along_every_lane()
    {
        AssertColumnsRise(SideMergeGraph());
        AssertColumnsRise(OctopusGraph());
        AssertColumnsRise(BaseMergeGraph());
        AssertColumnsRise(CrossMergeGraph());
    }

    [Fact]
    public void Colours_come_from_BranchLanes()
    {
        var branches = new[] { Br("main", "a3"), Br("feature", "b1"), Br("zeta", "b1") };
        var input = new LaneGraphInput("main", Chain("a3", "a2", "a1"), false, branches, [C("b1", "a2")], false);

        var graph = LaneGraphBuilder.Build(input);

        Assert.Equal(0, graph.Lanes[0].ColorLane);
        Assert.Equal(BranchLanes.LaneOf(branches, "feature"), LaneNamed(graph, "feature").ColorLane);
    }

    [Fact]
    public void Too_many_branches_are_left_out_by_group()
    {
        var branches = Enumerable.Range(1, 70).Select(i => Br("f" + i.ToString("D2"), "x" + i)).ToList();
        var others = Enumerable.Range(1, 70).Select(i => C("x" + i, "a3")).ToList();
        var input = new LaneGraphInput("main", Chain("a3", "a2", "a1"), false, branches, others, false);

        var graph = LaneGraphBuilder.Build(input);

        Assert.Equal(60, graph.Lanes.Count);
        Assert.Equal(11, graph.LeftOut);
    }

    [Fact]
    public void Empty_base_gives_the_empty_graph()
    {
        var graph = LaneGraphBuilder.Build(new LaneGraphInput("main", [], false, [], [], false));

        Assert.Same(LaneGraph.Empty, graph);
    }

    [Fact]
    public void A_hidden_merge_parent_stays_left_of_the_first_kept_commit()
    {
        // l1 is hidden (nine commits, eight kept) and merges in a3, the base tip. The first kept commit must come after a3.
        var others = new List<GraphCommit> { Commit("l1", "Merge main into feature", "a1", "a3") };
        for (var i = 2; i <= 9; i++)
            others.Add(C("l" + i, "l" + (i - 1)));

        var graph = Build(Chain("a3", "a2", "a1"), others, Br("feature", "l9"));

        var feature = LaneNamed(graph, "feature");
        Assert.Equal(1, feature.Hidden);
        Assert.Equal(3, feature.Gap);
        Assert.Equal("l2", feature.Commits[0].ShortHash);
        Assert.Equal(4, feature.Commits[0].Column);
    }

    [Fact]
    public void A_loop_through_hidden_commits_is_placed_without_the_loop()
    {
        var graph = Build(Chain("a3", "a2", "a1"), LoopOthers(), Br("a", "l10"), Br("b", "m9"));

        Assert.Equal(new[] { "main", "a", "b" }, Names(graph));
        Assert.All(graph.Lanes.Skip(1).SelectMany(l => l.Commits), c => Assert.True(c.Column >= 1));
        AssertColumnsRise(graph);
    }

    [Fact]
    public void An_independent_hidden_merge_keeps_its_order_next_to_a_loop()
    {
        // The loop, plus lane c: c1 is hidden and merges a3, the base tip. Only the loop's edge is left out; c2 must still come
        // after a3 (column 2), so c2 is at column 4 and not 2.
        var others = LoopOthers();
        others.Add(Commit("c1", "Merge main into c", "a1", "a3"));
        for (var i = 2; i <= 9; i++)
            others.Add(C("c" + i, "c" + (i - 1)));

        var graph = Build(Chain("a3", "a2", "a1"), others, Br("a", "l10"), Br("b", "m9"), Br("c", "c9"));

        var c = LaneNamed(graph, "c");
        Assert.Equal(1, c.Hidden);
        Assert.Equal(3, c.Gap);
        Assert.Equal(4, c.Commits[0].Column);
        Assert.All(graph.Lanes.Skip(1).SelectMany(l => l.Commits), x => Assert.True(x.Column >= 1));
        AssertColumnsRise(graph);
    }

    // Lane a: l1..l10, where l2 merges m9. Lane b forks from l1 (hidden in a) and runs m1..m9, with m1 hidden. The edge from m9 to a's
    // gap would close a loop (a's gap before b, b's m9 before a's gap), so that edge is left out.
    private static List<GraphCommit> LoopOthers()
    {
        var others = new List<GraphCommit> { C("l1", "a1"), C("l2", "l1", "m9") };
        for (var i = 3; i <= 10; i++)
            others.Add(C("l" + i, "l" + (i - 1)));
        others.Add(C("m1", "l1"));
        for (var i = 2; i <= 9; i++)
            others.Add(C("m" + i, "m" + (i - 1)));
        return others;
    }

    [Theory]
    [InlineData("Merge branch 'x' into y", "x")]
    [InlineData("Merge branch 'x'", "x")]
    [InlineData("Merge pull request #12 from o/x", "o/x")]
    [InlineData("Merge remote-tracking branch 'origin/x'", "origin/x")]
    [InlineData("Add file", "")]
    [InlineData("Merge branch '", "")]
    [InlineData("Merge branches 'a' and 'b'", "")]
    public void SideName_reads_the_branch_from_a_merge_subject(string subject, string expected) =>
        Assert.Equal(expected, LaneGraphBuilder.SideName(subject));

    [Theory]
    [InlineData("Merge branch 'x' into y", "x")]
    [InlineData("Merge tag 'v1'", "Merge tag 'v1'")]
    [InlineData("", "")]
    public void SideLabel_is_the_branch_name_or_else_the_whole_subject(string subject, string expected) =>
        Assert.Equal(expected, LaneGraphBuilder.SideLabel(subject));

    // A merged-in lane with no branch name in its merge subject is still labelled, from the subject (decided 2026-11-21).
    [Fact]
    public void An_unnamed_side_lane_is_labelled_from_its_merge_subject()
    {
        var graph = Build(Chain("a3", "a2", "a1"),
            [C("b1", "a3"), C("s1", "b1"), Commit("m", "Merge tag 'v1'", "b1", "s1")],
            Br("feature", "m"));

        Assert.Equal("Merge tag 'v1'", graph.Lanes.Single(l => l.Kind == LaneKind.Side).Name);
    }

    // Every lane's columns rise along it, and every link goes from a lower column to a higher one.
    private static void AssertColumnsRise(LaneGraph graph)
    {
        foreach (var lane in graph.Lanes)
        {
            for (var i = 1; i < lane.Commits.Count; i++)
                Assert.True(lane.Commits[i].Column > lane.Commits[i - 1].Column, $"{lane.Name}: columns do not rise");
        }

        foreach (var link in graph.Links)
        {
            if (link.FromColumn >= 0)
                Assert.True(link.ToColumn > link.FromColumn, $"link {link.Kind} {link.FromLane}->{link.ToLane} does not move right");
        }
    }
}
