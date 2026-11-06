using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>
/// The decisions of a paused sequence's keys, the one table the pause screen asks. Enter continues the pause: a conflict continues, an
/// edit amends and continues, and a reword or a squash keeps its message. S skips. X is asked first by the screen, so it has no action
/// here. Every other key has no action.
/// </summary>
public static class PauseKeys
{
    /// <summary>
    /// What a key does on a pause of this kind. <paramref name="enter"/> is Enter; otherwise <paramref name="key"/> is the letter typed
    /// (S skips). Null for X and for any other key.
    /// </summary>
    public static ResumeAction? ActionFor(PauseKind kind, char key, bool enter)
    {
        if (enter)
        {
            if (kind == PauseKind.Conflict)
                return ResumeAction.Continue;
            if (kind == PauseKind.Edit)
                return ResumeAction.AmendAndContinue;
            return ResumeAction.Message;
        }
        return key == 'S' ? ResumeAction.Skip : null;
    }
}
