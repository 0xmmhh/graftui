using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// One rebase's animation. Until git answers, the preview is drawn with the current branch's row dim, and the caption says what runs
/// (after the wait limit, the wait text). Once the result is in, the animation is done: the result's layout is drawn with its caption.
/// </summary>
public sealed class RebaseAnimation(GraphLayout layout, IReadOnlyList<RebaseRow> rows) : IBranchAnimation
{
    public BranchResult? Result { get; private set; }

    public void SetResult(BranchResult r, long elapsedMs) => Result = r;

    public bool IsDone(long elapsedMs) => Result is not null;

    public string Title(long elapsedMs) => Result is null ? RebaseText.RunTitle : Result.Title;

    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        var r = Result;
        if (r is null)
        {
            var text = $"{MergeScenes.Spin(elapsedMs)} {RebaseText.Running(layout.Current, layout.Selected)}";
            var caption = MergeScenes.Running(elapsedMs >= MergeAnimation.WaitAfterMs, text);
            return new SceneFrame(RebaseScenes.Preview(g, layout, rows, -1, dim: true).Grid, caption);
        }

        var grid = MergeScenes.Preview(g, r.After ?? layout).Grid;
        if (r.Overlay is not null)
            MergeScenes.MessageOverlay(grid, r.OverlayTitle ?? r.Title, r.Overlay);
        return new SceneFrame(grid, r.Caption);
    }
}
