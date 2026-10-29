namespace graftui.Ui.Text;

/// <summary>
/// The commit form's hint line. It follows the focused field: Enter saves from the subject, but in the body Enter adds a line
/// and Ctrl+J saves.
/// </summary>
public static class CommitFormHint
{
    public static string For(bool amend, bool bodyFocused)
    {
        var verb = amend ? "amends" : "commits";
        return bodyFocused
            ? $"Enter adds a line · Ctrl+J {verb} · Tab moves to the subject · Esc cancels"
            : $"Enter {verb} · Tab moves to the body · Ctrl+Enter saves from the body · Esc cancels";
    }
}
