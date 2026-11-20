using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Tests;

/// <summary>Fitting a lane graph into a grid. G is the sandbox graph, built in memory.</summary>
public sealed class LaneFitTests
{
    private static readonly LaneGraph G = SandboxLaneGraph.Build();

    private static readonly string[] SandboxRows =
    [
        "main: 34e4a2e@0 8c5ceee@1 be0126e@2",
        "feature/fast-forward: 8f0068a@3",
        "feature/ff-to-merge: 2b99ab2@3 810cc55@5",
        "side-for-ffm: df42801@4",
        "feature/conflict: 30cc9f2@2",
        "feature/merge-commit: acf4dee@2",
        "feature/picks: 089eae5@2 52b7690@3 f6d6964@4",
    ];

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
        Assert.Equal(SandboxRows, G.Lanes.Select(lane => Describe(lane)).ToArray());
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
}
