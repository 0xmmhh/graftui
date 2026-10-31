using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// Drives "stash, merge, pop": the changes are stashed (500 ms), the merge plays as a <see cref="MergeAnimation"/> (its own captions and
/// timing), then the changes are popped back (500 ms) before the final frame. The merge animation is driven as it is for a plain merge;
/// only the stash and pop phases are added around it. Without a merge (the stash failed, or the change was refused) it jumps to the final frame.
/// </summary>
public sealed class StashMergeAnimation(GraphLayout layout, int changed) : IBranchAnimation
{
    public const long StashMs = 500, PopMs = 500;

    // The longest a merge scene can run on its own clock: a conflict is 2250 ms. The search for its end stops well past that.
    private const long MaxMergeMs = 10_000;

    private static readonly Style Fg = new(Ink.Fg);

    private readonly MergeAnimation _merge = new(layout);

    // Set when the result arrives: whether the merge plays, when its scene ends (on this clock and on the merge's own), and when the pop ends.
    private bool _animated;
    private long _mergeEnd;
    private long _mergeOwnEnd;
    private long _doneAt;

    /// <inheritdoc/>
    public BranchResult? Result { get; private set; }

    /// <inheritdoc/>
    public void SetResult(BranchResult r, long elapsedMs)
    {
        Result = r;
        _animated = r.Success && r.Merge is not null;
        if (!_animated)
            return;

        // The merge clock starts when the stash ends; a result that arrives during the stash starts it at its beginning.
        var inner = Math.Max(0, elapsedMs - StashMs);
        _merge.SetOutcome(r.Merge!, inner);
        _mergeOwnEnd = FirstDone(_merge, inner + MaxMergeMs);
        _mergeEnd = StashMs + _mergeOwnEnd;
        _doneAt = _mergeEnd + PopMs;
    }

    /// <inheritdoc/>
    public bool IsDone(long elapsedMs) => Result is not null && (!_animated || elapsedMs >= _doneAt);

    /// <inheritdoc/>
    public string Title(long elapsedMs) => IsDone(elapsedMs) ? Result!.Title : "Merging";

    /// <inheritdoc/>
    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        if (IsDone(elapsedMs))
            return Done(g);

        if (elapsedMs < StashMs)
        {
            var stashing = new Caption($"{MergeScenes.Spin(elapsedMs)} Stashing your {MergeScenes.Plural(changed, "changed file")}…", Fg);
            return new SceneFrame(MergeScenes.Preview(g, layout).Grid, stashing);
        }

        if (Result is null || elapsedMs < _mergeEnd)
            return _merge.Render(g, elapsedMs - StashMs);

        // The merge is done: its final frame stays up while the changes are popped back.
        var popping = new Caption($"{MergeScenes.Spin(elapsedMs)} Popping your changes back…", Fg);
        return new SceneFrame(_merge.Render(g, _mergeOwnEnd).Grid, popping);
    }

    private SceneFrame Done(ArtGeometry g)
    {
        var r = Result!;
        var grid = DoneGrid(g, r);
        if (r.Overlay is not null)
            MergeScenes.MessageOverlay(grid, r.OverlayTitle ?? r.Title, r.Overlay);
        return new SceneFrame(grid, r.Caption);
    }

    /// <summary>The picture once the operation is done: the merged branch when the merge stands, else the layout as it was.</summary>
    private ArtGrid DoneGrid(ArtGeometry g, BranchResult r)
    {
        if (!r.Success || r.Merge is null)
            return MergeScenes.Preview(g, layout).Grid;
        if (r.After is not null)
            return MergeScenes.Preview(g, r.After).Grid;
        return _merge.Render(g, _mergeOwnEnd).Grid;
    }

    // The merge's end on its own clock. MergeAnimation keeps its timing private, so the end is found by search: IsDone only
    // ever turns true once, as the clock passes the end, which is what makes the search exact.
    private static long FirstDone(MergeAnimation merge, long hi)
    {
        long lo = 0;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (merge.IsDone(mid))
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }
}
