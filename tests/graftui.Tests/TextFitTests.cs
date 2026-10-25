using graftui.Ui.Text;

namespace graftui.Tests;

public class TextFitTests
{
    [Theory]
    [InlineData("abcdef", 3, "ab…")]
    [InlineData("abc", 3, "abc")]
    [InlineData("abc", 0, "")]
    [InlineData("x", -1, "")]
    public void Fit_cuts_with_an_ellipsis_and_handles_empty_widths(string text, int width, string expected)
    {
        Assert.Equal(expected, TextFit.Fit(text, width));
    }

    [Fact]
    public void Columns_counts_wide_characters_as_two()
    {
        Assert.Equal(4, TextFit.Columns("日本"));
    }

    [Fact]
    public void Fit_never_splits_a_wide_character()
    {
        Assert.Equal("日…", TextFit.Fit("日本語", 4));
    }

    [Fact]
    public void Sanitize_replaces_control_characters_with_spaces()
    {
        Assert.Equal("a b", TextFit.Sanitize("a\u001bb"));
    }
}
