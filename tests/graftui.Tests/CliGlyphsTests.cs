namespace graftui.Tests;

/// <summary>--glyphs and GRAFTUI_GLYPHS: the set is read from the flag, or from the environment when no flag is given.</summary>
public class CliGlyphsTests
{
    private const string NeedsName = "graftui: --glyphs needs a name: basic or unicode";

    private static CliParse P(string? environment, params string[] args) => CliArgs.Parse(args, environment);

    private static CliOptions Ok(string? environment, params string[] args)
    {
        var parsed = CliArgs.Parse(args, environment);
        Assert.Null(parsed.Error);
        return parsed.Options!;
    }

    [Fact]
    public void Without_a_flag_or_variable_the_set_is_unicode()
    {
        Assert.False(Ok(null).BasicGlyphs);
        Assert.False(Ok("").BasicGlyphs);
    }

    [Theory]
    [InlineData("--glyphs", "basic")]
    [InlineData("--glyphs=basic", "")]
    [InlineData("--glyphs", "BASIC")]
    public void The_flag_picks_basic(string flag, string name)
    {
        var args = name.Length == 0 ? new[] { flag } : new[] { flag, name };
        Assert.True(Ok(null, args).BasicGlyphs);
    }

    [Theory]
    [InlineData("--glyphs", "unicode")]
    [InlineData("--glyphs=Unicode", "")]
    public void The_flag_picks_unicode(string flag, string name)
    {
        var args = name.Length == 0 ? new[] { flag } : new[] { flag, name };
        Assert.False(Ok(null, args).BasicGlyphs);
    }

    [Fact]
    public void The_variable_picks_basic_when_no_flag_is_given()
    {
        Assert.True(Ok("basic", "path").BasicGlyphs);
        Assert.True(Ok("Basic").BasicGlyphs);
        Assert.False(Ok("unicode").BasicGlyphs);
    }

    [Fact]
    public void The_flag_wins_over_the_variable()
    {
        Assert.False(Ok("basic", "--glyphs", "unicode").BasicGlyphs);
        Assert.False(Ok("basic", "--glyphs=unicode").BasicGlyphs);
        Assert.True(Ok("unicode", "--glyphs", "basic").BasicGlyphs);
    }

    [Fact]
    public void A_bad_flag_is_an_error_even_when_the_variable_is_good()
    {
        Assert.Equal("graftui: unknown glyph set 'nope'; choose basic or unicode", P("basic", "--glyphs", "nope").Error);
    }

    [Fact]
    public void A_bad_variable_is_an_error_naming_the_variable()
    {
        Assert.Equal("graftui: GRAFTUI_GLYPHS is 'nope'; choose basic or unicode", P("nope").Error);
    }

    [Fact]
    public void A_bad_variable_is_ignored_when_the_flag_gives_the_set()
    {
        Assert.Null(P("nope", "--glyphs", "basic").Error);
        Assert.True(P("nope", "--glyphs", "basic").Options!.BasicGlyphs);
    }

    [Fact]
    public void Flag_without_a_name_or_with_a_bad_name_is_an_error()
    {
        Assert.Equal(NeedsName, P(null, "--glyphs").Error);
        Assert.Equal(NeedsName, P(null, "--glyphs=").Error);
        Assert.Equal(NeedsName, P(null, "--glyphs", "--sandbox").Error);
        Assert.Equal("graftui: unknown glyph set 'fancy'; choose basic or unicode", P(null, "--glyphs=fancy").Error);
    }

    [Fact]
    public void A_second_glyphs_flag_is_an_error()
    {
        Assert.Equal("graftui: --glyphs given more than once", P(null, "--glyphs", "basic", "--glyphs", "unicode").Error);
        Assert.Equal("graftui: --glyphs given more than once", P(null, "--glyphs=basic", "--glyphs=basic").Error);
    }

    [Fact]
    public void Help_lists_the_glyphs_flag()
    {
        Assert.Contains("--glyphs <set>", CliArgs.HelpText);
        Assert.Contains("GRAFTUI_GLYPHS", CliArgs.HelpText);
        Assert.Contains("--new-ui", CliArgs.HelpText);
        Assert.Contains("the old look is only partly covered", CliArgs.HelpText);
    }
}
