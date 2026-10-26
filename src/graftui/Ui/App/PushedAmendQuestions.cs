namespace graftui.Ui.App;

/// <summary>What one key press did to the pushed-amend questions.</summary>
internal enum QuestionResult
{
    /// <summary>Not a yes or a cancel for the question on screen. The question stays.</summary>
    Ignored,

    /// <summary>The yes key for the question on screen: the next question is shown.</summary>
    Advanced,

    /// <summary>The yes key for the last question: the message box may open.</summary>
    Confirmed,

    /// <summary>n, N or Esc: nothing changed.</summary>
    Cancelled,
}

/// <summary>
/// The three questions shown before a pushed commit may be amended, asked in escalating order:
/// "Are you sure?" (answer y), "Are you really sure?" (answer 1), "Last chance, are you still sure?" (answer 2).
/// n, N or Esc cancels at every step. Every other key is ignored and the question stays. There is no timing: a question
/// is answered by its key alone, so a repeat of an earlier step's key never counts for a later one.
/// Pure state, so the rules are tested without a clock or a terminal.
/// </summary>
internal sealed class PushedAmendQuestions
{
    public const int Count = 3;
    public const char CancelKey = 'n';
    public const char EscapeKey = '\u001b';

    /// <summary>The question on screen, 1 to <see cref="Count"/>.</summary>
    public int Step { get; private set; } = 1;

    /// <summary>The key that answers yes to the question on screen: y, then 1, then 2.</summary>
    public char YesKey => Step switch
    {
        1 => 'y',
        2 => '1',
        _ => '2',
    };

    /// <summary>The text of the question on screen.</summary>
    public string Question => Step switch
    {
        1 => "Are you sure?",
        2 => "Are you really sure?",
        _ => "Last chance, are you still sure?",
    };

    /// <summary>True for the keys that cancel (n, N, Esc).</summary>
    public static bool IsCancel(char key) => key is CancelKey or 'N' or EscapeKey;

    /// <summary>Starts again at the first question.</summary>
    public void Start() => Step = 1;

    /// <summary>Handles one key. <paramref name="key"/> is the typed character, or <see cref="EscapeKey"/>; '\0' for keys with no character.</summary>
    public QuestionResult Press(char key)
    {
        if (IsCancel(key))
            return QuestionResult.Cancelled;
        if (key != YesKey)
            return QuestionResult.Ignored;

        if (Step < Count)
        {
            Step++;
            return QuestionResult.Advanced;
        }
        return QuestionResult.Confirmed;
    }
}
