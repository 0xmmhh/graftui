using graftui.Ui.Text;

namespace graftui.Tests;

public class GlyphsTests
{
    // Every lane field of a set: the marks, the dashes and the nine box-drawing lines.
    private static char[] LaneFields(Glyphs g) =>
        [g.Dot, g.Pulse, g.GhostEmpty, g.GhostHalf, g.DashH, g.DashV, g.Gap, g.FastForward, g.Conflict, g.MergeMark,
         g.Line, g.VLine, g.ForkMid, g.ForkEnd, g.MergeUp, g.MergeDown, g.TeeUp, g.TeeDown, g.MergeMid];

    [Fact]
    public void Unicode_uses_the_mock_glyphs()
    {
        Assert.Equal('●', Glyphs.Unicode.Dot);
        Assert.Equal('◉', Glyphs.Unicode.Pulse);
        Assert.Equal('◌', Glyphs.Unicode.GhostEmpty);
        Assert.Equal('┄', Glyphs.Unicode.DashH);
        Assert.Equal('┆', Glyphs.Unicode.DashV);
        Assert.Equal('⇢', Glyphs.Unicode.FastForward);
        Assert.Equal('✕', Glyphs.Unicode.Conflict);
        Assert.Equal('⑂', Glyphs.Unicode.MergeMark);
    }

    [Fact]
    public void Unicode_spinner_has_ten_frames()
    {
        Assert.Equal(10, Glyphs.Unicode.Spinner.Length);
    }

    [Fact]
    public void Basic_uses_plain_ascii_for_the_round_dashed_and_arrow_glyphs()
    {
        Assert.Equal('*', Glyphs.Basic.Dot);
        Assert.Equal('@', Glyphs.Basic.Pulse);
        Assert.Equal('o', Glyphs.Basic.GhostEmpty);
        Assert.Equal('o', Glyphs.Basic.GhostHalf);
        Assert.Equal('-', Glyphs.Basic.DashH);
        Assert.Equal(':', Glyphs.Basic.DashV);
        Assert.Equal('.', Glyphs.Basic.Gap);
        Assert.Equal('>', Glyphs.Basic.FastForward);
        Assert.Equal('x', Glyphs.Basic.Conflict);
        Assert.Equal('Y', Glyphs.Basic.MergeMark);
    }

    [Fact]
    public void Every_lane_field_of_Basic_is_ascii()
    {
        foreach (var c in LaneFields(Glyphs.Basic))
            Assert.True(c < 128, $"'{c}' is not ASCII");
    }

    [Fact]
    public void Basic_spinner_is_ascii()
    {
        foreach (var c in Glyphs.Basic.Spinner)
            Assert.True(c < 128, $"'{c}' is not ASCII");
    }

    [Fact]
    public void Basic_draws_the_box_lines_and_corners_as_dash_bar_and_plus()
    {
        Assert.Equal('-', Glyphs.Basic.Line);
        Assert.Equal('|', Glyphs.Basic.VLine);
        Assert.Equal('+', Glyphs.Basic.ForkMid);
        Assert.Equal('+', Glyphs.Basic.ForkEnd);
        Assert.Equal('+', Glyphs.Basic.MergeUp);
        Assert.Equal('+', Glyphs.Basic.MergeDown);
        Assert.Equal('+', Glyphs.Basic.TeeUp);
        Assert.Equal('+', Glyphs.Basic.TeeDown);
        Assert.Equal('+', Glyphs.Basic.MergeMid);
    }

    [Fact]
    public void Unicode_keeps_the_box_lines()
    {
        Assert.Equal('─', Glyphs.Unicode.Line);
        Assert.Equal('│', Glyphs.Unicode.VLine);
        Assert.Equal('├', Glyphs.Unicode.ForkMid);
        Assert.Equal('╰', Glyphs.Unicode.ForkEnd);
        Assert.Equal('┤', Glyphs.Unicode.MergeMid);
    }

    [Fact]
    public void Every_text_mark_in_the_table_maps_to_one_plain_character()
    {
        foreach (var pair in Glyphs.BasicText)
        {
            Assert.True(pair.Key > 127, $"'{pair.Key}' is already ASCII");
            Assert.True(pair.Value < 128, $"'{pair.Key}' maps to '{pair.Value}', not ASCII");
        }
    }

    [Theory]
    [InlineData('✔', '+')]
    [InlineData('✕', 'x')]
    [InlineData('→', '>')]
    [InlineData('↑', '^')]
    [InlineData('↓', 'v')]
    [InlineData('·', '.')]
    [InlineData('…', '.')]
    [InlineData('│', '|')]
    [InlineData('●', '*')]
    [InlineData('⑂', 'Y')]
    public void Basic_maps_the_marks_the_screen_uses(char from, char to)
    {
        Assert.Equal(new System.Text.Rune(to), Glyphs.PlainFor(new System.Text.Rune(from), Glyphs.Basic));
    }

    [Fact]
    public void Basic_leaves_plain_text_and_user_text_alone()
    {
        Assert.Equal(new System.Text.Rune('a'), Glyphs.PlainFor(new System.Text.Rune('a'), Glyphs.Basic));
        Assert.Equal(new System.Text.Rune('é'), Glyphs.PlainFor(new System.Text.Rune('é'), Glyphs.Basic));
    }

    [Fact]
    public void The_unicode_set_changes_no_rune()
    {
        Assert.Equal(new System.Text.Rune('✔'), Glyphs.PlainFor(new System.Text.Rune('✔'), Glyphs.Unicode));
        Assert.Equal(new System.Text.Rune('→'), Glyphs.PlainFor(new System.Text.Rune('→'), Glyphs.Unicode));
    }
}
