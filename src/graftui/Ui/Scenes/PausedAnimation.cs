using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// The art while a paused rebase is continued, skipped or aborted: the paused picture with the running caption until git answers, then
/// the result's layout with its caption (or the paused picture again when the result has no layout).
/// </summary>
public sealed class PausedAnimation(SequenceStop stop, string running, Ink branchInk, Ink ontoInk) : IBranchAnimation
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
            var text = $"{MergeScenes.Spin(elapsedMs)} {running}";
            var caption = MergeScenes.Running(elapsedMs >= MergeAnimation.WaitAfterMs, text);
            return new SceneFrame(RebaseScenes.Paused(g, stop, branchInk, ontoInk).Grid, caption);
        }

        var grid = r.After is null
            ? RebaseScenes.Paused(g, stop, branchInk, ontoInk).Grid
            : MergeScenes.Preview(g, r.After).Grid;
        return new SceneFrame(grid, r.Caption);
    }
}
