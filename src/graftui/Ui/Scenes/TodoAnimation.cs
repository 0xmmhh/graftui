using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// One interactive rebase's animation: a run of the todo scene, or the resume of a paused one. The scene is held at its hold point until git
/// answers. The answer says where the run stops (a pause, an abort, or the end), and the clock plays on from the hold point to that stop. Once
/// the run is over the result is drawn: the layout after the change, or, for a pause, the paused picture with the pause's caption.
/// </summary>
public sealed class TodoAnimation(GraphLayout layout, IReadOnlyList<RebaseRow> rows, TodoRun run) : IBranchAnimation
{
    private TodoRun _run = run;
    private long _offset;

    public BranchResult? Result { get; private set; }

    /// <summary>Records git's answer. The scene resumes from its hold point at the moment the answer arrives.</summary>
    public void SetResult(BranchResult r, long elapsedMs)
    {
        Result = r;
        _run = TodoScenes.Ended(_run, rows, r.Sequence);
        _offset = Math.Max(0, elapsedMs - TodoScenes.Hold(_run));
    }

    public bool IsDone(long elapsedMs) => Result is not null && TimeOf(elapsedMs) >= TodoScenes.Total(_run);

    public string Title(long elapsedMs) => Result is null ? RebaseText.RunTitle : Result.Title;

    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        var r = Result;
        var te = TimeOf(elapsedMs);
        var total = TodoScenes.Total(_run);
        if (r is null || te < total)
            return TodoScenes.Rebase(g, layout, rows, _run, te, elapsedMs, waiting: r is null && elapsedMs >= MergeAnimation.WaitAfterMs);

        // The run is over: a finished or aborted rebase shows its layout after the change; a pause or a failure keeps the scene at its end.
        if (r.After is not null && (_run.End == TodoEnd.Play || _run.End == TodoEnd.Abort))
        {
            var grid = MergeScenes.Preview(g, r.After).Grid;
            if (r.Overlay is not null)
                MergeScenes.MessageOverlay(grid, r.OverlayTitle ?? r.Title, r.Overlay);
            return new SceneFrame(grid, r.Caption);
        }

        var last = TodoScenes.Rebase(g, layout, rows, _run, total, elapsedMs, waiting: false);
        return new SceneFrame(last.Grid, r.Caption);
    }

    // The time the scene has reached: held at the hold point until the answer, then offset from the clock.
    private long TimeOf(long elapsedMs) =>
        Result is null ? Math.Min(elapsedMs, TodoScenes.Hold(_run)) : elapsedMs - _offset;
}
