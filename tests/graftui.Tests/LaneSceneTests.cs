using System.Text;
using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// Drawing a lane placement into a grid. G is the sandbox graph, built in memory. The goldens are the sandbox graph at four sizes,
/// copied from the plan's spec; each row is written out in full, trailing blanks cut.
/// </summary>
public sealed class LaneSceneTests
{
    private static readonly LaneGraph G = SandboxLaneGraph.Build();

    private static ArtGrid R(string selected, GhostKind kind, int width, int height) =>
        LaneScene.Render(LaneFit.Fit(G, selected, kind, width, height), Glyphs.Unicode);

    // The rows joined with newlines, so a golden can be written one row per line.
    private static string Rows(params string[] rows) => string.Join("\n", rows);

    // Each row's characters, trailing blanks cut, the rows joined with newlines.
    private static string Dump(ArtGrid grid)
    {
        var rows = new List<string>();
        for (var y = 0; y < grid.Height; y++)
        {
            var row = new StringBuilder();
            for (var x = 0; x < grid.Width; x++)
                row.Append(grid[x, y].Ch);
            rows.Add(row.ToString().TrimEnd());
        }

        return Rows(rows.ToArray());
    }

    // The ink of a branch lane in the sandbox graph, as the scene draws it.
    private static Ink L(int lane) => BranchInks.Of(G.Lanes[lane].ColorLane);

    private static int Count(ArtGrid grid, char ch)
    {
        var n = 0;
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (grid[x, y].Ch == ch)
                    n++;
            }
        }

        return n;
    }

    private static bool RowHas(ArtGrid grid, int y, char ch)
    {
        for (var x = 0; x < grid.Width; x++)
        {
            if (grid[x, y].Ch == ch)
                return true;
        }

        return false;
    }

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

    // A line of count commits: prefix1 sits on from, prefix2 on prefix1, and so on.
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

    private static LaneGraph Graph(IReadOnlyList<GraphCommit> baseChain, IReadOnlyList<GraphCommit> others, params Branch[] branches) =>
        LaneGraphBuilder.Build(new LaneGraphInput("main", baseChain, false, branches, others, false));

    // The sandbox graph at 86 × 20, with feature/merge-commit selected and its merge ghost.
    [Fact]
    public void Golden_wide()
    {
        var expected = Rows(
            "            34e4a2e 8c5ceee be0126e",
            " main          ●───────●───────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄◌ new merge commit",
            "                       │       │                               ┆",
            "                       │       │    8f0068a                    ┆",
            " fast-forward          │       ├───────●                       ┆",
            "                       │       │                               ┆",
            "                       │       │    2b99ab2         810cc55    ┆",
            " ff-to-merge           │       ╰───────●───────────────●       ┆",
            "                       │               │               │       ┆",
            "                       │               │    df42801    │       ┆",
            " side-for-ffm          │               ╰───────●───────╯       ┆",
            "                       │                                       ┆",
            "                       │    30cc9f2                            ┆",
            " conflict              ├───────●                               ┆",
            "                       │                                       ┆",
            "                       │    acf4dee                            ┆",
            " merge-commit          ├───────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯",
            "                       │",
            "                       │    089eae5 52b7690 f6d6964",
            " picks                 ╰───────●───────●───────●"
        );

        Assert.Equal(expected, Dump(R("feature/merge-commit", GhostKind.Merge, 86, 20)));
    }

    [Fact]
    public void Golden_80_columns()
    {
        var expected = Rows(
            " main          ●────●────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄◌",
            " fast-forward       │    ├────●              ┆",
            " ff-to-merge        │    ╰────●─────────●    ┆",
            " side-for-ffm       │         ╰────●────╯    ┆",
            " conflict           ├────●                   ┆",
            " merge-commit       ├────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯",
            " picks              ╰────●────●────●"
        );

        Assert.Equal(expected, Dump(R("feature/merge-commit", GhostKind.Merge, 47, 8)));
    }

    [Fact]
    public void Golden_fast_forward_with_the_side_lane()
    {
        var expected = Rows(
            " main          ●────●────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄⇢ main moves here",
            "                    │    │                   ┆",
            " fast-forward       │    ├────●              ┆",
            "                    │    │                   ┆",
            " ff-to-merge        │    ╰────●─────────●┄┄┄┄╯",
            "                    │         │         │",
            " side-for-ffm       │         ╰────●────╯",
            "                    │",
            " conflict           ├────●",
            "                    │",
            " merge-commit       ├────●",
            "                    │",
            " picks              ╰────●────●────●"
        );

        Assert.Equal(expected, Dump(R("feature/ff-to-merge", GhostKind.FastForward, 67, 14)));
    }

    [Fact]
    public void Golden_64_columns_with_a_conflict()
    {
        var expected = Rows(
            " main        ●─●─●┄┄┄┄┄┄┄✕",
            " fast-forw…    │ ├─●     ┆",
            " ff-to-mer…    │ ╰─●───● ┆",
            " side-for-…    │   ╰─●─╯ ┆",
            " conflict      ├─●┄┄┄┄┄┄┄╯",
            " merge-com…    ├─●",
            " picks         ╰─●─●─●"
        );

        Assert.Equal(expected, Dump(R("feature/conflict", GhostKind.Conflict, 31, 8)));
    }

    [Fact]
    public void Colours_on_the_wide_golden()
    {
        var g = R("feature/merge-commit", GhostKind.Merge, 86, 20);

        Assert.Equal(('m', L(0), CellBg.None), (g[1, 1].Ch, g[1, 1].Style.Ink, g[1, 1].Bg));
        Assert.Equal(('m', L(5), CellBg.Sel), (g[1, 16].Ch, g[1, 16].Style.Ink, g[1, 16].Bg));
        Assert.Equal(CellBg.Sel, g[0, 16].Bg);
        Assert.Equal(CellBg.Sel, g[12, 16].Bg);
        Assert.Equal(CellBg.None, g[13, 16].Bg);
        Assert.Equal(Ink.Dim, InkAt(g, 1, 19));
        Assert.Equal(Ink.Dim, InkAt(g, 1, 10));
        Assert.Equal(('●', L(0)), (g[15, 1].Ch, g[15, 1].Style.Ink));
        Assert.Equal(('●', L(5)), (g[31, 16].Ch, g[31, 16].Style.Ink));
        Assert.Equal(('●', Ink.Faint), (g[31, 19].Ch, g[31, 19].Style.Ink));
        Assert.Equal(('a', Ink.Hash), (g[28, 15].Ch, g[28, 15].Style.Ink));
        Assert.Equal(('0', Ink.Faint), (g[28, 18].Ch, g[28, 18].Style.Ink));
        Assert.Equal(('│', L(5)), (g[23, 2].Ch, g[23, 2].Style.Ink));
        Assert.Equal(('├', Ink.Faint), (g[23, 13].Ch, g[23, 13].Style.Ink));
        Assert.Equal(('├', L(5)), (g[23, 16].Ch, g[23, 16].Style.Ink));
        Assert.Equal(('╰', Ink.Faint), (g[23, 19].Ch, g[23, 19].Style.Ink));
        Assert.Equal(('┄', Ink.Mrg), (g[32, 1].Ch, g[32, 1].Style.Ink));
        Assert.Equal(('◌', Ink.Mrg), (g[63, 1].Ch, g[63, 1].Style.Ink));
        Assert.Equal(('n', Ink.Mrg), (g[65, 1].Ch, g[65, 1].Style.Ink));
        Assert.Equal(('┆', Ink.Mrg), (g[63, 8].Ch, g[63, 8].Style.Ink));
        Assert.Equal(('┄', Ink.Mrg), (g[40, 16].Ch, g[40, 16].Style.Ink));
        Assert.Equal(('╯', Ink.Mrg), (g[63, 16].Ch, g[63, 16].Style.Ink));
    }

    [Fact]
    public void Fast_forward_lights_the_side_lane()
    {
        var g = R("feature/ff-to-merge", GhostKind.FastForward, 67, 14);

        Assert.Equal(('f', L(2)), (g[1, 4].Ch, g[1, 4].Style.Ink));
        Assert.Equal(('s', Ink.Dim), (g[1, 6].Ch, g[1, 6].Style.Ink));
        Assert.Equal(('●', L(3)), (g[35, 6].Ch, g[35, 6].Style.Ink));
        Assert.Equal(('╯', L(3)), (g[40, 6].Ch, g[40, 6].Style.Ink));
        Assert.Equal(('⇢', Ink.Ok), (g[45, 0].Ch, g[45, 0].Style.Ink));
    }

    [Fact]
    public void Selecting_main_draws_no_ghost()
    {
        var p = LaneFit.Fit(G, "main", GhostKind.Merge, 86, 20);
        var g = LaneScene.Render(p, Glyphs.Unicode);

        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
            {
                var ink = g[x, y].Style.Ink;
                Assert.False(ink == Ink.Mrg || ink == Ink.Ok || ink == Ink.Bad, $"ghost ink at ({x},{y})");
                Assert.NotEqual(CellBg.Sel, g[x, y].Bg);
                if (x >= p.GhostX)
                    Assert.Equal(' ', g[x, y].Ch);
            }
        }
    }

    [Fact]
    public void Short_pane_says_how_many_are_left_out()
    {
        var g = R("feature/merge-commit", GhostKind.Merge, 86, 4);
        var rows = Dump(g).Split('\n');

        Assert.Equal(" +3 more branches", rows[3]);
        Assert.Equal(Ink.Dim, InkAt(g, 2, 3));
        Assert.Equal('╯', g[63, 2].Ch);
        Assert.Equal('┆', g[63, 1].Ch);
    }

    [Fact]
    public void Ghost_without_its_tip_lane()
    {
        var g = R("feature/merge-commit", GhostKind.Merge, 86, 2);
        var rows = Dump(g).Split('\n');

        Assert.Equal('◌', g[63, 0].Ch);
        Assert.Equal(0, Count(g, '╯'));
        Assert.Equal(0, Count(g, '┆'));
        Assert.Equal(" +5 more branches", rows[1]);
    }

    [Fact]
    public void Narrow_pane_leads_in_from_the_left()
    {
        var g = R("feature/merge-commit", GhostKind.Merge, 20, 8);

        Assert.Equal('┈', g[8, 0].Ch);
        Assert.Equal('◌', g[19, 0].Ch);
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < 9; x++)
                Assert.NotEqual('●', g[x, y].Ch);
        }
    }

    [Fact]
    public void Hidden_commits_show_a_gap_mark()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), Line("b", 11, "a3"), Br("feature", "b11"));
        var p = LaneFit.Fit(graph, "feature", GhostKind.None, 80, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var feature = graph.Lanes.Single(l => l.Name == "feature");

        Assert.Equal(3, feature.Hidden);
        Assert.Equal('┈', g[p.XOf(feature.Gap), p.YOf(1)].Ch);
        Assert.Equal('─', g[p.XOf(feature.Gap) + 1, p.YOf(1)].Ch);
    }

    [Fact]
    public void A_hidden_lane_whose_fork_is_not_drawn_runs_from_its_gap_mark_to_the_dot()
    {
        // child is stacked on parent, and has nine commits, so one is hidden. At 86 × 2 parent's row is dropped and child is kept.
        var graph = Graph(Chain("a3", "a2", "a1"), [C("p1", "a3"), .. Line("c", 9, "p1")], Br("parent", "p1"), Br("child", "c9"));
        var p = LaneFit.Fit(graph, "child", GhostKind.None, 86, 2);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var child = graph.Lanes.Single(l => l.Name == "child");
        var y = p.YOf(child.Index);
        var gapX = p.XOf(child.Gap);
        var dotX = p.XOf(child.Commits[0].Column);

        Assert.True(child.Hidden > 0);
        Assert.False(p.IsKept(graph.Lanes.Single(l => l.Name == "parent").Index));
        Assert.Equal('┈', g[gapX, y].Ch);
        Assert.Equal(1, Enumerable.Range(0, g.Width).Count(x => g[x, y].Ch == '┈'));
        for (var x = gapX + 1; x < dotX; x++)
            Assert.NotEqual(' ', g[x, y].Ch);
        Assert.Equal('●', g[dotX, y].Ch);
    }

    [Fact]
    public void A_fork_from_older_history_has_no_corner()
    {
        var graph = LaneGraphBuilder.Build(new LaneGraphInput("main", Chain(Ids(12)), true, [Br("feature", "b1")], [C("b1", "z0")], false));
        var p = LaneFit.Fit(graph, "feature", GhostKind.None, 80, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var feature = graph.Lanes.Single(l => l.Name == "feature");

        Assert.False(RowHas(g, p.YOf(1), '├'));
        Assert.False(RowHas(g, p.YOf(1), '╰'));
        Assert.Equal('┈', g[p.XOf(feature.Gap), p.YOf(1)].Ch);
        Assert.Equal('┈', g[p.GraphX - 1, p.YOf(0)].Ch);
    }

    [Fact]
    public void Merging_the_base_into_a_branch_turns_down()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), [C("b1", "a1"), Commit("m", "Merge main into feature", "b1", "a3")], Br("feature", "m"));
        var p = LaneFit.Fit(graph, "feature", GhostKind.None, 60, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var merge = graph.Links.Single(l => l.Kind == LinkKind.Merge);
        var x = p.XOf(merge.ToColumn);

        Assert.Equal('╮', g[x, p.YOf(0)].Ch);
        for (var y = p.YOf(0) + 1; y < p.YOf(1); y++)
            Assert.Equal('│', g[x, y].Ch);
    }

    [Fact]
    public void A_branch_merged_into_another()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), [C("x1", "a3"), C("y1", "a3"), Commit("ym", "Merge x into y", "y1", "x1")],
            Br("x", "x1"), Br("y", "ym"));
        var p = LaneFit.Fit(graph, "y", GhostKind.None, 60, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var y = graph.Lanes.Single(l => l.Name == "y");

        Assert.Equal('╮', g[p.XOf(y.Commits[y.Commits.Count - 1].Column), p.YOf(1)].Ch);
    }

    // The base has six commits, so its tip a6 sits at column 5: a branch that merges a6 in runs on to column 5 with no commit
    // in between. The columns are then: x1 at 3, ym at 4 (its parents are y1 and x1), and x2 at 6.
    [Fact]
    public void A_merge_into_a_line_that_runs_on_past_it_is_a_tee_down()
    {
        var graph = Graph(Chain("a6", "a5", "a4", "a3", "a2", "a1"),
            [C("x1", "a3"), C("x2", "x1", "a6"), C("y1", "a3"), Commit("ym", "Merge x into y", "y1", "x1")],
            Br("x", "x2"), Br("y", "ym"));
        var p = LaneFit.Fit(graph, "y", GhostKind.None, 60, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var x = graph.Lanes.Single(l => l.Name == "x");
        var y = graph.Lanes.Single(l => l.Name == "y");
        var merge = graph.Links.Single(l => l.Kind == LinkKind.Merge && l.ToLane == y.Index);

        Assert.True(x.Commits[x.Commits.Count - 1].Column > merge.ToColumn);
        Assert.Equal('┬', g[p.XOf(merge.ToColumn), p.YOf(x.Index)].Ch);
    }

    // The mirror image: the merged branch is the lower row, and its line runs on past the merge commit xm.
    [Fact]
    public void A_merge_from_a_line_that_runs_on_past_it_is_a_tee_up()
    {
        var graph = Graph(Chain("a6", "a5", "a4", "a3", "a2", "a1"),
            [C("x1", "a3"), C("y1", "a3"), Commit("xm", "Merge y into x", "x1", "y1"), C("y2", "y1", "a6")],
            Br("x", "xm"), Br("y", "y2"));
        var p = LaneFit.Fit(graph, "x", GhostKind.None, 60, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var x = graph.Lanes.Single(l => l.Name == "x");
        var y = graph.Lanes.Single(l => l.Name == "y");
        var merge = graph.Links.Single(l => l.Kind == LinkKind.Merge && l.ToLane == x.Index);

        Assert.True(y.Commits[y.Commits.Count - 1].Column > merge.ToColumn);
        Assert.Equal('┴', g[p.XOf(merge.ToColumn), p.YOf(y.Index)].Ch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_octopus_merge_corner_keeps_its_colour(bool reverse)
    {
        var graph = Graph([C("m", "a2", "y1", "x1"), C("a2", "a1"), C("a1")],
            [C("x1", "a1"), C("y1", "a1")], Br("x", "x1"), Br("y", "y1"));
        if (reverse)
            graph = graph with { Links = graph.Links.Reverse().ToArray() };
        var p = LaneFit.Fit(graph, "x", GhostKind.None, 80, 20);
        var lane = graph.LaneOf("x");
        var merge = graph.Links.Single(l => l.Kind == LinkKind.Merge && l.FromLane == lane);
        var grid = LaneScene.Render(p, Glyphs.Unicode);

        Assert.Equal(('╯', BranchInks.Of(graph.Lanes[lane].ColorLane)),
            (grid[p.XOf(merge.ToColumn), p.YOf(lane)].Ch, grid[p.XOf(merge.ToColumn), p.YOf(lane)].Style.Ink));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Coincident_merges_keep_both_vertical_connections(bool reverse, bool basic)
    {
        var graph = Graph([C("m", "a2", "x1"), C("a2", "a1"), C("a1")],
            [C("x1", "a1"), C("y1", "a1"), C("ym", "y1", "x1")], Br("x", "x1"), Br("y", "ym"));
        if (reverse)
            graph = graph with { Links = graph.Links.Reverse().ToArray() };
        var lane = graph.LaneOf("x");
        var merges = graph.Links.Where(l => l.Kind == LinkKind.Merge && l.FromLane == lane).ToArray();
        Assert.Equal(2, merges.Length);
        Assert.Equal(merges[0].ToColumn, merges[1].ToColumn);
        var p = LaneFit.Fit(graph, "x", GhostKind.None, 80, 20);
        var grid = LaneScene.Render(p, basic ? Glyphs.Basic : Glyphs.Unicode);
        var x = p.XOf(merges[0].ToColumn);
        var y = p.YOf(lane);

        Assert.Equal(basic ? '+' : '┤', grid[x, y].Ch);
        Assert.Equal(basic ? '|' : '│', grid[x, y - 1].Ch);
        Assert.Equal(basic ? '|' : '│', grid[x, y + 1].Ch);
        Assert.Equal(BranchInks.Of(graph.Lanes[lane].ColorLane), grid[x, y].Style.Ink);
    }

    [Fact]
    public void An_unrelated_lane_has_no_connector()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), [C("b1", "a3"), C("r2", "r1"), C("r1")], Br("feature", "b1"), Br("r", "r2"));
        var p = LaneFit.Fit(graph, "feature", GhostKind.None, 80, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var unrelated = graph.Lanes.Single(l => l.Name == "r");
        var y = p.YOf(unrelated.Index);
        var first = p.XOf(unrelated.Commits[0].Column);

        Assert.False(RowHas(g, y, '├'));
        Assert.False(RowHas(g, y, '╰'));
        Assert.Equal(' ', g[first - 1, y].Ch);
    }

    [Fact]
    public void A_kept_lane_whose_parent_row_was_dropped_comes_in_from_the_left()
    {
        // child is stacked on parent. At 40 × 2 only one lane besides the base fits, so child is kept and parent is not.
        var graph = Graph(Chain("a3", "a2", "a1"), [C("p1", "a3"), C("c1", "p1")], Br("parent", "p1"), Br("child", "c1"));
        var p = LaneFit.Fit(graph, "child", GhostKind.None, 40, 2);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var child = graph.Lanes.Single(l => l.Name == "child");
        var parent = graph.Lanes.Single(l => l.Name == "parent");
        var y = p.YOf(child.Index);
        var dot = p.XOf(child.Commits[0].Column);

        Assert.True(p.IsKept(child.Index));
        Assert.False(p.IsKept(parent.Index));
        Assert.Equal('┈', g[dot - 1, y].Ch);
        Assert.Equal('●', g[dot, y].Ch);
        Assert.False(RowHas(g, y, '├'));
        Assert.False(RowHas(g, y, '╰'));
    }

    [Fact]
    public void One_commit_has_one_row_and_its_hash_above()
    {
        var graph = Graph(Chain("a1"), []);
        var g = LaneScene.Render(LaneFit.Fit(graph, "main", GhostKind.None, 40, 5), Glyphs.Unicode);

        Assert.Equal("    a1\n main  ●", Dump(g));
    }

    [Fact]
    public void A_pointer_lights_the_lane_it_points_into_without_a_label_background()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), [C("b1", "a3")], Br("alpha", "b1"), Br("beta", "b1"));
        var p = LaneFit.Fit(graph, "beta", GhostKind.Merge, 60, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);

        Assert.Equal('◌', g[p.GhostX, p.YOf(0)].Ch);
        Assert.Equal('╯', g[p.GhostX, p.YOf(1)].Ch);
        Assert.Equal('┆', g[p.GhostX, p.YOf(0) + 1].Ch);
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
                Assert.NotEqual(CellBg.Sel, g[x, y].Bg);
        }
    }

    [Fact]
    public void Basic_glyphs_replace_the_round_and_dashed_ones()
    {
        var g = LaneScene.Render(LaneFit.Fit(G, "feature/merge-commit", GhostKind.Merge, 86, 20), Glyphs.Basic);
        var text = Dump(g);

        foreach (var ch in "●◌◍◉┄┆┈⇢✕⑂")
            Assert.DoesNotContain(ch, text);
        Assert.Equal('*', g[31, 16].Ch);
        Assert.Equal('o', g[63, 1].Ch);
        Assert.Equal('-', g[40, 1].Ch);
        Assert.Equal(':', g[63, 8].Ch);
    }

    [Theory]
    [InlineData(11, 20)]
    [InlineData(80, 0)]
    public void An_empty_placement_renders_nothing(int width, int height)
    {
        var placement = LaneFit.Fit(G, "feature/merge-commit", GhostKind.Merge, width, height);
        var g = LaneScene.Render(placement, Glyphs.Unicode);

        Assert.Equal(0, g.Height);
    }

    [Fact]
    public void An_empty_graph_renders_nothing()
    {
        var g = LaneScene.Render(LaneFit.Fit(LaneGraph.Empty, "", GhostKind.None, 80, 10), Glyphs.Unicode);

        Assert.Equal(0, g.Height);
    }

    // An unrelated branch whose hidden commits are in the window runs from its gap mark to its first dot, with no fork.
    [Fact]
    public void An_unrelated_lane_with_hidden_commits_runs_from_its_gap()
    {
        var graph = Graph(Chain("a3", "a2", "a1"), Chain(Ids(11, "r")), Br("lone", "r11"));
        var p = LaneFit.Fit(graph, "lone", GhostKind.None, 86, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);
        var lone = graph.Lanes.Single(l => l.Name == "lone");
        var y = p.YOf(lone.Index);
        var gapX = p.XOf(lone.Gap);
        var dotX = p.XOf(lone.Commits[0].Column);

        Assert.True(lone.Hidden > 0);
        Assert.Equal(-1, lone.Parent);
        Assert.Equal('┈', g[gapX, y].Ch);
        for (var x = gapX + 1; x < dotX; x++)
            Assert.Equal('─', g[x, y].Ch);
        Assert.Equal('●', g[dotX, y].Ch);
    }

    // The base with older history (BaseMore): its lead-in mark sits just left of its first dot, and the line runs to that dot.
    [Fact]
    public void The_base_lead_in_touches_its_first_dot()
    {
        var graph = LaneGraphBuilder.Build(new LaneGraphInput("main", Chain(Ids(12)), true, [Br("feature", "b1")], [C("b1", "z0")], false));
        var p = LaneFit.Fit(graph, "feature", GhostKind.None, 80, 10);
        var g = LaneScene.Render(p, Glyphs.Unicode);

        Assert.True(graph.BaseMore);
        Assert.Equal('┈', g[p.GraphX - 1, p.YOf(0)].Ch);
        Assert.Equal('●', g[p.GraphX, p.YOf(0)].Ch);
    }

    // Spaces and box-drawing runs, so a golden row can say how wide a gap is.
    private static string Sp(int n) => new string(' ', n);

    private static string Dash(int n) => new string('─', n);

    // The done merge at 86 × 20: Golden_wide with feature/merge-commit's merge drawn in. The dashed trail is solid, the merge commit is a
    // solid dot with its hash above and the base's chip below, and the tip's line comes up into it.
    [Fact]
    public void Golden_merged_wide()
    {
        var expected = Rows(
            "            34e4a2e 8c5ceee be0126e" + Sp(25) + "1234567",
            " main          ●───────●───────●" + Dash(31) + "● merge commit 1234567",
            Sp(23) + "│" + Sp(7) + "│" + Sp(31) + "│  main",
            Sp(23) + "│" + Sp(7) + "│    8f0068a" + Sp(20) + "│",
            " fast-forward          │       ├───────●" + Sp(23) + "│",
            Sp(23) + "│" + Sp(7) + "│" + Sp(31) + "│",
            Sp(23) + "│" + Sp(7) + "│    2b99ab2         810cc55    │",
            " ff-to-merge           │       ╰───────●───────────────●       │",
            Sp(23) + "│" + Sp(15) + "│" + Sp(15) + "│" + Sp(7) + "│",
            Sp(23) + "│" + Sp(15) + "│    df42801    │" + Sp(7) + "│",
            " side-for-ffm          │               ╰───────●───────╯       │",
            Sp(23) + "│" + Sp(39) + "│",
            Sp(23) + "│    30cc9f2" + Sp(28) + "│",
            " conflict              ├───────●" + Sp(31) + "│",
            Sp(23) + "│" + Sp(39) + "│",
            Sp(23) + "│    acf4dee" + Sp(28) + "│",
            " merge-commit          ├───────●" + Dash(31) + "╯",
            Sp(23) + "│",
            Sp(23) + "│    089eae5 52b7690 f6d6964",
            " picks                 ╰───────●───────●───────●"
        );

        var p = LaneFit.Fit(G, "feature/merge-commit", GhostKind.Merged, 86, 20, "1234567");
        Assert.Equal(expected, Dump(LaneScene.Render(p, Glyphs.Unicode, "1234567")));
    }

    // The fast-forward at 86 × 20: feature/fast-forward's lane is drawn on the base row, with the base's name as a chip after its tip.
    // No dashes, no ghost glyph, no label; the hash of the lifted lane sits above the base row.
    [Fact]
    public void Golden_fast_forwarded_wide()
    {
        var expected = Rows(
            "            34e4a2e 8c5ceee be0126e 8f0068a",
            " main          ●───────●───────●───────●  main",
            Sp(23) + "│" + Sp(7) + "│",
            Sp(23) + "│" + Sp(7) + "│",
            " fast-forward          │       │",
            Sp(23) + "│" + Sp(7) + "│",
            Sp(23) + "│" + Sp(7) + "│    2b99ab2         810cc55",
            " ff-to-merge           │       ╰───────●───────────────●",
            Sp(23) + "│" + Sp(15) + "│" + Sp(15) + "│",
            Sp(23) + "│" + Sp(15) + "│    df42801    │",
            " side-for-ffm          │               ╰───────●───────╯",
            Sp(23) + "│",
            Sp(23) + "│    30cc9f2",
            " conflict              ├───────●",
            Sp(23) + "│",
            Sp(23) + "│    acf4dee",
            " merge-commit          ├───────●",
            Sp(23) + "│",
            Sp(23) + "│    089eae5 52b7690 f6d6964",
            " picks                 ╰───────●───────●───────●"
        );

        Assert.Equal(expected, Dump(R("feature/fast-forward", GhostKind.FastForwarded, 86, 20)));
    }

    // A conflict that stopped: the same drawing as the conflict's preview, with "stopped" where the preview says "no commit".
    [Fact]
    public void Stopped_wide_is_the_conflict_with_stopped_for_no_commit()
    {
        var preview = Dump(R("feature/conflict", GhostKind.Conflict, 86, 20));

        Assert.Contains("no commit", preview);
        Assert.Equal(preview.Replace("no commit", "stopped"), Dump(R("feature/conflict", GhostKind.Stopped, 86, 20)));
    }

    // The full words fit to the right of the ghost when the pane is wide enough: 24 columns after the ghost and its gap.
    [Fact]
    public void Stopped_label_is_in_full_when_it_fits()
    {
        var p = LaneFit.Fit(G, "feature/conflict", GhostKind.Stopped, 140, 20);

        Assert.True(140 - (p.GhostX + 2) >= 24);
        Assert.Equal("stopped · main unchanged", p.GhostLabel);
    }

    // The done merge in an 80-column pane: no hash row, no chip, the merge commit's solid dot at the ghost, and no dashes or dotted trail.
    [Fact]
    public void Merged_at_80_columns_has_no_hash_row_or_chip()
    {
        var p = LaneFit.Fit(G, "feature/merge-commit", GhostKind.Merged, 47, 7, "1234567");
        var g = LaneScene.Render(p, Glyphs.Unicode, "1234567");

        Assert.Equal(0, p.HashWidth);
        Assert.Equal('●', g[p.GhostX, p.YOf(0)].Ch);
        Assert.Equal(0, Count(g, '┄') + Count(g, '┆') + Count(g, '◌'));
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
                Assert.NotEqual(CellBg.Chip, g[x, y].Bg);
        }
    }

    // A done state never moves the graph: the rows, the spacing and the columns are those of its preview at each size.
    [Fact]
    public void Done_ghosts_do_not_move_the_graph()
    {
        var pairs = new[]
        {
            (GhostKind.Merge, GhostKind.Merged, "feature/merge-commit"),
            (GhostKind.Conflict, GhostKind.Stopped, "feature/conflict"),
            (GhostKind.FastForward, GhostKind.FastForwarded, "feature/fast-forward"),
        };
        var sizes = new[] { (86, 20), (47, 7), (37, 14) };

        foreach (var pair in pairs)
        {
            foreach (var size in sizes)
            {
                var preview = LaneFit.Fit(G, pair.Item3, pair.Item1, size.Item1, size.Item2);
                var done = LaneFit.Fit(G, pair.Item3, pair.Item2, size.Item1, size.Item2, "1234567");

                Assert.Equal(preview.RowY, done.RowY);
                Assert.Equal(preview.Step, done.Step);
                Assert.Equal(preview.GraphX, done.GraphX);
                Assert.Equal(preview.GhostX, done.GhostX);
            }
        }
    }

    // A pointer branch has no lane of its own, so a fast-forward lifts nothing: no lane on the base row, no chip.
    [Fact]
    public void A_pointer_branch_is_not_lifted()
    {
        var graph = Graph(Chain("m3", "m2", "m1"), [C("f1", "m3")], Br("feature", "f1"), Br("ptr", "m2"));
        var p = LaneFit.Fit(graph, "ptr", GhostKind.FastForwarded, 86, 20);
        var g = LaneScene.Render(p, Glyphs.Unicode);

        Assert.Equal(-1, p.LiftedLane);
        Assert.Equal(p.YOf(graph.LaneOf("feature")), p.LineYOf(graph.LaneOf("feature")));
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
                Assert.NotEqual(CellBg.Chip, g[x, y].Bg);
        }
    }

    // feature/ff-to-merge is lifted with its side lane: its own dots sit on the base row, and side-for-ffm still merges into it.
    [Fact]
    public void Ff_to_merge_lifts_with_its_side_lane()
    {
        var p = LaneFit.Fit(G, "feature/ff-to-merge", GhostKind.FastForwarded, 86, 20);
        var g = LaneScene.Render(p, Glyphs.Unicode);

        Assert.Equal(2, p.LiftedLane);
        Assert.Equal(p.YOf(0), p.LineYOf(2));
        Assert.Equal('●', g[p.XOf(3), p.YOf(0)].Ch);
        Assert.Equal('●', g[p.XOf(5), p.YOf(0)].Ch);
        Assert.Equal('╯', g[p.XOf(5), p.YOf(3)].Ch);
    }

    // g's fast-forward: feature/fast-forward's lane drawn at row 3 while its own row is 4. Its line, dot and hash move with it, the
    // label stays on its row, and nothing is dashed (there is no ghost).
    [Fact]
    public void A_shifted_lane_is_drawn_at_its_shift_y()
    {
        var lane = G.LaneOf("feature/fast-forward");
        var p = LaneFit.Fit(G, "feature/fast-forward", GhostKind.None, 86, 20) with { ShiftLane = lane, ShiftY = 3 };
        var dump = Dump(LaneScene.Render(p, Glyphs.Unicode));
        var rows = dump.Split('\n');

        Assert.Equal(Sp(23) + "│" + Sp(7) + "├───────●", rows[3]);
        Assert.Equal("8f0068a", rows[2].Substring(36, 7));
        Assert.Equal(" fast-forward          │       │", rows[4]);
        Assert.DoesNotContain("┄", dump);
        Assert.DoesNotContain("⇢", dump);
        Assert.Equal(L(lane), InkAt(LaneScene.Render(p, Glyphs.Unicode), 39, 3));
    }

    // Right under the base's row (ShiftY 2), the lane's hashes would land on the base row's line, so they are left out.
    [Fact]
    public void A_shifted_lane_next_to_the_base_hides_its_hashes()
    {
        var lane = G.LaneOf("feature/fast-forward");
        var p = LaneFit.Fit(G, "feature/fast-forward", GhostKind.None, 86, 20) with { ShiftLane = lane, ShiftY = 2 };

        Assert.DoesNotContain("8f0068a", Dump(LaneScene.Render(p, Glyphs.Unicode)));
    }

    private static Ink InkAt(ArtGrid grid, int x, int y) => grid[x, y].Style.Ink;
}
