using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Tests;

/// <summary>Fitting a lane graph into a grid. G is the sandbox graph, built in memory.</summary>
public sealed class LaneFitTests
{
    private static readonly LaneGraph G = SandboxLaneGraph.Build();

    // A commit whose subject is "s <id>".
    private static GraphCommit C(string id, params string[] parents) => new(id, id, parents, "s " + id);

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

    private static Branch Br(string name, string tip) => new(name, tip, tip, false);

    private static LaneGraph Graph(IReadOnlyList<GraphCommit> baseChain, IReadOnlyList<GraphCommit> others, params Branch[] branches) =>
        LaneGraphBuilder.Build(new LaneGraphInput("main", baseChain, false, branches, others, false));

    private static LanePlacement Fit(string selected, GhostKind ghost, int width, int height) =>
        LaneFit.Fit(G, selected, ghost, width, height);

    private static string Describe(Lane lane) =>
        lane.Name + ": " + string.Join(" ", lane.Commits.Select(c => c.ShortHash + "@" + c.Column));

    [Fact]
    public void The_fixture_is_the_sandbox_graph()
    {
        Assert.Equal(SandboxLaneGraph.Rows, G.Lanes.Select(Describe).ToArray());
        Assert.Equal(LaneKind.Side, G.Lanes[3].Kind);
        Assert.Equal(6, G.Columns);
        Assert.False(G.BaseMore);
        Assert.False(G.OthersCut);
        Assert.Empty(G.Pointers);
    }

    [Fact]
    public void Empty_graph_has_no_rows()
    {
        var p = LaneFit.Fit(LaneGraph.Empty, "", GhostKind.Merge, 86, 20);

        Assert.Empty(p.Rows);
        Assert.Empty(p.RowY);
        Assert.Equal(0, p.Height);
    }

    [Fact]
    public void Below_twelve_columns_has_no_rows()
    {
        Assert.Empty(Fit("feature/merge-commit", GhostKind.Merge, 11, 20).Rows);
        Assert.NotEmpty(Fit("feature/merge-commit", GhostKind.Merge, 12, 20).Rows);
    }

    [Fact]
    public void Wide_pane()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merge, 86, 20);

        Assert.Equal(12, p.LabelWidth);
        Assert.Equal(15, p.GraphX);
        Assert.Equal(8, p.Step);
        Assert.Equal(0, p.FirstColumn);
        Assert.Equal(7, p.HashWidth);
        Assert.Equal(3, p.RowGap);
        Assert.Equal(new[] { 1, 4, 7, 10, 13, 16, 19 }, p.RowY);
        Assert.Equal(20, p.Height);
        Assert.Equal(63, p.GhostX);
        Assert.Equal("new merge commit", p.GhostLabel);
        Assert.Equal(0, p.More);
        Assert.False(p.ShowMore);
        Assert.Equal(new[] { 0, 5 }, p.OnRows);
        Assert.Equal(5, p.SelectedLane);
        Assert.Equal(5, p.TipLane);
        Assert.Equal(2, p.TipColumn);
    }

    [Fact]
    public void Pane_of_an_80_column_screen()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merge, 47, 8);

        Assert.Equal(5, p.Step);
        Assert.Equal(0, p.HashWidth);
        Assert.Equal(1, p.RowGap);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, p.RowY);
        Assert.Equal(7, p.Height);
        Assert.Equal(45, p.GhostX);
        Assert.Equal("", p.GhostLabel);
    }

    [Fact]
    public void Fast_forward_with_a_side_lane()
    {
        var p = Fit("feature/ff-to-merge", GhostKind.FastForward, 67, 14);

        Assert.Equal(5, p.Step);
        Assert.Equal(2, p.RowGap);
        Assert.Equal(0, p.HashWidth);
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12 }, p.RowY);
        Assert.Equal("main moves here", p.GhostLabel);
        Assert.Equal(new[] { 0, 2, 3 }, p.OnRows);
    }

    [Fact]
    public void The_layout_does_not_move_with_the_selection()
    {
        var wide = Fit("feature/merge-commit", GhostKind.Merge, 86, 20);
        var narrow = Fit("feature/merge-commit", GhostKind.Merge, 47, 8);
        var kinds = new[] { GhostKind.None, GhostKind.Merge, GhostKind.FastForward, GhostKind.Conflict };

        foreach (var branch in SandboxLaneGraph.Branches)
        {
            foreach (var kind in kinds)
            {
                var atWide = Fit(branch.Name, kind, 86, 20);
                var atNarrow = Fit(branch.Name, kind, 47, 8);

                Assert.Equal(wide.LabelWidth, atWide.LabelWidth);
                Assert.Equal(wide.GraphX, atWide.GraphX);
                Assert.Equal(wide.Step, atWide.Step);
                Assert.Equal(wide.FirstColumn, atWide.FirstColumn);
                Assert.Equal(wide.RowGap, atWide.RowGap);
                Assert.Equal(wide.RowY, atWide.RowY);

                Assert.Equal(narrow.LabelWidth, atNarrow.LabelWidth);
                Assert.Equal(narrow.GraphX, atNarrow.GraphX);
                Assert.Equal(narrow.Step, atNarrow.Step);
                Assert.Equal(narrow.FirstColumn, atNarrow.FirstColumn);
                Assert.Equal(narrow.RowGap, atNarrow.RowGap);
                Assert.Equal(narrow.RowY, atNarrow.RowY);
            }
        }
    }

    [Fact]
    public void Selecting_the_base_lights_only_the_base()
    {
        var p = Fit("main", GhostKind.Merge, 86, 20);

        Assert.Equal(GhostKind.None, p.Ghost);
        Assert.Equal(new[] { 0 }, p.OnRows);
        Assert.Equal(-1, p.SelectedLane);
        Assert.Equal(-1, p.TipLane);
    }

    [Fact]
    public void A_narrow_pane_cuts_the_oldest_columns()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merge, 20, 8);

        Assert.Equal(6, p.LabelWidth);
        Assert.Equal(9, p.GraphX);
        Assert.Equal(1, p.FirstColumn);
        Assert.Equal(2, p.Step);
        Assert.Equal(19, p.GhostX);
    }

    [Fact]
    public void Pane_of_a_64_column_screen()
    {
        var p = Fit("feature/conflict", GhostKind.Conflict, 31, 8);

        Assert.Equal(10, p.LabelWidth);
        Assert.Equal(13, p.GraphX);
        Assert.Equal(2, p.Step);
        Assert.Equal(0, p.FirstColumn);
        Assert.Equal(25, p.GhostX);
    }

    [Fact]
    public void A_short_pane_keeps_the_base_and_the_selected_lane()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merge, 86, 4);

        Assert.Equal(1, p.RowGap);
        Assert.Equal(new[] { 0, 1, 5 }, p.Rows);
        Assert.Equal(new[] { 0, 1, 2 }, p.RowY);
        Assert.Equal(3, p.More);
        Assert.True(p.ShowMore);
        Assert.Equal(4, p.Height);
        Assert.Equal(0, p.HashWidth);
    }

    [Fact]
    public void A_tiny_pane_keeps_the_tip_lane_before_the_rest()
    {
        var p = Fit("feature/ff-to-merge", GhostKind.Merge, 86, 3);

        Assert.Equal(new[] { 0, 2 }, p.Rows);
        Assert.Equal(4, p.More);
        Assert.True(p.ShowMore);
        Assert.Equal(3, p.Height);
    }

    [Fact]
    public void The_tip_lane_can_be_dropped()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merge, 86, 2);

        Assert.Equal(new[] { 0 }, p.Rows);
        Assert.Equal(5, p.More);
        Assert.True(p.ShowMore);
        Assert.Equal(2, p.Height);
        Assert.Equal(5, p.TipLane);
        Assert.False(p.IsKept(5));
    }

    [Fact]
    public void One_row_has_no_more_line()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merge, 86, 1);

        Assert.Equal(new[] { 0 }, p.Rows);
        Assert.False(p.ShowMore);
        Assert.Equal(1, p.Height);
    }

    [Fact]
    public void Left_out_lanes_reserve_the_more_line()
    {
        var leftOut = G with { LeftOut = 2 };

        var p = LaneFit.Fit(leftOut, "feature/merge-commit", GhostKind.Merge, 86, 20);

        Assert.Equal(2, p.RowGap);
        Assert.Equal(7, p.Rows.Count);
        Assert.Equal(2, p.More);
        Assert.True(p.ShowMore);
        Assert.Equal(15, p.Height);
    }

    [Fact]
    public void A_pointer_lights_the_lane_it_points_into()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), [C("b1", "a3")], Br("alpha", "b1"), Br("beta", "b1"));

        var p = LaneFit.Fit(graph, "beta", GhostKind.Merge, 60, 10);

        Assert.Equal(-1, p.SelectedLane);
        Assert.Equal(1, p.TipLane);
        Assert.Equal(new[] { 0, 1 }, p.OnRows);
    }

    [Fact]
    public void A_pointer_on_the_base()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), [], Br("same", "a3"), Br("old", "a2"));

        var p = LaneFit.Fit(graph, "same", GhostKind.Merge, 60, 10);

        Assert.Equal(0, p.TipLane);
        Assert.Equal(new[] { 0 }, p.OnRows);
    }

    [Fact]
    public void An_unknown_branch()
    {
        var p = Fit("nope", GhostKind.Merge, 86, 20);

        Assert.Equal(-1, p.TipLane);
        Assert.Equal(new[] { 0 }, p.OnRows);
        Assert.Equal(GhostKind.Merge, p.Ghost);
    }

    [Fact]
    public void One_commit()
    {
        var graph = Graph(Chain("a1"), []);

        var p = LaneFit.Fit(graph, "main", GhostKind.None, 40, 5);

        Assert.Equal(4, p.LabelWidth);
        Assert.Equal(7, p.GraphX);
        Assert.Equal(14, p.Step);
        Assert.Equal(7, p.HashWidth);
        Assert.Equal(new[] { 1 }, p.RowY);
        Assert.Equal(2, p.Height);
    }

    // main, a (on a2), b stacked on a, and three more branches on a2: six lanes, so a short pane has to drop some.
    private static LaneGraph StackedGraph() => Graph(
        Chain("a3", "a2", "a1"),
        [C("x1", "a2"), C("y1", "x1"), C("z1", "a2"), C("w1", "a2"), C("v1", "a2")],
        Br("a", "x1"), Br("b", "y1"), Br("c", "z1"), Br("d", "w1"), Br("e", "v1"));

    // main, a, b stacked on a, c stacked on b: the deep stack for the case where a middle row is past the limit.
    private static LaneGraph DeepStackGraph() => Graph(
        Chain("a3", "a2", "a1"),
        [C("x1", "a2"), C("y1", "x1"), C("w1", "y1"), C("z1", "a2"), C("v1", "a2")],
        Br("a", "x1"), Br("b", "y1"), Br("c", "w1"), Br("d", "z1"), Br("e", "v1"));

    [Fact]
    public void A_stacked_branch_brings_its_parent_row_when_both_fit_under_the_limit()
    {
        var graph = StackedGraph();

        // Height 4 gives a limit of 3: the branch and its parent fit with the base.
        var p = LaneFit.Fit(graph, "b", GhostKind.Merge, 86, 4);

        Assert.True(p.IsKept(graph.LaneOf("a")));
        Assert.True(p.IsKept(graph.LaneOf("b")));
        Assert.Equal(3, p.Rows.Count);
        Assert.Equal(3, p.More);
        Assert.True(p.ShowMore);
        Assert.Equal(4, p.Height);
    }

    [Fact]
    public void A_stacked_branch_is_kept_without_its_parent_when_there_is_no_room()
    {
        var graph = StackedGraph();

        // Height 3 gives a limit of 2: the parent does not fit, so the branch is kept anyway (within the height) and its parent is not.
        var p = LaneFit.Fit(graph, "b", GhostKind.Merge, 86, 3);

        Assert.True(p.IsKept(graph.LaneOf("b")));
        Assert.False(p.IsKept(graph.LaneOf("a")));
        Assert.Equal(2, p.Rows.Count);
        Assert.Equal(4, p.More);
        Assert.True(p.ShowMore);
        Assert.Equal(3, p.Height);
    }

    [Fact]
    public void A_deep_stack_past_the_limit_keeps_the_selected_lane_and_drops_the_middle_row()
    {
        var graph = DeepStackGraph();

        // Height 4 gives a limit of 3. The selected lane c needs b and a as well: no room, so c is kept anyway. Then a fills the
        // last row under the limit and b, past it, is dropped. c is drawn with a lead-in from the left (2b).
        var p = LaneFit.Fit(graph, "c", GhostKind.Merge, 86, 4);

        Assert.True(p.IsKept(graph.LaneOf("c")));
        Assert.True(p.IsKept(graph.LaneOf("a")));
        Assert.False(p.IsKept(graph.LaneOf("b")));
        Assert.Equal(3, p.Rows.Count);
        Assert.True(p.Height <= 4);
    }

    [Theory]
    [InlineData("feature/conflict", 14, "conflict")]
    [InlineData("main", 14, "main")]
    [InlineData("a/b/very-long-branch-name", 10, "very-long…")]
    [InlineData("trailing/", 14, "trailing/")]
    [InlineData("", 14, "")]
    public void Label_is_the_last_path_part_cut_to_width(string name, int width, string expected)
    {
        Assert.Equal(expected, LaneFit.Label(name, width));
    }

    [Fact]
    public void Ghost_text_names_the_kind()
    {
        Assert.Equal("new merge commit", LaneFit.GhostText(GhostKind.Merge, "main"));
        Assert.Equal("trunk moves here", LaneFit.GhostText(GhostKind.FastForward, "trunk"));
        Assert.Equal("no commit", LaneFit.GhostText(GhostKind.Conflict, "main"));
        Assert.Equal("", LaneFit.GhostText(GhostKind.None, "main"));
    }

    [Theory]
    [InlineData(1, "+1 more branch")]
    [InlineData(3, "+3 more branches")]
    public void More_text_counts_the_branches(int count, string expected)
    {
        Assert.Equal(expected, LaneFit.MoreText(count));
    }

    [Fact]
    public void Merged_ghost_text_has_the_hash_cut_to_seven()
    {
        Assert.Equal("merge commit 1234567", LaneFit.GhostText(GhostKind.Merged, "main", "1234567890"));
        Assert.Equal("merge commit 1234567", LaneFit.GhostText(GhostKind.Merged, "main", "1234567"));
        Assert.Equal("merge commit", LaneFit.GhostText(GhostKind.Merged, "main", ""));
    }

    [Fact]
    public void Done_ghost_texts_are_the_fast_forward_no_text_and_the_stopped_words()
    {
        Assert.Equal("", LaneFit.GhostText(GhostKind.FastForwarded, "main"));
        Assert.Equal("stopped · main unchanged", LaneFit.GhostText(GhostKind.Stopped, "main"));
    }

    [Fact]
    public void Short_texts_drop_the_hash_and_the_base_name()
    {
        Assert.Equal("merge commit", LaneFit.GhostShortText(GhostKind.Merged, "main"));
        Assert.Equal("stopped", LaneFit.GhostShortText(GhostKind.Stopped, "main"));
        Assert.Equal("new merge commit", LaneFit.GhostShortText(GhostKind.Merge, "main"));
    }

    [Fact]
    public void The_merged_label_carries_the_hash_when_it_fits()
    {
        var p = LaneFit.Fit(G, "feature/merge-commit", GhostKind.Merged, 86, 20, "1234567");

        Assert.Equal("merge commit 1234567", p.GhostLabel);
    }

    // At 84 columns the full label no longer fits to the right of the ghost, so the label falls back to "merge commit" (and no hash).
    [Fact]
    public void The_merged_label_falls_back_to_the_short_words()
    {
        var p = LaneFit.Fit(G, "feature/merge-commit", GhostKind.Merged, 84, 20, "1234567");

        Assert.True(84 - (p.GhostX + 2) < "merge commit 1234567".Length);
        Assert.Equal("merge commit", p.GhostLabel);
    }

    // A done ghost with no room for its words has no label, as the preview ghosts have none.
    [Fact]
    public void A_done_ghost_with_no_room_has_no_label()
    {
        var p = Fit("feature/merge-commit", GhostKind.Merged, 47, 8);

        Assert.Equal("", p.GhostLabel);
    }

    // The reviewer's nit on 6a: a stop that the verify did not confirm says "stopped" only, never "main unchanged".
    [Fact]
    public void An_unverified_stop_says_stopped_and_nothing_more()
    {
        Assert.Equal("stopped", LaneFit.GhostFullText(GhostKind.Stopped, "main", "", verified: false));
        Assert.Equal("stopped · main unchanged", LaneFit.GhostFullText(GhostKind.Stopped, "main", "", verified: true));

        var p = LaneFit.Fit(G, "feature/conflict", GhostKind.Stopped, 140, 20, "", ghostVerified: false);

        Assert.Equal("stopped", p.GhostLabel);
    }

    [Fact]
    public void Only_a_stop_depends_on_the_verify()
    {
        Assert.Equal("merge commit 1234567", LaneFit.GhostFullText(GhostKind.Merged, "main", "1234567", verified: false));
        Assert.Equal("no commit", LaneFit.GhostFullText(GhostKind.Conflict, "main", "", verified: false));
    }
}
