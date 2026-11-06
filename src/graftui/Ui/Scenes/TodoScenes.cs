using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>How a run of the interactive rebase's scene ends: it plays to the end, stops at a row, aborts there, or stops before it plays.</summary>
public enum TodoEnd { Play, Stop, Abort, Halt }

/// <summary>
/// What the scene plays. Starts[j] is when row j starts its flight, in scene time; -1 for a row that is not played (a row before From, or
/// the skipped row). From is the first row the scene plays; SkipAt is the row a resume skips (-1 for none). End says how the run stops:
/// at StopAt (with the kind of stop in Stop), by an abort of the stop row, or to the end.
/// </summary>
public sealed record TodoRun(IReadOnlyList<long> Starts, int From, int SkipAt, TodoEnd End, int StopAt, PauseKind? Stop);

/// <summary>
/// The interactive rebase's scene. The current branch's commits (row A) are cut loose at 600 ms and fly down to the
/// new base (row B) in plan order: a pick, reword or edit lands in a new slot, a squash or fixup folds into the slot above, and a drop goes
/// up in ✕. A stop shows where it happened, and an abort crosses the stop row and flashes before it returns. Every frame is a function of
/// the run and the scene time, so the same inputs draw the same picture.
/// </summary>
public static class TodoScenes
{
    /// <summary>The time before the first flight: row A is held whole while git answers.</summary>
    public const long Cut = 600;

    /// <summary>One flight, and the gap before the next one.</summary>
    public const long Fly = 700, Gap = 140;

    /// <summary>The flash and the return of an aborted run.</summary>
    public const long Flash = 700, Ret = 500;

    private const long LandedMs = 260, FoldedMs = 320;

    private const int BlinkMs = 140;

    private static readonly Style Dim = new(Ink.Dim), Bad = new(Ink.Bad), BadB = new(Ink.Bad, true), FgB = new(Ink.Fg, true);
    private static readonly Style Mrg = new(Ink.Mrg), MrgB = new(Ink.Mrg, true), WarnB = new(Ink.Warn, true), OkB = new(Ink.Ok, true);
    private static readonly Style Inc = new(Ink.Inc), IncB = new(Ink.Inc, true), Fg = new(Ink.Fg);

    /// <summary>
    /// The run of a plan from <paramref name="from"/> on. Each row that is played starts after the one before it, with the gap between; the
    /// rows before <paramref name="from"/> and the skipped row have no flight.
    /// </summary>
    public static TodoRun Plan(IReadOnlyList<RebaseRow> rows, int from, int skipAt)
    {
        var starts = new long[rows.Count];
        var at = Cut;
        for (var j = 0; j < rows.Count; j++)
        {
            if (j < from || j == skipAt)
            {
                starts[j] = -1;
                continue;
            }
            starts[j] = at;
            at += Fly + Gap;
        }
        return new TodoRun(starts, from, skipAt, TodoEnd.Play, -1, null);
    }

    /// <summary>
    /// The run of an abort of the paused row <paramref name="stopAt"/>, which stopped as <paramref name="stop"/>: nothing flies, the row is
    /// drawn as its stop, and the abort's flash and return play out.
    /// </summary>
    public static TodoRun Aborting(TodoRun run, int stopAt, PauseKind stop) =>
        run with { End = TodoEnd.Abort, StopAt = stopAt, From = stopAt, SkipAt = -1, Stop = stop };

    /// <summary>
    /// The run once git has answered. A finished rebase plays to the end; a pause stops at its row; an abort keeps its run; anything else
    /// stops before it plays.
    /// </summary>
    public static TodoRun Ended(TodoRun run, IReadOnlyList<RebaseRow> rows, SequenceOutcome? o)
    {
        if (run.End == TodoEnd.Abort)
            return run;
        if (o is not null && o.Kind == SequenceOutcomeKind.Done)
            return run with { End = TodoEnd.Play };
        if (o is not null && o.Kind == SequenceOutcomeKind.Paused && o.Stop is not null)
        {
            var at = StopIndex(rows, o.Stop);
            if (at >= 0 && run.Starts[at] >= 0)
                return run with { End = TodoEnd.Stop, StopAt = at, Stop = o.Stop.Pause };
        }
        return run with { End = TodoEnd.Halt };
    }

    /// <summary>The scene time the run has reached when it is over: its last flight's landing, or the stop's landing, or the abort's return.</summary>
    public static long Total(TodoRun run)
    {
        if (run.End == TodoEnd.Abort)
            return Flash + Ret;
        if (run.End == TodoEnd.Halt)
            return Cut;
        if (run.End == TodoEnd.Stop)
            return run.Starts[run.StopAt] + Fly;

        var end = Cut;
        foreach (var start in run.Starts.Where(s => s >= 0))
            end = Math.Max(end, start + Fly);
        return end;
    }

    /// <summary>The scene time the run holds at until git answers: row A whole, or, for an abort, the crossed row at once.</summary>
    public static long Hold(TodoRun run) => run.End == TodoEnd.Abort ? 0 : Cut;

    /// <summary>
    /// The plan row a stop is at: the stopped commit, or for a squash the last of its folds (the stop comes after the group). -1 when the
    /// stop names no row of the plan.
    /// </summary>
    public static int StopIndex(IReadOnlyList<RebaseRow> rows, SequenceStop stop)
    {
        if (stop.Pause == PauseKind.Squash)
        {
            var last = -1;
            for (var j = 0; j < rows.Count; j++)
            {
                if (stop.Folded.Contains(rows[j].Hash))
                    last = j;
            }
            if (last >= 0)
                return last;
        }
        for (var j = 0; j < rows.Count; j++)
        {
            if (rows[j].Hash == stop.Commit)
                return j;
        }
        return -1;
    }

    /// <summary>
    /// The first row a resume plays. A conflict's commit is picked again (it is continued or skipped), and an abort starts at the stopped
    /// row; an edit, reword or squash has landed already, so the rows after its stop are the ones that fly.
    /// </summary>
    public static int ResumeFrom(IReadOnlyList<RebaseRow> rows, SequenceStop stop, ResumeAction action)
    {
        var at = StopIndex(rows, stop);
        if (at < 0)
            return Math.Clamp(stop.Step - 1, 0, rows.Count);
        if (action == ResumeAction.Skip || action == ResumeAction.Abort || stop.Pause == PauseKind.Conflict)
            return at;
        return at + 1;
    }

    /// <summary>
    /// The slot each row lands in on the new base: a kept row takes the next slot, a fold takes the slot above it, and a drop has none
    /// (-1). A fold with nothing kept above it is -1 as well (the engine refuses such a plan).
    /// </summary>
    public static IReadOnlyList<int> SlotsOf(IReadOnlyList<RebaseRow> rows)
    {
        var slots = new int[rows.Count];
        var kept = 0;
        for (var j = 0; j < rows.Count; j++)
        {
            var action = rows[j].Action;
            if (action == RebaseAction.Drop)
                slots[j] = -1;
            else if (RebasePlan.IsFold(action))
                slots[j] = kept - 1;
            else
                slots[j] = kept++;
        }
        return slots;
    }

    /// <summary>
    /// Draws the frame at scene time <paramref name="te"/> (clamped to the hold point by the caller). <paramref name="t"/> is the animation
    /// clock, for the spinner and the blinks; <paramref name="waiting"/> swaps the caption for the wait text.
    /// </summary>
    public static SceneFrame Rebase(ArtGeometry g, GraphLayout L, IReadOnlyList<RebaseRow> rows, TodoRun run, long te, long t, bool waiting)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        var slots = SlotsOf(rows);
        var curInk = BranchInks.Current(L);
        var cur = new Style(curInk);
        var curB = new Style(curInk, true);
        var detached = te >= Cut && run.End != TodoEnd.Abort;
        var sxTip = g.TipX(L.SelectedOwn.Count);

        MergeScenes.DrawShared(grid, g, L);
        MergeScenes.DrawRowB(grid, g, L.SelectedOwn, new Style(BranchInks.Selected(L)), L.SelectedOwnMore);
        DrawRowA(grid, g, rows, run, te, detached ? Dim : cur, curB);
        if (run.End != TodoEnd.Abort && te < Cut)
            grid.Put(g.XB + 3, g.A, '✂', MergeScenes.Blink(te, WarnB, cur, BlinkMs));

        var flying = Flying(run, te);
        DrawLandings(grid, g, rows, slots, run, te, sxTip, cur, curB);

        var spots = MergeScenes.LabelSpots(g, L);
        MergeScenes.CurLabel(grid, g, spots.CurX, spots.CurRow, L);
        MergeScenes.SelLabel(grid, g, spots.SelX, spots.SelRow, L);
        return new SceneFrame(grid, CaptionAt(L, rows, run, te, t, waiting, flying));
    }

    // Row A: the current branch's commits, up to the ones the art has room for. A commit that has flown (or has been skipped) is a dim ghost.
    private static void DrawRowA(ArtGrid grid, ArtGeometry g, IReadOnlyList<RebaseRow> rows, TodoRun run, long te, Style line, Style cur)
    {
        var n = Math.Min(rows.Count, Visible(g));
        var px = g.XB;
        for (var j = 0; j < n; j++)
        {
            var x = g.TipX(j + 1);
            grid.HLine(px + 1, x - 1, g.A, line);
            DrawRowADot(grid, g, rows[j], j, x, run, te, cur);
            px = x;
        }
    }

    private static void DrawRowADot(ArtGrid grid, ArtGeometry g, RebaseRow row, int j, int x, TodoRun run, long te, Style cur)
    {
        if (j == run.SkipAt)
        {
            grid.Put(x, g.A, '✕', Dim);
            return;
        }

        if (IsMoved(run, j, te))
        {
            if (row.Action != RebaseAction.Drop)
            {
                grid.Put(x, g.A, '◌', Dim);
                return;
            }
            // A dropped commit blinks while its flight is on, then stays a dim ✕.
            var flying = run.Starts[j] >= 0 && te < run.Starts[j] + Fly;
            grid.Put(x, g.A, '✕', flying ? MergeScenes.Blink(te, Bad, Dim) : Dim);
            return;
        }

        if (row.Action == RebaseAction.Drop)
            grid.Put(x, g.A, '✕', Bad);
        else if (RebasePlan.IsFold(row.Action))
            grid.Put(x, g.A, '●', MrgB);
        else if (row.Action == RebaseAction.Reword || row.Action == RebaseAction.Edit)
            grid.Put(x, g.A, '●', WarnB);
        else
            grid.Put(x, g.A, '●', cur);
    }

    // Whether row j has left row A by scene time te: it is before the first played row, it is the skipped row, or its flight has started.
    private static bool IsMoved(TodoRun run, int j, long te)
    {
        if (j < run.From || j == run.SkipAt)
            return true;
        if (run.End == TodoEnd.Abort)
            return j <= run.StopAt;
        var start = run.Starts[j];
        return start >= 0 && te >= start;
    }

    // The row whose flight is on at te, or -1.
    private static int Flying(TodoRun run, long te)
    {
        for (var j = 0; j < run.Starts.Count; j++)
        {
            var start = run.Starts[j];
            if (start >= 0 && te >= start && te < start + Fly)
                return j;
        }
        return -1;
    }

    // Row B: each commit that has landed, in its slot, then the flight on its way and the stop, if the run has one.
    private static void DrawLandings(ArtGrid grid, ArtGeometry g, IReadOnlyList<RebaseRow> rows, IReadOnlyList<int> slots, TodoRun run,
        long te, int sxTip, Style cur, Style curB)
    {
        for (var j = 0; j < rows.Count; j++)
        {
            var slot = slots[j];
            if (slot < 0 || j == run.SkipAt)
                continue;

            var row = rows[j];
            var dest = Dest(g, sxTip, slot);
            var from = Dest(g, sxTip, slot - 1);
            var isFold = RebasePlan.IsFold(row.Action);

            if (run.End == TodoEnd.Abort && j == run.StopAt)
            {
                DrawStop(grid, g, run, row, from, dest, te, cur);
                continue;
            }

            // A commit before the first played row landed before the pause: it is drawn in its slot, with no flight and no bright dot.
            if (j < run.From)
            {
                if (!isFold)
                    DrawLanded(grid, g, from, dest, cur, curB, bright: false);
                continue;
            }

            var start = run.Starts[j];
            if (start < 0 || te < start)
                continue;

            if (te < start + Fly)
            {
                DrawFlight(grid, g, j, dest, start, te);
                continue;
            }

            if (run.End == TodoEnd.Stop && j == run.StopAt)
            {
                DrawStop(grid, g, run, row, from, dest, te, cur);
                continue;
            }

            var since = te - (start + Fly);
            if (isFold)
                grid.Put(dest, g.B, '●', since < FoldedMs ? MrgB : curB);
            else
                DrawLanded(grid, g, from, dest, cur, curB, bright: since < LandedMs);
        }
    }

    // The slot's x on row B: the base's tip, then one step per slot. Slot -1 is the base's tip itself.
    private static int Dest(ArtGeometry g, int sxTip, int slot) => sxTip + g.Step * (slot + 1);

    // A landed commit: the line from the slot before, then its dot, bright for a moment after it lands.
    private static void DrawLanded(ArtGrid grid, ArtGeometry g, int from, int dest, Style cur, Style curB, bool bright)
    {
        grid.HLine(from + 1, dest - 1, g.B, cur);
        grid.Put(dest, g.B, '●', bright ? OkB : curB);
    }

    // The flight of one row: a dot on its arc from its commit on row A to its slot on row B.
    private static void DrawFlight(ArtGrid grid, ArtGeometry g, int j, int dest, long start, long te)
    {
        var sx = g.TipX(Math.Min(j + 1, Visible(g)));
        Arc(grid, sx, g.A, dest, g.B, MergeScenes.Ease((te - start) / (double)Fly));
    }

    // The stop: a conflict is a cross one cell short of its slot (an abort flashes it while it returns); an edit, a reword or a squash
    // lands warm in its slot.
    private static void DrawStop(ArtGrid grid, ArtGeometry g, TodoRun run, RebaseRow row, int from, int dest, long te, Style cur)
    {
        if (run.Stop != PauseKind.Conflict)
        {
            grid.HLine(from + 1, dest - 1, g.B, cur);
            grid.Put(dest, g.B, '●', WarnB);
            return;
        }

        grid.HLine(from + 1, dest - 1, g.B, Bad, '━');
        if (run.End == TodoEnd.Abort)
            grid.Put(dest, g.B, '✕', te < Flash ? MergeScenes.Blink(te, BadB, FgB) : Bad);
        else
            grid.Put(dest, g.B, '✕', BadB);
    }

    // The arc of a flight: a dot with a dim and a bright trail behind it, the arc lifting by 2.4 rows at its middle.
    private static void Arc(ArtGrid grid, int sx, int sy, int dx, int dy, double p)
    {
        var at = Position(sx, sy, dx, dy, p);
        var trail = Position(sx, sy, dx, dy, Math.Max(0, p - 0.12));
        var far = Position(sx, sy, dx, dy, Math.Max(0, p - 0.24));
        if (far != trail)
            grid.Put(far.X, far.Y, '·', Dim);
        if (trail != at)
            grid.Put(trail.X, trail.Y, '·', Inc);
        grid.Put(at.X, at.Y, '●', IncB);
    }

    private static (int X, int Y) Position(int sx, int sy, int dx, int dy, double q)
    {
        var x = Round(sx + (dx - sx) * q);
        var y = Math.Max(0, Round(sy + (dy - sy) * q - 2.4 * Math.Sin(Math.PI * q)));
        return (x, y);
    }

    /// <summary>The caption in flight: the abort's words, a wait while git is slow, the flight's own words, else the running line.</summary>
    private static Caption CaptionAt(GraphLayout L, IReadOnlyList<RebaseRow> rows, TodoRun run, long te, long t, bool waiting, int flying)
    {
        if (run.End == TodoEnd.Abort)
            return new Caption("✕ " + RebaseText.Aborting, Bad);
        if (waiting)
            return MergeScenes.Running(true, "");
        var running = $"{MergeScenes.Spin(t)} {RebaseText.Running(L.Current, L.Selected)}";
        if (flying < 0 || te < Cut)
            return new Caption(running, Fg);

        var row = rows[flying];
        var n = rows.Count;
        if (row.Action == RebaseAction.Drop)
            return new Caption($"{MergeScenes.Spin(t)} {RebaseText.Dropping(row)}", Bad);
        if (RebasePlan.IsFold(row.Action))
            return new Caption($"{MergeScenes.Spin(t)} {RebaseText.Squashing(row, flying + 1, n)}", Mrg);
        return new Caption($"{MergeScenes.Spin(t)} {RebaseText.Replaying(row, flying + 1, n)}", Fg);
    }

    // The most commits row A has room for: the commits between the base and the right edge.
    private static int Visible(ArtGeometry g) => Math.Max(1, (g.Width - 1 - g.XB) / g.Step);

    private static int Round(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);
}
