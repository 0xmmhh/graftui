using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>Which stash operation the animation shows: a discard of one file, a stash, a pop, or an undo of any of them.</summary>
public enum StashOp { Discard, Stash, Pop, Undo }

/// <summary>
/// Drives one stash operation's animation (the mock's doDiscard, doStash and doPop). Like <see cref="ChangesAnimation"/>, the scene
/// is held at its hold point until git answers, then plays out from there; a failure jumps straight to the final frame.
/// The stash box shows the old count until the result says otherwise. <paramref name="moving"/> is the rows that leave the
/// boxes: the discarded row, or every row a stash takes. A pop passes none, because the rows it brings back come with the result.
/// </summary>
public sealed class StashAnimation(StashOp op, AreasModel before, IReadOnlyList<FileChange> moving, string runningText) : IAreasAnimation
{
    // Discard: the letter blinks while git runs, the token flies to the stash from 400 ms, turns Warn at 700 and fades to Dim at 950.
    private const long DiscardHold = 400, DiscardWarn = 700, DiscardGone = 950, DiscardTotal = 1050;

    // Stash and pop: each token flies 700 ms, 80 ms after the one before. Past the sixth it starts with the sixth.
    private const long FlyMs = 700, StaggerMs = 80, TailMs = 100, MaxStagger = 5;
    private const long PopHold = 300, UndoTotal = 300;

    private static readonly Style Fg = new(Ink.Fg), FgB = new(Ink.Fg, true), Dim = new(Ink.Dim);
    private static readonly Style Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true);
    private static readonly Style Bad = new(Ink.Bad), BadB = new(Ink.Bad, true);
    private static readonly Style Inc = new(Ink.Inc), IncB = new(Ink.Inc, true);

    private long _offset;

    public long HoldMs { get; } = op switch
    {
        StashOp.Discard => DiscardHold,
        StashOp.Stash => FlightMs(moving.Count),
        StashOp.Pop => PopHold,
        _ => 0,
    };

    /// <summary>For a pop this is an estimate until the result says how many rows come back.</summary>
    public long TotalMs { get; private set; } = op switch
    {
        StashOp.Discard => DiscardTotal,
        StashOp.Stash => FlightMs(moving.Count) + TailMs,
        StashOp.Pop => PopHold + FlyMs + TailMs,
        _ => UndoTotal,
    };

    public AreasResult? Result { get; private set; }

    /// <inheritdoc/>
    public void SetResult(AreasResult r, long elapsedMs)
    {
        Result = r;
        if (op == StashOp.Pop)
            TotalMs = PopHold + FlightMs(r.Arrived?.Count ?? 0) + TailMs;
        // A success continues from the hold point; a failure jumps straight to the end (the failure frame).
        _offset = r.Success ? Math.Max(0, elapsedMs - HoldMs) : elapsedMs - TotalMs;
    }

    /// <inheritdoc/>
    public SceneFrame Final(ArtGeometry g) => Done(g);

    /// <inheritdoc/>
    public bool IsDone(long elapsedMs) => Result is not null && TimeOf(elapsedMs) >= TotalMs;

    /// <inheritdoc/>
    public string Title(long elapsedMs)
    {
        if (IsDone(elapsedMs))
            return Result!.Title;
        return op switch
        {
            StashOp.Discard => "Discarding",
            StashOp.Stash => "Stashing",
            StashOp.Pop => "Popping",
            _ => "Undoing",
        };
    }

    /// <inheritdoc/>
    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        if (IsDone(elapsedMs))
            return Done(g);

        var te = TimeOf(elapsedMs);
        var grid = op switch
        {
            StashOp.Discard => RenderDiscard(g, elapsedMs, te),
            StashOp.Stash => RenderStash(g, te),
            StashOp.Pop => RenderPop(g, elapsedMs, te),
            _ => AreasScenes.Static(g, before), // undo: the old areas, with the spinner, until the new ones land
        };
        return new SceneFrame(grid, Running(elapsedMs));
    }

    // Time the scene has reached: clamped to the hold point until the result arrives, then offset from the clock.
    private long TimeOf(long elapsedMs) => Result is null ? Math.Min(elapsedMs, HoldMs) : elapsedMs - _offset;

    // Total time of a flight of n tokens: the last one lands 700 ms after it starts, and the starts are 80 ms apart (at most six).
    private static long FlightMs(int n) => n <= 0 ? 0 : FlyMs + StaggerMs * (Math.Min(n, MaxStagger + 1) - 1);

    private Caption Running(long elapsedMs) =>
        Result is null && elapsedMs >= MergeAnimation.WaitAfterMs
            ? new Caption(MergeScenes.WaitingText, Warn)
            : new Caption($"{MergeScenes.Spin(elapsedMs)} {runningText}", Fg);

    private SceneFrame Done(ArtGeometry g)
    {
        var r = Result!;
        if (!r.Success)
        {
            var failed = AreasScenes.Static(g, before);
            MergeScenes.MessageOverlay(failed, r.Title, r.Message ?? r.Caption.Text);
            return new SceneFrame(failed, r.Caption);
        }
        return new SceneFrame(AreasScenes.Static(g, r.After), r.Caption);
    }

    // The discarded row leaves the working tree at once. Its letter blinks while git runs, then flies to the stash box and fades.
    // When the fade ends, the box shows the new count.
    private ArtGrid RenderDiscard(ArtGeometry g, long elapsedMs, long te)
    {
        if (te >= DiscardGone && Result is { } done)
            return AreasScenes.Static(g, done.After);

        var grid = AreasScenes.Static(g, before, new AreasOptions(HideUnstaged: PathsOf(ChangeSide.Unstaged)));
        if (moving.Count == 0)
            return grid;

        var L = AreasLayout.For(g.Width, g.Height);
        var row = moving[0];
        var from = L.WtPos(IndexOf(before.Unstaged, row.Path, 0));
        var to = L.StashPos;
        if (te < DiscardHold)
        {
            AreasScenes.FlyToken(grid, from, from, 0, row.Letter, row.Path, MergeScenes.Blink(elapsedMs, BadB, WarnB));
            return grid;
        }

        var p = MergeScenes.Ease((te - DiscardHold) / (double)(DiscardGone - DiscardHold));
        if (te < DiscardWarn)
            AreasScenes.FlyToken(grid, from, to, p, row.Letter, row.Path, Warn, bold: false, pathStyle: Fg);
        else
            AreasScenes.FlyToken(grid, from, to, p, row.Letter, row.Path, Dim, bold: false, pathStyle: Dim);
        return grid;
    }

    // Every row a stash takes leaves its box at once and flies to the stash box, 80 ms apart. The count stays at the old value.
    private ArtGrid RenderStash(ArtGeometry g, long te)
    {
        var L = AreasLayout.For(g.Width, g.Height);
        var grid = AreasScenes.Static(g, before, new AreasOptions(HideUnstaged: PathsOf(ChangeSide.Unstaged), HideStaged: PathsOf(ChangeSide.Staged)));

        for (var k = 0; k < moving.Count; k++)
        {
            var f = moving[k];
            var p = Math.Clamp((te - StaggerMs * Math.Min(k, MaxStagger)) / (double)FlyMs, 0, 1);
            if (p >= 1)
                continue;

            var from = f.Side == ChangeSide.Staged
                ? L.IxPos(IndexOf(before.Staged, f.Path, k))
                : L.WtPos(IndexOf(before.Unstaged, f.Path, k));
            AreasScenes.FlyToken(grid, from, L.StashPos, MergeScenes.Ease(p), f.Letter, f.Path, AreasScenes.LetterStyle(f));
        }
        return grid;
    }

    // While git runs, the stash box title blinks. Once the pop is known to have landed, each brought-back row flies from the
    // stash box to its place in the result and appears when it lands. Its own row is hidden until then.
    private ArtGrid RenderPop(ArtGeometry g, long elapsedMs, long te)
    {
        if (Result is not { } r)
            return AreasScenes.Static(g, before, new AreasOptions(StashTitle: MergeScenes.Blink(elapsedMs, IncB, FgB)));

        var L = AreasLayout.For(g.Width, g.Height);
        var arrived = r.Arrived ?? [];
        var inFlight = new List<(FileChange F, int K, double P)>();
        var hideUnstaged = new HashSet<string>();
        var hideStaged = new HashSet<string>();
        for (var k = 0; k < arrived.Count; k++)
        {
            var f = arrived[k];
            var p = Math.Clamp((te - PopHold - StaggerMs * Math.Min(k, MaxStagger)) / (double)FlyMs, 0, 1);
            if (p >= 1)
                continue;
            inFlight.Add((f, k, p));
            (f.Side == ChangeSide.Staged ? hideStaged : hideUnstaged).Add(f.Path);
        }

        var grid = AreasScenes.Static(g, r.After, new AreasOptions(HideUnstaged: hideUnstaged, HideStaged: hideStaged));
        foreach (var (f, k, p) in inFlight)
        {
            var to = f.Side == ChangeSide.Staged
                ? L.IxPos(IndexOf(r.After.Staged, f.Path, k))
                : L.WtPos(IndexOf(r.After.Unstaged, f.Path, k));
            AreasScenes.FlyToken(grid, L.StashPos, to, MergeScenes.Ease(p), f.Letter, f.Path, AreasScenes.LetterStyle(f));
        }
        return grid;
    }

    private IReadOnlySet<string> PathsOf(ChangeSide side) =>
        moving.Where(f => f.Side == side).Select(f => f.Path).ToHashSet();

    private static int IndexOf(IReadOnlyList<FileChange> list, string path, int fallback)
    {
        for (var j = 0; j < list.Count; j++)
            if (list[j].Path == path)
                return j;
        return fallback;
    }
}
