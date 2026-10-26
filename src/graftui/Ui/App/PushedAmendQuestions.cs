namespace graftui.Ui.App;

/// <summary>What one key press did to the pushed-amend questions.</summary>
internal enum QuestionResult
{
    /// <summary>Not counted: the wrong key, or a right key that came too soon after the previous key.</summary>
    Ignored,

    /// <summary>The right key after a quiet gap: the next question is shown.</summary>
    Advanced,

    /// <summary>The third question was answered: the message box may open.</summary>
    Confirmed,

    /// <summary>n or Esc: nothing changed.</summary>
    Cancelled,
}

/// <summary>
/// The three questions shown before a pushed commit may be amended. Question n is answered by pressing the digit n, after a
/// quiet gap. Every key received while the questions are up restarts the gap, ignored keys included, so a held key, a burst of
/// keys or a typed-ahead "123" never moves past a question. n or Esc cancels on every question.
/// Pure state: the caller passes the time, so the rules can be tested without a clock or a terminal.
/// </summary>
internal sealed class PushedAmendQuestions
{
    public const int Count = 3;
    public const long QuietGapMs = 700;
    public const char CancelKey = 'n';
    public const char EscapeKey = '\u001b';

    private long _lastKeyMs;

    /// <summary>The question on screen, 1 to <see cref="Count"/>.</summary>
    public int Step { get; private set; }

    /// <summary>The key that answers the question on screen.</summary>
    public char AnswerKey => (char)('0' + Step);

    /// <summary>Shows question 1. The gap is measured from this moment.</summary>
    public void Start(long nowMs)
    {
        Step = 1;
        _lastKeyMs = nowMs;
    }

    /// <summary>True for the keys that cancel (n, N, Esc). They work even when the question is not visible.</summary>
    public static bool IsCancel(char key) => key is CancelKey or 'N' or EscapeKey;

    /// <summary>Handles one key. <paramref name="key"/> is the typed character, or <see cref="EscapeKey"/>; '\0' for keys with no character.</summary>
    public QuestionResult Press(char key, long nowMs)
    {
        // The gap is measured from the previous key, whatever it was, and every key restarts it.
        var quiet = nowMs - _lastKeyMs >= QuietGapMs;
        _lastKeyMs = nowMs;

        if (IsCancel(key))
            return QuestionResult.Cancelled;
        if (!quiet || key != AnswerKey)
            return QuestionResult.Ignored;

        if (Step < Count)
        {
            Step++;
            return QuestionResult.Advanced;
        }
        return QuestionResult.Confirmed;
    }
}
