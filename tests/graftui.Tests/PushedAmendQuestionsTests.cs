using graftui.Ui.App;

namespace graftui.Tests;

/// <summary>
/// The three pushed-amend questions. Only a digit answer, given after a quiet gap, moves them on; a burst of keys never does.
/// A pass through all three is the only way to reach the message box.
/// </summary>
public class PushedAmendQuestionsTests
{
    private static PushedAmendQuestions Started(long at = 0)
    {
        var q = new PushedAmendQuestions();
        q.Start(at);
        return q;
    }

    [Fact]
    public void A_burst_of_y_never_moves_past_the_first_question()
    {
        var q = Started();
        var results = new[] { 1000, 1010, 1020, 1030, 1040 }.Select(t => q.Press('y', t)).ToList();

        Assert.All(results, r => Assert.Equal(QuestionResult.Ignored, r));
        Assert.Equal(1, q.Step);
    }

    [Fact]
    public void A_burst_of_one_only_counts_once_and_never_reaches_the_message_box()
    {
        var q = Started();

        Assert.Equal(QuestionResult.Advanced, q.Press('1', 1000));
        Assert.Equal(QuestionResult.Ignored, q.Press('1', 1010));
        Assert.Equal(QuestionResult.Ignored, q.Press('1', 1020));
        Assert.Equal(QuestionResult.Ignored, q.Press('1', 1030));
        Assert.Equal(2, q.Step);
    }

    [Fact]
    public void Typed_ahead_123_in_one_burst_stops_after_the_first_question()
    {
        var q = Started();

        Assert.Equal(QuestionResult.Advanced, q.Press('1', 1000));
        Assert.Equal(QuestionResult.Ignored, q.Press('2', 1005));
        Assert.Equal(QuestionResult.Ignored, q.Press('3', 1010));
        Assert.Equal(2, q.Step);
    }

    [Fact]
    public void Holding_the_answer_key_through_all_three_questions_is_not_enough()
    {
        var q = Started();
        var results = new List<QuestionResult>();
        for (var t = 1000; t < 1500; t += 30)
            results.Add(q.Press('1', t));

        Assert.Equal(QuestionResult.Advanced, results[0]);
        Assert.Equal(2, q.Step);
        Assert.DoesNotContain(QuestionResult.Confirmed, results);
    }

    [Fact]
    public void One_then_two_then_three_each_after_the_gap_reaches_the_message_box()
    {
        var q = Started();

        Assert.Equal(QuestionResult.Advanced, q.Press('1', 1000));
        Assert.Equal(QuestionResult.Advanced, q.Press('2', 1800));
        Assert.Equal(QuestionResult.Confirmed, q.Press('3', 2600));
    }

    [Fact]
    public void A_key_that_is_not_the_answer_restarts_the_gap()
    {
        var q = Started();

        Assert.Equal(QuestionResult.Ignored, q.Press('x', 500));
        // 500 ms after the x is inside the gap, even though it is 1000 ms after the question appeared.
        Assert.Equal(QuestionResult.Ignored, q.Press('1', 1000));
        Assert.Equal(QuestionResult.Advanced, q.Press('1', 1800));
    }

    [Fact]
    public void An_ignored_digit_also_restarts_the_gap()
    {
        var q = Started();
        Assert.Equal(QuestionResult.Advanced, q.Press('1', 1000));

        Assert.Equal(QuestionResult.Ignored, q.Press('9', 1600));
        Assert.Equal(QuestionResult.Ignored, q.Press('2', 2000));
        Assert.Equal(QuestionResult.Advanced, q.Press('2', 2700));
    }

    [Fact]
    public void The_gap_is_exactly_seven_hundred_milliseconds()
    {
        var early = Started();
        Assert.Equal(QuestionResult.Ignored, early.Press('1', 699));

        var exact = Started();
        Assert.Equal(QuestionResult.Advanced, exact.Press('1', 700));
    }

    [Fact]
    public void Cancel_works_on_every_question()
    {
        foreach (var cancel in new[] { 'n', 'N', PushedAmendQuestions.EscapeKey })
        {
            var q = Started();
            Assert.Equal(QuestionResult.Cancelled, q.Press(cancel, 1000));

            q.Press('1', 2000);
            Assert.Equal(QuestionResult.Cancelled, q.Press(cancel, 2100));

            q.Press('2', 3000);
            Assert.Equal(QuestionResult.Cancelled, q.Press(cancel, 3050));
        }
    }

    [Fact]
    public void The_answer_key_follows_the_question_on_screen()
    {
        var q = Started();
        Assert.Equal('1', q.AnswerKey);
        q.Press('1', 1000);
        Assert.Equal('2', q.AnswerKey);
        q.Press('2', 2000);
        Assert.Equal('3', q.AnswerKey);
    }

    [Fact]
    public void Only_the_third_answer_confirms()
    {
        var q = Started();
        var results = new List<QuestionResult> { q.Press('1', 1000), q.Press('2', 2000), q.Press('3', 3000) };

        Assert.Equal(new[] { QuestionResult.Advanced, QuestionResult.Advanced, QuestionResult.Confirmed }, results);
    }

    [Fact]
    public void A_fresh_start_resets_the_questions_to_the_first()
    {
        var q = Started();
        q.Press('1', 1000);
        q.Press('2', 2000);

        q.Start(5000);

        Assert.Equal(1, q.Step);
    }
}
