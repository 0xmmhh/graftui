using graftui.Ui.Scenes;

namespace graftui.Tests;

public class ArtGeometryTests
{
    [Fact]
    public void Eleven_rows_at_80_columns_uses_the_widest_step()
    {
        Assert.Equal(new ArtGeometry(80, 11, 6, 3, 7, 16), ArtGeometry.For(80, 11));
    }

    [Fact]
    public void Nine_rows_is_the_branches_art_without_the_two_empty_rows()
    {
        Assert.Equal(new ArtGeometry(80, 9, 6, 2, 6, 16), ArtGeometry.For(80, 9));
    }

    [Fact]
    public void Unsupported_height_has_no_geometry()
    {
        Assert.Null(ArtGeometry.For(80, 10));
    }

    [Fact]
    public void Narrow_grid_falls_back_to_a_smaller_step()
    {
        var g = ArtGeometry.For(25, 8);

        Assert.NotNull(g);
        Assert.Equal(3, g.Step);
        Assert.Equal(10, g.XB);
    }

    [Fact]
    public void Too_narrow_for_any_step_has_no_geometry()
    {
        Assert.Null(ArtGeometry.For(22, 8));
    }

    [Fact]
    public void Five_rows_is_the_single_line_art_with_its_line_on_row_two()
    {
        // B is 4 only so the record is valid: no row-B line is drawn in the single-line art, but row 4 holds the lower label's name.
        Assert.Equal(new ArtGeometry(80, 5, 6, 2, 4, 16), ArtGeometry.For(80, 5));
    }
}
