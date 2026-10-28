using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

public class LogViewTests
{
    private static int[] Ones(int count) => Enumerable.Repeat(1, count).ToArray();

    [Fact]
    public void No_anchor_shows_the_newest_lines()
    {
        Assert.Equal(7, LogView.TopLine(Ones(10), rows: 3, anchor: -1));
    }

    [Fact]
    public void No_anchor_with_fewer_lines_than_rows_starts_at_the_top()
    {
        Assert.Equal(0, LogView.TopLine(Ones(2), rows: 3, anchor: -1));
    }

    [Fact]
    public void Anchor_above_the_end_is_the_top_line()
    {
        Assert.Equal(4, LogView.TopLine(Ones(10), rows: 3, anchor: 4));
    }

    [Fact]
    public void Anchor_near_the_end_is_capped_at_the_newest_lines()
    {
        Assert.Equal(7, LogView.TopLine(Ones(10), rows: 3, anchor: 9));
    }

    [Fact]
    public void Everything_fits_with_an_anchor_starts_at_the_top()
    {
        Assert.Equal(0, LogView.TopLine(Ones(3), rows: 5, anchor: 2));
    }

    [Fact]
    public void Zero_rows_keeps_the_anchor()
    {
        Assert.Equal(4, LogView.TopLine(Ones(10), rows: 0, anchor: 4));
    }

    [Fact]
    public void A_tall_newest_line_is_the_bottom_line_even_when_it_is_taller_than_the_pane()
    {
        Assert.Equal(1, LogView.TopLine([1, 5], rows: 3, anchor: -1));
    }

    [Fact]
    public void Taller_lines_count_their_rows_when_choosing_the_newest_lines_that_fit()
    {
        // 1 + 3 rows fit in 4; the 1-row line above them is not shown.
        Assert.Equal(1, LogView.TopLine([1, 1, 3], rows: 4, anchor: -1));
        Assert.Equal(0, LogView.TopLine([1, 1, 3], rows: 5, anchor: -1));
    }

    [Fact]
    public void A_short_message_is_one_row_unchanged()
    {
        var line = new LogLine(LogKind.Message, "Refused: working tree has uncommitted changes", 0, default);

        var rows = LogView.Rows(line, 60);

        Assert.Equal([line], rows);
    }

    [Fact]
    public void A_long_message_wraps_at_words_and_every_row_fits()
    {
        var text = "the step is refs/graftui/undo/000001: git update-ref -d refs/graftui/undo/000001 drops it so older steps become reachable";
        var line = new LogLine(LogKind.Message, "    " + text, 0, default);

        var rows = LogView.Rows(line, 40);

        Assert.True(rows.Count > 2);
        Assert.All(rows, r => Assert.True(TextFit.Columns(r.Text) <= 40, r.Text));
        Assert.All(rows, r => Assert.StartsWith("    ", r.Text));
        Assert.Equal(text, string.Join(" ", rows.Select(r => r.Text.Trim())));
    }

    [Fact]
    public void Command_and_output_lines_stay_one_row()
    {
        var line = new LogLine(LogKind.Command, "git update-ref -d refs/graftui/undo/000001", 0);

        Assert.Equal([line], LogView.Rows(line, 10));
    }
}
