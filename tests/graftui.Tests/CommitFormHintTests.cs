using graftui.Ui.Text;

namespace graftui.Tests;

public class CommitFormHintTests
{
    [Fact]
    public void In_the_subject_enter_saves_and_the_hint_says_so()
    {
        Assert.Equal("Enter commits · Tab: body · Esc cancels", CommitFormHint.For(amend: false, bodyFocused: false));
        Assert.Equal("Enter amends · Tab: body · Esc cancels", CommitFormHint.For(amend: true, bodyFocused: false));
    }

    [Fact]
    public void In_the_body_enter_adds_a_line_and_ctrl_j_saves()
    {
        Assert.Equal("Enter adds a line · Ctrl+J commits · Tab: subject · Esc cancels", CommitFormHint.For(amend: false, bodyFocused: true));
        Assert.Equal("Enter adds a line · Ctrl+J amends · Tab: subject · Esc cancels", CommitFormHint.For(amend: true, bodyFocused: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_hint_fits_two_lines_without_a_cut_at_the_narrowest_caption(bool amend, bool bodyFocused)
    {
        // The caption at 64x22 is 36 columns wide (measured on screen); the hint fits in its two lines, nothing cut with an ellipsis.
        var (first, second) = TextFit.Wrap2(CommitFormHint.For(amend, bodyFocused), 36);

        Assert.DoesNotContain(TextFit.Ellipsis, first);
        Assert.DoesNotContain(TextFit.Ellipsis, second ?? "");
    }
}
