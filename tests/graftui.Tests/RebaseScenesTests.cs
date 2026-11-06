using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class RebaseScenesTests
{
    // Three commits of the current branch, one of the onto branch, at 62 x 9 (step 6, row A at 2, row B at 6, the shared base at 16).
    private static readonly ArtGeometry G = ArtGeometry.For(62, 9)!;

    private static readonly CommitInfo C1 = Layouts.C("aaaaaaa1", subject: "one");
    private static readonly CommitInfo C2 = Layouts.C("bbbbbbb2", subject: "two");
    private static readonly CommitInfo C3 = Layouts.C("ccccccc3", subject: "three");

    private static readonly GraphLayout L = Layouts.Diverged() with
    {
        Current = "feature/picks",
        Selected = "main",
        CurrentOwn = [C1, C2, C3],
        SelectedOwn = [Layouts.C("ddddddd4", subject: "main's")],
        CurrentLane = 1,
        SelectedLane = 0,
    };

    private static RebaseRow Row(CommitInfo c, RebaseAction action = RebaseAction.Pick) =>
        new(c.Hash, c.ShortHash, c.Subject, action);

    private static string RowText(ArtGrid grid, int y)
    {
        var chars = new char[grid.Width];
        for (var x = 0; x < grid.Width; x++)
            chars[x] = grid[x, y].Ch;
        return new string(chars);
    }

    private static int CountOn(ArtGrid grid, int y, char ch) => RowText(grid, y).Count(c => c == ch);

    [Fact]
    public void Each_own_commit_of_the_current_branch_is_a_dot_on_row_A()
    {
        var rows = new[] { Row(C1), Row(C2), Row(C3) };

        var grid = RebaseScenes.Preview(G, L, rows, -1).Grid;

        // The shared commit at the base, then the three own commits.
        Assert.Equal(4, RowText(grid, G.A).Count(c => c == '●'));
        Assert.Equal('●', grid[G.TipX(1), G.A].Ch);
    }

    [Fact]
    public void A_drop_is_drawn_as_a_cross_and_a_fold_as_a_merge_dot()
    {
        var rows = new[] { Row(C1, RebaseAction.Drop), Row(C2, RebaseAction.Squash), Row(C3) };

        var grid = RebaseScenes.Preview(G, L, rows, -1).Grid;

        Assert.Equal('✕', grid[G.TipX(1), G.A].Ch);
        Assert.Equal(new Style(Ink.Bad), grid[G.TipX(1), G.A].Style);
        Assert.Equal('●', grid[G.TipX(2), G.A].Ch);
        Assert.Equal(new Style(Ink.Mrg, true), grid[G.TipX(2), G.A].Style);
    }

    [Fact]
    public void A_reword_or_edit_is_a_bold_warning_dot()
    {
        var rows = new[] { Row(C1, RebaseAction.Reword), Row(C2, RebaseAction.Edit), Row(C3) };

        var grid = RebaseScenes.Preview(G, L, rows, -1).Grid;

        Assert.Equal(new Style(Ink.Warn, true), grid[G.TipX(1), G.A].Style);
        Assert.Equal(new Style(Ink.Warn, true), grid[G.TipX(2), G.A].Style);
    }

    [Fact]
    public void The_cursor_is_a_marker_under_its_commit()
    {
        var rows = new[] { Row(C1), Row(C2), Row(C3) };

        var grid = RebaseScenes.Preview(G, L, rows, cursor: 1).Grid;

        Assert.Equal('▴', grid[G.TipX(2), G.A + 1].Ch);
        Assert.Equal(1, RowText(grid, G.A + 1).Count(c => c == '▴'));
    }

    [Fact]
    public void One_dim_ghost_lands_on_row_B_for_each_commit_that_is_kept()
    {
        // Two picks and a fold: the fold lands on the commit above it, so it adds no ghost.
        var rows = new[] { Row(C1), Row(C2), Row(C3, RebaseAction.Squash) };

        var grid = RebaseScenes.Preview(G, L, rows, -1).Grid;

        Assert.Equal(2, CountOn(grid, G.B, '◌'));
        Assert.Equal(new Style(Ink.Dim), grid[G.TipX(2), G.B].Style);
        Assert.Equal(new Style(Ink.Dim), grid[G.TipX(3), G.B].Style);
    }

    [Fact]
    public void More_than_three_landing_commits_draw_three_ghosts_and_then_a_tail()
    {
        var more = new[]
        {
            Row(C1), Row(C2), Row(C3),
            new RebaseRow("fffffff6", "fffffff6", "four", RebaseAction.Pick),
            new RebaseRow("fffffff7", "fffffff7", "five", RebaseAction.Pick),
        };

        var grid = RebaseScenes.Preview(G, L, more, -1).Grid;

        // The onto tip is commit 1, so the three ghosts sit at 2, 3 and 4; the fourth and fifth landings are not drawn.
        Assert.Equal(3, CountOn(grid, G.B, '◌'));
        Assert.Equal('◌', grid[G.TipX(4), G.B].Ch);
        Assert.Equal('┄', grid[G.TipX(4) + 1, G.B].Ch);
    }

    [Fact]
    public void A_dropped_commit_lands_nowhere()
    {
        var gone = new RebaseRow("eeeeeee5", "eeeeeee5", "gone", RebaseAction.Drop);

        var grid = RebaseScenes.Preview(G, L, [Row(C1), gone], -1).Grid;

        Assert.Equal(1, CountOn(grid, G.B, '◌'));
    }

    [Fact]
    public void Labels_name_the_current_branch_at_its_tip_and_the_onto_branch_on_row_B()
    {
        var grid = RebaseScenes.Preview(G, L, [Row(C1)], -1).Grid;

        Assert.Contains("feature/picks (current)", RowText(grid, G.A - 2));
        Assert.Contains("main", RowText(grid, G.B + 2));
    }

    [Fact]
    public void A_running_frame_draws_row_A_dim()
    {
        var grid = RebaseScenes.Preview(G, L, [Row(C1), Row(C2), Row(C3)], -1, dim: true).Grid;

        Assert.Equal(new Style(Ink.Dim), grid[G.TipX(1), G.A].Style);
    }
}
