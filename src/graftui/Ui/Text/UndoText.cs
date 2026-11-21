using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>
/// The one plain line the simplified Commands pane shows for an undo: what went back, in the user's words. The wording
/// comes from the undo step's own description, never from git's text. The exact pane (v) shows the commands that ran.
/// </summary>
internal static class UndoText
{
    /// <summary>"↶ undo: the merge of feature/x", "↶ undo: put the stash-merge back".</summary>
    public static string PlainLine(UndoRecord step)
    {
        string what;
        switch (step.Kind)
        {
            case UndoKind.Stash:
                // Undoing a stash brings the stashed changes back; the stash entry itself is taken by it.
                what = "brought the stashed changes back";
                break;
            case UndoKind.Pop:
                what = "put the stash entry back";
                break;
            case UndoKind.StashMerge:
                what = "put the stash-merge back";
                break;
            default:
                what = "the " + step.What;
                break;
        }
        return "↶ undo: " + what;
    }
}
