using graftui.Ui.Text;

namespace graftui.Tests;

public class GlyphsTests
{
    // The glyphs that Basic replaces: every field except the eight box-drawing lines.
    private static char[] ReplacedFields(Glyphs g) =>
        [g.Dot, g.Pulse, g.GhostEmpty, g.GhostHalf, g.DashH, g.DashV, g.Gap, g.FastForward, g.Conflict, g.MergeMark];

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
    public void Every_replaced_field_of_Basic_is_ascii()
    {
        foreach (var c in ReplacedFields(Glyphs.Basic))
            Assert.True(c < 128, $"'{c}' is not ASCII");
    }

    [Fact]
    public void Basic_spinner_is_ascii()
    {
        foreach (var c in Glyphs.Basic.Spinner)
            Assert.True(c < 128, $"'{c}' is not ASCII");
    }

    [Fact]
    public void The_box_drawing_lines_are_the_same_in_both_sets()
    {
        Assert.Equal(Glyphs.Unicode.Line, Glyphs.Basic.Line);
        Assert.Equal(Glyphs.Unicode.VLine, Glyphs.Basic.VLine);
        Assert.Equal(Glyphs.Unicode.ForkMid, Glyphs.Basic.ForkMid);
        Assert.Equal(Glyphs.Unicode.ForkEnd, Glyphs.Basic.ForkEnd);
        Assert.Equal(Glyphs.Unicode.MergeUp, Glyphs.Basic.MergeUp);
        Assert.Equal(Glyphs.Unicode.MergeDown, Glyphs.Basic.MergeDown);
        Assert.Equal(Glyphs.Unicode.TeeUp, Glyphs.Basic.TeeUp);
        Assert.Equal(Glyphs.Unicode.TeeDown, Glyphs.Basic.TeeDown);
    }
}
