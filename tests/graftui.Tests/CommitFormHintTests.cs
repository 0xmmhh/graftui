using graftui.Ui.Text;

namespace graftui.Tests;

public class CommitFormHintTests
{
    [Fact]
    public void In_the_subject_enter_saves_and_the_hint_says_so()
    {
        Assert.Equal("Enter commits · Tab moves to the body · Ctrl+Enter saves from the body · Esc cancels", CommitFormHint.For(amend: false, bodyFocused: false));
        Assert.Equal("Enter amends · Tab moves to the body · Ctrl+Enter saves from the body · Esc cancels", CommitFormHint.For(amend: true, bodyFocused: false));
    }

    [Fact]
    public void In_the_body_enter_adds_a_line_and_ctrl_j_saves()
    {
        Assert.Equal("Enter adds a line · Ctrl+J commits · Tab moves to the subject · Esc cancels", CommitFormHint.For(amend: false, bodyFocused: true));
        Assert.Equal("Enter adds a line · Ctrl+J amends · Tab moves to the subject · Esc cancels", CommitFormHint.For(amend: true, bodyFocused: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_hint_wraps_into_two_lines_without_a_cut_at_the_narrow_pane_width(bool amend, bool bodyFocused)
    {
        // The Changes pane at 64 columns is about this wide; the caption wraps to two lines, and nothing is cut with an ellipsis.
        var (first, second) = TextFit.Wrap2(CommitFormHint.For(amend, bodyFocused), 50);

        Assert.NotNull(second);
        Assert.DoesNotContain(TextFit.Ellipsis, first);
        Assert.DoesNotContain(TextFit.Ellipsis, second);
    }
}
