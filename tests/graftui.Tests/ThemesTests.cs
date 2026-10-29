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
}
