using graftui.Ui.Views;

namespace graftui.Tests;

public class LogViewTests
{
    [Fact]
    public void No_anchor_shows_the_newest_lines()
    {
        Assert.Equal(7, LogView.TopLine(count: 10, rows: 3, anchor: -1));
    }

    [Fact]
    public void No_anchor_with_fewer_lines_than_rows_starts_at_the_top()
    {
        Assert.Equal(0, LogView.TopLine(count: 2, rows: 3, anchor: -1));
    }

    [Fact]
    public void Anchor_above_the_end_is_the_top_line()
    {
        Assert.Equal(4, LogView.TopLine(count: 10, rows: 3, anchor: 4));
    }

    [Fact]
    public void Anchor_near_the_end_is_capped_at_the_newest_lines()
    {
        Assert.Equal(7, LogView.TopLine(count: 10, rows: 3, anchor: 9));
    }

    [Fact]
    public void Everything_fits_with_an_anchor_starts_at_the_top()
    {
        Assert.Equal(0, LogView.TopLine(count: 3, rows: 5, anchor: 2));
    }

    [Fact]
    public void Zero_rows_keeps_the_anchor()
    {
        Assert.Equal(4, LogView.TopLine(count: 10, rows: 0, anchor: 4));
    }
}
