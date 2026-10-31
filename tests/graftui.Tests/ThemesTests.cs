using graftui.Model;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Drawing;

namespace graftui.Tests;

public class ThemesTests
{
    [Fact]
    public void There_are_five_themes_in_the_mock_order()
    {
        Assert.Equal(new[] { "default", "lifted", "muted", "high-contrast", "light" }, Themes.Names);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("LIFTED")]
    [InlineData("Muted")]
    [InlineData("high-contrast")]
    [InlineData("light")]
    public void Every_name_is_found_ignoring_case(string name)
    {
        Assert.True(Themes.TryGet(name, out var t));
        Assert.Equal(name.ToLowerInvariant(), t.Name);
    }

    [Fact]
    public void An_unknown_name_is_not_found()
    {
        Assert.False(Themes.TryGet("nope", out var t));
        Assert.Equal(Themes.Default, t);
    }

    [Fact]
    public void Default_matches_the_colours_the_app_has_always_used()
    {
        var d = Themes.Default;
        Assert.Equal(new Color(0x12, 0x16, 0x1d), d.Term);
        Assert.Equal(new Color(0x2b, 0x34, 0x42), d.Frame);
        Assert.Equal(new Color(0xd5, 0xdc, 0xe5), d.Fg);
        Assert.Equal(new Color(0x6c, 0x7a, 0x8c), d.Dim);
        Assert.Equal(new Color(0x1a, 0x20, 0x29), d.Chip);
        Assert.Equal(new Color(0x22, 0x31, 0x4a), d.SelRow);
        Assert.Equal(new Color(0x4f, 0xd1, 0xc5), d.Cur);
        Assert.Equal(new Color(0xc3, 0x9b, 0xff), d.Inc);
        Assert.Equal(new Color(0xf5, 0xc4, 0x51), d.Mrg);
        Assert.Equal(new Color(0x7e, 0xe7, 0x87), d.Ok);
        Assert.Equal(new Color(0xff, 0x6b, 0x6b), d.Bad);
        Assert.Equal(new Color(0xf0, 0xa3, 0x5e), d.Warn);
        Assert.Equal(new Color(0xb3, 0x9b, 0x62), d.Hash);
    }

    [Fact]
    public void Default_is_the_palette_defaults()
    {
        var d = Themes.Default;
        Assert.Equal(Palette.Term, d.Term);
        Assert.Equal(Palette.Frame, d.Frame);
        Assert.Equal(Palette.Fg, d.Fg);
        Assert.Equal(Palette.Dim, d.Dim);
        Assert.Equal(Palette.SelRow, d.SelRow);
        Assert.Equal(Palette.Cur, d.Cur);
        Assert.Equal(Palette.Inc, d.Inc);
        Assert.Equal(Palette.Mrg, d.Mrg);
        Assert.Equal(Palette.Ok, d.Ok);
        Assert.Equal(Palette.Bad, d.Bad);
        Assert.Equal(Palette.Warn, d.Warn);
        Assert.Equal(Palette.Hash, d.Hash);
        Assert.Equal(Palette.Chip, d.Chip);
    }

    [Fact]
    public void Lifted_chip_is_the_mock_value()
    {
        Assert.True(Themes.TryGet("lifted", out var t));
        Assert.Equal(new Color(0x25, 0x2d, 0x3a), t.Chip);
    }

    [Fact]
    public void Light_background_is_the_mock_value()
    {
        Assert.True(Themes.TryGet("light", out var t));
        Assert.Equal(new Color(0xf7, 0xf8, 0xfa), t.Term);
        Assert.Equal(new Color(0x1d, 0x24, 0x30), t.Fg);
    }

    public static IEnumerable<object[]> EveryThemeName() => Themes.Names.Select(n => new object[] { n });

    [Fact]
    public void Default_lanes_are_the_design_colours_in_lane_order()
    {
        var expected = new[] { "5b9dff", "c39bff", "4fd1c5", "f78fb3", "b5e36b", "7dcfff" };
        var lanes = Themes.Default.Lanes;
        Assert.Equal(BranchLanes.Count, lanes.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(Hex(expected[i]), lanes[i]);
    }

    [Fact]
    public void Palette_lane_defaults_are_the_default_theme_lanes()
    {
        for (var i = 0; i < BranchLanes.Count; i++)
            Assert.Equal(Themes.Default.Lanes[i], Palette.Lane(i));
    }

    [Fact]
    public void A_lane_ink_is_drawn_in_its_lane_colour()
    {
        for (var i = 0; i < BranchLanes.Count; i++)
            Assert.Equal(Palette.Lane(i), Palette.Get(new Style((Ink)((int)Ink.Lane0 + i))).Foreground);
    }

    [Theory]
    [MemberData(nameof(EveryThemeName))]
    public void Every_theme_has_one_lane_per_branch_lane_and_they_all_differ(string name)
    {
        Assert.True(Themes.TryGet(name, out var t));
        Assert.Equal(BranchLanes.Count, t.Lanes.Count);
        Assert.Equal(BranchLanes.Count, t.Lanes.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(EveryThemeName))]
    public void No_lane_is_the_colour_of_a_status_or_the_merge_commit(string name)
    {
        Assert.True(Themes.TryGet(name, out var t));
        foreach (var lane in t.Lanes)
        {
            Assert.NotEqual(t.Bad, lane);
            Assert.NotEqual(t.Mrg, lane);
            Assert.NotEqual(t.Ok, lane);
            Assert.NotEqual(t.Warn, lane);
        }
    }

    [Theory]
    [MemberData(nameof(EveryThemeName))]
    public void Every_lane_is_readable_on_the_background_and_on_the_selected_row(string name)
    {
        Assert.True(Themes.TryGet(name, out var t));
        foreach (var lane in t.Lanes)
        {
            Assert.True(Contrast(lane, t.Term) >= 4.5, $"{name}: {lane.R:x2}{lane.G:x2}{lane.B:x2} on the background");
            Assert.True(Contrast(lane, t.SelRow) >= 3.9, $"{name}: {lane.R:x2}{lane.G:x2}{lane.B:x2} on the selected row");
        }
    }

    // "rrggbb" as a colour.
    private static Color Hex(string rrggbb) => new(
        Convert.ToByte(rrggbb[..2], 16),
        Convert.ToByte(rrggbb[2..4], 16),
        Convert.ToByte(rrggbb[4..6], 16));

    // WCAG 2 contrast ratio between two colours.
    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte channel)
    {
        var v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
