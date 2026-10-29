using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// What a branch change shows once git has answered. <c>After</c> is the layout drawn at the end (null = keep the scene's own last
/// frame). <c>Overlay</c>, when set, is drawn as a message box over the final frame. <c>Merge</c> is set only for a stash-merge-pop.
/// </summary>
public sealed record BranchResult(bool Success, string Title, Caption Caption, GraphLayout? After, string? OverlayTitle = null,
    string? Overlay = null, MergeOutcome? Merge = null);

/// <summary>
/// One branch change's animation: switch, create, rename, delete, or stash-merge-pop. Held at its hold point until git answers, then
/// played out to its final frame, the same way the Changes tab's animations work.
/// </summary>
public interface IBranchAnimation
{
    /// <summary>git's answer, once it has arrived.</summary>
    BranchResult? Result { get; }

    /// <summary>Records git's answer. <paramref name="elapsedMs"/> is the time on the animation clock.</summary>
    void SetResult(BranchResult r, long elapsedMs);

    /// <summary>True once the scene has played out after the result arrived (a failure is done at once).</summary>
    bool IsDone(long elapsedMs);

    /// <summary>Running titles while the change is in flight; the result's title once it is done.</summary>
    string Title(long elapsedMs);

    /// <summary>The frame at this time on the animation clock.</summary>
    SceneFrame Render(ArtGeometry g, long elapsedMs);
}
