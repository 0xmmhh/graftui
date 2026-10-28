using graftui.Ui.Text;

namespace graftui.Tests;

public class TextFitTests
{
    [Fact]
    public void WrapWords_breaks_at_spaces_and_cuts_a_word_longer_than_the_line()
    {
        Assert.Equal(new[] { "aaa", "bbbb…", "cc" }, TextFit.WrapWords("aaa bbbbbbbbbb cc", 5));
        Assert.Equal(new[] { "one two", "three" }, TextFit.WrapWords("one two three", 7));
        Assert.Empty(TextFit.WrapWords("   ", 5));
    }

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
    public void FitPair_cuts_nothing_when_both_names_fit()
    {
        Assert.Equal(("feature", "main"), TextFit.FitPair("feature", "main", 20));
    }

    [Fact]
    public void FitPair_keeps_the_name_that_fits_half_the_budget_whole()
    {
        Assert.Equal(("feature/confl…", "main"), TextFit.FitPair("feature/conflict", "main", 18));
        Assert.Equal(("feature/conflict", "main"), TextFit.FitPair("feature/conflict", "main", 20));
    }

    [Fact]
    public void FitPair_splits_the_budget_evenly_when_neither_name_fits_half()
    {
        var a = new string('a', 20);
        var b = new string('b', 20);

        var (x, y) = TextFit.FitPair(a, b, 20);

        Assert.Equal(new string('a', 9) + "…", x);
        Assert.Equal(new string('b', 9) + "…", y);
    }

    [Fact]
    public void FitPair_gives_each_name_at_least_eight_columns()
    {
        var (x, y) = TextFit.FitPair(new string('a', 30), new string('b', 30), 10);

        Assert.Equal(new string('a', 7) + "…", x);
        Assert.Equal(new string('b', 7) + "…", y);
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
    public void Wrap2_keeps_text_that_fits_on_one_line()
    {
        Assert.Equal(("abc", null), TextFit.Wrap2("abc", 5));
    }

    [Fact]
    public void Wrap2_breaks_at_the_last_space_that_fits()
    {
        Assert.Equal(("aa bb", "cc"), TextFit.Wrap2("aa bb cc", 5));
    }

    [Fact]
    public void Wrap2_cuts_a_long_second_line_with_an_ellipsis()
    {
        Assert.Equal(("hello", "big wor…"), TextFit.Wrap2("hello big world", 8));
    }

    [Fact]
    public void Wrap2_cuts_text_with_no_spaces_at_the_width()
    {
        Assert.Equal(("abcd", "efg…"), TextFit.Wrap2("abcdefghij", 4));
    }

    [Fact]
    public void Wrap2_returns_nothing_for_a_zero_width()
    {
        Assert.Equal(("", null), TextFit.Wrap2("abc", 0));
    }

    [Fact]
    public void Sanitize_replaces_control_characters_with_spaces()
    {
        Assert.Equal("a b", TextFit.Sanitize("a\u001bb"));
    }
}
