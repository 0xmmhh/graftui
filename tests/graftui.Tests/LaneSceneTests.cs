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

    private static Ink InkAt(ArtGrid grid, int x, int y) => grid[x, y].Style.Ink;
}
