using graftui.Model;

namespace graftui;

/// <summary>The exit rules Program applies once the UI has closed. Pure: no console, no repo.</summary>
internal static class ExitRule
{
    /// <summary>
    /// True when the sequence that a branch change left paused is gone by exit: no pause graftui started is still there, and no sequence
    /// graftui started is still in progress without a readable stop (<paramref name="unreadable"/>). Only then does the change's own exit
    /// code stand down; while either is there, the sequence is still in progress and the code stays.
    /// </summary>
    public static bool ChangeGone(SequenceStop? paused, string? unreadable) => paused is null && unreadable is null;
}
