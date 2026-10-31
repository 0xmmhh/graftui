using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The preview options and label spots the branch animations are built on.</summary>
public class BranchPreviewTests
{
    // 80x11: the rows are A=3 (current) and B=7 (selected); XB=16 and one commit is 6 columns.
    private static readonly ArtGeometry Geo = ArtGeometry.For(80, 11)!;

    private static string Row(ArtGrid grid, int y) =>
        string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    private static string Text(ArtGrid grid) =>
        string.Join('\n', Enumerable.Range(0, grid.Height).Select(y => Row(grid, y)));

    private static GraphLayout Same(string name, string tip) =>
        new(name, false, name, tip, tip, tip, [], false, [], false, [Layouts.C(tip)], false, Layouts.C(tip), Layouts.C(tip), 0, 0,
            GraphKind.Same);

    [Fact]
    public void No_current_extra_drops_the_current_label_suffix_and_nothing_else()
    {
        var grid = MergeScenes.Preview(Geo, Layouts.Diverged(), new PreviewOptions(NoCurrentExtra: true)).Grid;

        Assert.DoesNotContain("(current)", Text(grid));
        Assert.Equal("main", Row(grid, Geo.A - 2).Trim());
        Assert.Equal("feature", Row(grid, Geo.B + 2).Trim());
    }

    [Fact]
    public void No_cur_name_keeps_the_marker_and_drops_the_name()
    {
        var grid = MergeScenes.Preview(Geo, Layouts.Diverged(), new PreviewOptions(NoCurName: true)).Grid;

        Assert.Equal('▾', grid[Geo.TipX(1), Geo.A - 1].Ch);
        Assert.DoesNotContain("main", Text(grid));
        Assert.Contains("feature", Text(grid));
    }

    [Fact]
    public void No_sel_name_keeps_the_marker_and_drops_the_name()
    {
        var grid = MergeScenes.Preview(Geo, Layouts.Diverged(), new PreviewOptions(NoSelName: true)).Grid;

        Assert.Equal('▴', grid[Geo.TipX(1), Geo.B + 1].Ch);
        Assert.DoesNotContain("feature", Text(grid));
        Assert.Contains("main (current)", Text(grid));
    }

    [Fact]
    public void Ghost_tail_turns_the_last_selected_commit_into_a_grey_ghost()
    {
        var grid = MergeScenes.Preview(Geo, Layouts.Diverged(), new PreviewOptions(GhostTail: 1)).Grid;

        var ghost = grid[Geo.TipX(1), Geo.B];
        Assert.Equal('◌', ghost.Ch);
        Assert.Equal(new Style(Ink.Dim), ghost.Style);
        // The segment leading to it is grey too.
        Assert.Equal(new Style(Ink.Dim), grid[Geo.TipX(1) - 2, Geo.B].Style);
        // The current line is untouched.
        Assert.Equal('●', grid[Geo.TipX(1), Geo.A].Ch);
    }

    [Fact]
    public void Ghost_tail_is_capped_at_the_selected_commits()
    {
        var grid = MergeScenes.Preview(Geo, Layouts.Diverged(), new PreviewOptions(GhostTail: 5)).Grid;

        Assert.Equal('◌', grid[Geo.TipX(1), Geo.B].Ch);
    }

    [Theory]
    [InlineData(GraphKind.Diverged)]
    [InlineData(GraphKind.FastForward)]
    [InlineData(GraphKind.UpToDate)]
    public void Label_spots_are_where_preview_draws_the_marker_and_the_name(GraphKind kind)
    {
        var L = kind switch
        {
            GraphKind.Diverged => Layouts.Diverged(),
            GraphKind.FastForward => Layouts.FastForward(),
            _ => Layouts.UpToDate(),
        };
        var spots = MergeScenes.LabelSpots(Geo, L);
        var grid = MergeScenes.Preview(Geo, L).Grid;

        Assert.Equal('▾', grid[spots.CurX, spots.CurRow - 1].Ch);
        Assert.Contains("main", Row(grid, spots.CurRow - 2));
        Assert.Equal('▴', grid[spots.SelX, spots.SelRow + 1].Ch);
        Assert.Contains("feature", Row(grid, spots.SelRow + 2));
        Assert.True(spots.SelShown);
    }

    [Fact]
    public void Label_spots_for_the_same_branch_show_one_label_only()
    {
        var L = Same("main", Layouts.Cur);

        var spots = MergeScenes.LabelSpots(Geo, L);

        Assert.False(spots.SelShown);
        Assert.Equal((Geo.XB, Geo.A, Geo.XB, Geo.A), (spots.CurX, spots.CurRow, spots.SelX, spots.SelRow));
    }

    [Fact]
    public void Label_spots_for_two_names_on_one_commit_show_both()
    {
        var L = Same("feature", Layouts.Cur) with { Current = "main" };

        var spots = MergeScenes.LabelSpots(Geo, L);

        Assert.True(spots.SelShown);
        Assert.Equal(Geo.A, spots.SelRow);
    }

    [Fact]
    public void Label_text_x_keeps_a_label_inside_the_grid()
    {
        Assert.Equal(16, MergeScenes.LabelTextX(Geo, 16, 4));
        Assert.Equal(Geo.Width - 10, MergeScenes.LabelTextX(Geo, Geo.Width, 10));
        Assert.Equal(0, MergeScenes.LabelTextX(Geo, -3, 4));
    }
}
