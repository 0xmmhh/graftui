using graftui.Ui.App;

namespace graftui.Tests;

/// <summary>
/// The three pushed-amend questions: "Are you sure?" (y), "Are you really sure?" (1), "Last chance, are you still sure?" (2).
/// Only the question's own yes key moves on; n, N and Esc cancel at every step; anything else is ignored. No timing is involved.
/// </summary>
public class PushedAmendQuestionsTests
{
    /// <summary>Presses each key in order on a fresh set of questions. The escape character stands for Esc.</summary>
    private static (PushedAmendQuestions Questions, List<QuestionResult> Results) Press(string keys)
    {
        var q = new PushedAmendQuestions();
        q.Start();
        var results = keys.Select(k => q.Press(k == '\u001b' ? PushedAmendQuestions.EscapeKey : k)).ToList();
        return (q, results);
    }

    [Fact]
    public void The_questions_run_in_escalating_order_with_their_own_yes_keys()
    {
        var q = new PushedAmendQuestions();
        q.Start();

        Assert.Equal("Are you sure?", q.Question);
        Assert.Equal('y', q.YesKey);
        Assert.Equal(QuestionResult.Advanced, q.Press('y'));

        Assert.Equal("Are you really sure?", q.Question);
        Assert.Equal('1', q.YesKey);
        Assert.Equal(QuestionResult.Advanced, q.Press('1'));

        Assert.Equal("Last chance, are you still sure?", q.Question);
        Assert.Equal('2', q.YesKey);
        Assert.Equal(QuestionResult.Confirmed, q.Press('2'));
    }

    [Fact]
    public void Y12_confirms()
    {
        var (q, results) = Press("y12");

        Assert.Equal([QuestionResult.Advanced, QuestionResult.Advanced, QuestionResult.Confirmed], results);
        Assert.Equal(3, q.Step);
    }

    [Fact]
    public void A_repeat_of_the_yes_key_does_not_move_past_the_second_question()
    {
        var (q, results) = Press("yyyy");

        Assert.Equal(QuestionResult.Advanced, results[0]);
        Assert.All(results.Skip(1), r => Assert.Equal(QuestionResult.Ignored, r));
        Assert.Equal(2, q.Step);
    }

    [Fact]
    public void A_burst_of_ones_does_not_move_past_the_last_question()
    {
        var (q, results) = Press("y11111");

        Assert.Equal(QuestionResult.Advanced, results[0]);
        Assert.Equal(QuestionResult.Advanced, results[1]);
        Assert.All(results.Skip(2), r => Assert.Equal(QuestionResult.Ignored, r));
        Assert.Equal(3, q.Step);
    }

    [Theory]
    [InlineData("121", 1)]
    [InlineData("12y", 2)]
    [InlineData("2", 1)]
    [InlineData("12", 1)]
    [InlineData("1y2", 2)]
    [InlineData("y2y", 2)]
    public void Keys_in_the_wrong_order_make_no_progress_beyond_the_right_ones(string keys, int expectedStep)
    {
        var (q, results) = Press(keys);

        Assert.Equal(expectedStep, q.Step);
        Assert.DoesNotContain(QuestionResult.Confirmed, results);
    }

    [Fact]
    public void A_wrong_digit_at_the_second_question_is_ignored_and_the_question_stays()
    {
        var (q, results) = Press("y9");

        Assert.Equal([QuestionResult.Advanced, QuestionResult.Ignored], results);
        Assert.Equal(2, q.Step);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("N")]
    [InlineData("\u001b")]
    public void Cancel_keys_cancel_at_every_step(string cancel)
    {
        Assert.Equal(QuestionResult.Cancelled, Press(cancel).Results[0]);
        Assert.Equal(QuestionResult.Cancelled, Press("y" + cancel).Results[1]);
        Assert.Equal(QuestionResult.Cancelled, Press("y1" + cancel).Results[2]);
    }

    [Fact]
    public void Other_keys_are_ignored_and_never_confirm()
    {
        var (q, results) = Press("x ?\t");

        Assert.All(results, r => Assert.Equal(QuestionResult.Ignored, r));
        Assert.Equal(1, q.Step);
    }

    [Fact]
    public void Start_goes_back_to_the_first_question()
    {
        var q = new PushedAmendQuestions();
        q.Start();
        q.Press('y');
        q.Press('1');

        q.Start();

        Assert.Equal(1, q.Step);
        Assert.Equal("Are you sure?", q.Question);
    }
}
