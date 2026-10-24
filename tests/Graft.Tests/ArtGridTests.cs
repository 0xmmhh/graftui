using Graft.Ui.Scenes;
using Graft.Ui.Text;

namespace Graft.Tests;

public class ArtGridTests
{
    private static readonly Style Bad = new(Ink.Bad);
    private static readonly Style Blank = new(Ink.Fg);

    [Fact]
    public void Put_then_read_round_trips()
    {
        var grid = new ArtGrid(5, 3);

        grid.Put(2, 1, 'x', Bad);

        Assert.Equal('x', grid[2, 1].Ch);
        Assert.Equal(Bad, grid[2, 1].Style);
    }

    [Fact]
    public void Writes_outside_the_grid_are_ignored_and_reads_outside_give_a_blank()
    {
        var grid = new ArtGrid(5, 3);

        grid.Put(-1, 0, 'x', Bad);
        grid.Put(5, 0, 'x', Bad);
        grid.Put(0, 3, 'x', Bad);

        Assert.Equal(' ', grid[-1, 0].Ch);
        Assert.Equal(' ', grid[5, 2].Ch);
        Assert.Equal(new Cell(' ', Blank), grid[0, 3]);
        Assert.All(Cells(grid), c => Assert.Equal(' ', c.Ch));
    }

    [Fact]
    public void Text_writes_one_glyph_per_column_and_replaces_wide_runes_with_a_question_mark()
    {
        var grid = new ArtGrid(5, 1);

        grid.Text(0, 0, "a日b", Bad);

        Assert.Equal('a', grid[0, 0].Ch);
        Assert.Equal('?', grid[1, 0].Ch);
        Assert.Equal('b', grid[2, 0].Ch);
    }

    [Fact]
    public void HLine_and_VLine_use_box_drawing_glyphs_by_default()
    {
        var grid = new ArtGrid(5, 5);

        grid.HLine(0, 2, 0, Bad);
        grid.VLine(4, 0, 2, Bad);

        Assert.Equal('─', grid[0, 0].Ch);
        Assert.Equal('─', grid[2, 0].Ch);
        Assert.Equal('│', grid[4, 1].Ch);
    }

    [Fact]
    public void Clear_blanks_the_rectangle_and_nothing_else()
    {
        var grid = new ArtGrid(4, 2);
        grid.Text(0, 0, "abcd", Bad);
        grid.Text(0, 1, "efgh", Bad);

        grid.Clear(1, 2, 0, 0);

        Assert.Equal('a', grid[0, 0].Ch);
        Assert.Equal(' ', grid[1, 0].Ch);
        Assert.Equal(' ', grid[2, 0].Ch);
        Assert.Equal('d', grid[3, 0].Ch);
        Assert.Equal('e', grid[0, 1].Ch);
    }

    [Fact]
    public void DimAll_dims_visible_glyphs_only()
    {
        var grid = new ArtGrid(3, 1);
        grid.Put(0, 0, 'x', Bad);

        grid.DimAll();

        Assert.Equal(new Style(Ink.Dim), grid[0, 0].Style);
        Assert.Equal(new Cell(' ', Blank), grid[1, 0]);
    }

    private static IEnumerable<Cell> Cells(ArtGrid grid)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                yield return grid[x, y];
    }
}
