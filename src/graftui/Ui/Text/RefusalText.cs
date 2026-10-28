using graftui.Operations;

namespace graftui.Ui.Text;

/// <summary>
/// How a refusal is shown, on both tabs. The caption and the overlay get the first line of the reason only. The Commands
/// pane gets the whole reason: the detail is indented under it and wraps to the pane, so nothing is cut.
/// </summary>
public static class RefusalText
{
    public static string Caption(string reason) => "Refused: " + ChangeRefusals.Short(reason);

    public static string Overlay(string reason) => ChangeRefusals.Short(reason);

    /// <summary>The Commands pane lines for a refusal: the reason, then each detail line indented under it.</summary>
    public static IReadOnlyList<string> LogLines(string reason)
    {
        var first = reason.Split('\n')[0];
        return ["Refused: " + first, .. ChangeRefusals.Details(reason).Select(d => "    " + d)];
    }
}
