using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>One frame of the graph: the placement (its Height places the list) and the drawn grid.</summary>
public sealed record LaneFrame(LanePlacement Placement, ArtGrid Grid);

/// <summary>
/// g's animation on the lane graph, from the pre-merge graph to the card's still. Pure: a time goes in, a frame comes out. The times
/// are the mock's: a merge is 2.6 s, a fast-forward 2.3 s, a stop 2.8 s. From the end time on, the frame is the card's own graph.
/// </summary>
public sealed class LaneMergeAnimation
{
    public const long MergeTotal = 2600, MergePulseEnd = 700, MergeTrailEnd = 1500, MergeGhostEnd = 1900, MergeChipEnd = 2300;
    public const long FfTotal = 2300, FfPulseEnd = 600, FfLiftEnd = 1500, FfChipEnd = 1900;
    public const long ConflictTotal = 2800, ConflictPulseEnd = 500, ConflictTrailEnd = 1300, ConflictFlashEnd = 2000, ConflictRetreatEnd = 2600;
    public const long FlashMs = 140, SpinMs = 80;

    private readonly DigestSheetInput _input;

    public LaneMergeAnimation(DigestSheetInput input)
    {
        _input = input;
        Total = TotalFor(input.Card);
    }

    public DigestSheetInput Input => _input;

    /// <summary>Merged, FastForwarded and Stopped play; every other card has no animation.</summary>
    public static bool CanPlay(MergeCard card)
    {
        return card.Kind == DigestKind.Merged || card.Kind == DigestKind.FastForwarded || card.Kind == DigestKind.Stopped;
    }

    /// <summary>MergeTotal, FfTotal or ConflictTotal by the card's kind; 0 when the card cannot play.</summary>
    public long Total { get; }

    /// <summary>
    /// The graph at time t (ms) for a grid of gridWidth × budget rows. From Total on, and for a card that cannot play, it is the card's
    /// own graph, so the last frame is the still the user was looking at.
    /// </summary>
    public LaneFrame GraphAt(long t, int gridWidth, int budget, Glyphs glyphs)
    {
        var preview = Fit(PreviewGhost(_input.Card), gridWidth, budget);
        var done = Fit(DigestText.Ghost(_input.Card), gridWidth, budget);
        if (t >= Total || preview.Rows.Count == 0)
            return DoneFrame(done, glyphs);

        if (_input.Card.Kind == DigestKind.Merged)
            return MergeFrame(t, preview, done, glyphs);
        if (_input.Card.Kind == DigestKind.FastForwarded)
            return FastForwardFrame(t, preview, done, gridWidth, budget, glyphs);
        return ConflictFrame(t, preview, glyphs);
    }

    /// <summary>The ease in and out of a move: 0 at p = 0, 1 at p = 1, flat at both ends.</summary>
    internal static double Ease(double p)
    {
        return p * p * (3 - 2 * p);
    }

    /// <summary>How far time t is through the span from..to, clamped to 0..1.</summary>
    internal static double Phase(long t, long from, long to)
    {
        var p = (t - from) / (double)(to - from);
        if (p < 0)
            return 0;
        if (p > 1)
            return 1;
        return p;
    }

    /// <summary>How many of count cells a fraction q of the way along a path is drawn: rounded, halves away from zero.</summary>
    internal static int Steps(double q, int count)
    {
        return (int)Math.Round(q * count, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Which of count commits is pulsing at time t, of a pulse that ends at end: 0 first, then on. -1 when the pulse is over or there
    /// is nothing to pulse.
    /// </summary>
    internal static int PulseIndex(long t, long end, int count)
    {
        if (t >= end || count == 0)
            return -1;
        return (int)Math.Floor(Phase(t, 0, end) * count * 0.999);
    }

    private static long TotalFor(MergeCard card)
    {
        if (!CanPlay(card))
            return 0;
        if (card.Kind == DigestKind.Merged)
            return MergeTotal;
        if (card.Kind == DigestKind.FastForwarded)
            return FfTotal;
        return ConflictTotal;
    }

    // The ghost the preview of m draws for the card's kind: the merge, the fast-forward or the conflict (the dashed previews).
    private static GhostKind PreviewGhost(MergeCard card)
    {
        if (card.Kind == DigestKind.Merged)
            return GhostKind.Merge;
        if (card.Kind == DigestKind.FastForwarded)
            return GhostKind.FastForward;
        if (card.Kind == DigestKind.Stopped)
            return GhostKind.Conflict;
        return GhostKind.None;
    }

    // The card's own spacing rule, so no frame moves the graph or the list.
    private LanePlacement Fit(GhostKind kind, int gridWidth, int budget)
    {
        return PreviewSheets.FitGraph(_input.Graph, _input.Layout.Selected, kind, gridWidth, budget, Hash(), _input.Card.Verified);
    }

    private string Hash()
    {
        return _input.Card.Hash ?? "";
    }

    private LaneFrame DoneFrame(LanePlacement done, Glyphs glyphs)
    {
        return new LaneFrame(done, LaneScene.Render(done, glyphs, Hash()));
    }

    private LaneFrame MergeFrame(long t, LanePlacement preview, LanePlacement done, Glyphs glyphs)
    {
        if (t < MergePulseEnd)
        {
            var pulse = LaneScene.Render(preview, glyphs, Hash());
            DrawPulse(pulse, preview, glyphs, t, MergePulseEnd);
            return new LaneFrame(preview, pulse);
        }

        if (t < MergeTrailEnd)
        {
            var trail = LaneScene.Render(preview, glyphs, Hash());
            var q = Ease(Phase(t, MergePulseEnd, MergeTrailEnd));
            DrawTrail(trail, TrailPath(preview, glyphs), q, LaneScene.LaneStyle(preview, preview.TipLane), glyphs);
            DrawBaseRun(trail, preview, q, glyphs);
            return new LaneFrame(preview, trail);
        }

        return MergeEndFrame(t, done, glyphs);
    }

    // The ghost fills, its label is typed, the new hash is cleared while that happens, and the main chip is typed after the label.
    private LaneFrame MergeEndFrame(long t, LanePlacement done, Glyphs glyphs)
    {
        var grid = LaneScene.Render(done, glyphs, Hash());
        var p3 = Phase(t, MergeTrailEnd, MergeGhostEnd);
        var p4 = Phase(t, MergeGhostEnd, MergeChipEnd);

        SetGhost(grid, done, GhostFill(p3, glyphs), new Style(Ink.Mrg), Prefix(done.GhostLabel, p3), new Style(Ink.Fg));
        if (p3 < 1)
            ClearHash(grid, done, Hash());

        var chipKeep = (int)Math.Ceiling(p4 * LaneScene.ChipWidth(done));
        TrimChip(grid, done.GhostX + 2, done.YOf(0) + 1, chipKeep);
        return new LaneFrame(done, grid);
    }

    // The ghost's glyph while it fills: empty, half, then full.
    private static char GhostFill(double p, Glyphs glyphs)
    {
        if (p < 0.34)
            return glyphs.GhostEmpty;
        if (p < 0.67)
            return glyphs.GhostHalf;
        return glyphs.Dot;
    }

    private LaneFrame FastForwardFrame(long t, LanePlacement preview, LanePlacement done, int gridWidth, int budget, Glyphs glyphs)
    {
        if (t < FfPulseEnd)
        {
            var pulse = LaneScene.Render(preview, glyphs, Hash());
            DrawPulse(pulse, preview, glyphs, t, FfPulseEnd);
            return new LaneFrame(preview, pulse);
        }

        if (t < FfLiftEnd)
            return LiftFrame(t, preview, done, gridWidth, budget, glyphs);

        return FastForwardEndFrame(t, done, glyphs);
    }

    // The branch's lane slides up from its row onto the base row, drawn without dashes. Until the lane is known (-1), the dashed preview stays.
    private LaneFrame LiftFrame(long t, LanePlacement preview, LanePlacement done, int gridWidth, int budget, Glyphs glyphs)
    {
        var lane = done.LiftedLane;
        if (lane < 0)
            return new LaneFrame(preview, LaneScene.Render(preview, glyphs, Hash()));

        var baseY = preview.YOf(0);
        var laneY = preview.YOf(lane);
        var lift = Ease(Phase(t, FfPulseEnd, FfLiftEnd));
        var y = (int)Math.Round(laneY + (baseY - laneY) * lift, MidpointRounding.AwayFromZero);
        y = Math.Max(baseY + 1, y);

        var shifted = Fit(GhostKind.None, gridWidth, budget) with { ShiftLane = lane, ShiftY = y };
        return new LaneFrame(shifted, LaneScene.Render(shifted, glyphs, Hash()));
    }

    // The fast-forward's chip is typed after the lane has landed on the base row.
    private LaneFrame FastForwardEndFrame(long t, LanePlacement done, Glyphs glyphs)
    {
        var grid = LaneScene.Render(done, glyphs, Hash());
        if (t < FfChipEnd && done.TipColumn >= done.FirstColumn)
        {
            var p = Phase(t, FfLiftEnd, FfChipEnd);
            var chipX = done.XOf(done.TipColumn) + 2;
            TrimChip(grid, chipX, done.YOf(0), (int)Math.Ceiling(p * LaneScene.ChipWidth(done)));
        }

        return new LaneFrame(done, grid);
    }

    private LaneFrame ConflictFrame(long t, LanePlacement preview, Glyphs glyphs)
    {
        if (t < ConflictPulseEnd)
        {
            var pulse = LaneScene.Render(preview, glyphs, Hash());
            DrawPulse(pulse, preview, glyphs, t, ConflictPulseEnd);
            return new LaneFrame(preview, pulse);
        }

        var grid = LaneScene.Render(preview, glyphs, Hash());
        var bad = new Style(Ink.Bad);
        if (t < ConflictTrailEnd)
        {
            DrawTrail(grid, TrailPath(preview, glyphs), Ease(Phase(t, ConflictPulseEnd, ConflictTrailEnd)), bad, glyphs);
            return new LaneFrame(preview, grid);
        }

        if (t < ConflictFlashEnd)
        {
            // The ✕ blinks with the conflicting files beside it: on for one FlashMs, off for the next.
            DrawTrail(grid, TrailPath(preview, glyphs), 1, bad, glyphs);
            var on = ((t - ConflictTrailEnd) / FlashMs) % 2 == 0;
            var glyph = ' ';
            if (on)
                glyph = glyphs.Conflict;
            var phrase = DigestText.ConflictPhrase(_input.Card.ConflictFiles);
            SetGhost(grid, preview, glyph, new Style(Ink.Bad, true), phrase, bad);
            return new LaneFrame(preview, grid);
        }

        // The trail retreats while the label says what put the repo back.
        var retreat = 1 - Ease(Phase(t, ConflictFlashEnd, ConflictRetreatEnd));
        DrawTrail(grid, TrailPath(preview, glyphs), retreat, bad, glyphs);
        SetGhost(grid, preview, glyphs.Conflict, bad, RollbackText(), bad);
        return new LaneFrame(preview, grid);
    }

    // The command that put the repo back, or "no commit" when none ran.
    private string RollbackText()
    {
        if (_input.Card.Rollback is null)
            return "no commit";
        return _input.Card.Rollback;
    }

    // The travelling trail: the tip's route to the ghost (along its row, then up the ghost's column). Empty when the tip is not drawn.
    private static List<TrailCell> TrailPath(LanePlacement p, Glyphs g)
    {
        var path = new List<TrailCell>();
        if (p.TipLane <= 0 || !p.IsKept(p.TipLane) || p.TipColumn < 0)
            return path;

        var tx = p.GraphX - 1;
        if (p.TipColumn >= p.FirstColumn)
            tx = p.XOf(p.TipColumn);
        var ty = p.YOf(p.TipLane);
        var gx = p.GhostX;
        var my = p.YOf(0);

        for (var x = tx + 1; x <= gx; x++)
        {
            var ch = g.Line;
            if (x == gx)
                ch = g.MergeUp;
            path.Add(new TrailCell(x, ty, ch));
        }

        for (var v = ty - 1; v > my; v--)
            path.Add(new TrailCell(gx, v, g.VLine));

        return path;
    }

    // Draws the first n cells of the trail in style, where the cell is blank or a dashed or corner mark (other lines survive). The cell
    // after the last drawn one holds the travelling dot, while the trail is only part of the way.
    private static void DrawTrail(ArtGrid grid, List<TrailCell> path, double q, Style style, Glyphs g)
    {
        var n = Steps(q, path.Count);
        for (var i = 0; i < n; i++)
        {
            var cell = path[i];
            var ch = grid[cell.X, cell.Y].Ch;
            if (ch == ' ' || ch == g.DashH || ch == g.DashV || ch == g.MergeUp)
                grid.Put(cell.X, cell.Y, cell.Ch, style);
        }

        if (n > 0 && n < path.Count)
        {
            var head = path[n - 1];
            grid.Put(head.X, head.Y, g.Dot, new Style(Ink.Fg, true));
        }
    }

    // The base's dashed run to the ghost turns solid as the trail runs.
    private static void DrawBaseRun(ArtGrid grid, LanePlacement p, double q, Glyphs g)
    {
        var bx = LaneScene.LastX(p, p.Graph.Lanes[0]);
        var n = Steps(q, p.GhostX - bx - 1);
        var y = p.YOf(0);
        var style = LaneScene.LaneStyle(p, 0);
        for (var i = 1; i <= n; i++)
        {
            var x = bx + i;
            if (grid[x, y].Ch == g.DashH)
                grid.Put(x, y, g.Line, style);
        }
    }

    // The pulse: the selected branch's commits take turns, oldest first, as a bright dot with its hash written again above.
    private static void DrawPulse(ArtGrid grid, LanePlacement p, Glyphs g, long t, long end)
    {
        var lane = p.SelectedLane;
        if (lane <= 0 || !p.IsKept(lane))
            return;

        var shown = new List<LaneCommit>();
        foreach (var commit in p.Graph.Lanes[lane].Commits)
        {
            if (commit.Column >= p.FirstColumn)
                shown.Add(commit);
        }

        var i = PulseIndex(t, end, shown.Count);
        if (i < 0)
            return;

        var commitNow = shown[i];
        var x = p.XOf(commitNow.Column);
        var y = p.LineYOf(lane);
        var bright = new Style(Ink.Fg, true);
        grid.Put(x, y, g.Pulse, bright);

        if (p.HashWidth <= 0)
            return;

        var hash = commitNow.ShortHash;
        if (hash.Length > p.HashWidth)
            hash = hash.Substring(0, p.HashWidth);
        var hx = x - p.HashWidth / 2;
        grid.Text(hx, y - 1, hash, bright);
        for (var k = 0; k < hash.Length; k++)
            grid.SetBg(hx + k, y - 1, CellBg.Sel);
    }

    // Writes the ghost of the done graph again: its glyph, then its label typed (text), in the given styles. Nothing else on the row changes.
    private static void SetGhost(ArtGrid grid, LanePlacement p, char glyph, Style glyphStyle, string text, Style textStyle)
    {
        var my = p.YOf(0);
        grid.Clear(p.GhostX, p.Width - 1, my, my);
        if (glyph != ' ')
            grid.Put(p.GhostX, my, glyph, glyphStyle);

        var label = LaneFit.FitGhostLabel(text, p.Width, p.GhostX);
        if (label.Length > 0)
            grid.Text(p.GhostX + 2, my, label, textStyle);
    }

    // The first characters of text that are shown at fraction p: all of them at 1, none at 0.
    private static string Prefix(string text, double p)
    {
        var count = (int)Math.Ceiling(p * text.Length);
        if (count <= 0)
            return "";
        if (count >= text.Length)
            return text;
        return text.Substring(0, count);
    }

    // Blanks the new hash above the ghost, where the done graph draws it.
    private static void ClearHash(ArtGrid grid, LanePlacement p, string hash)
    {
        if (p.HashWidth <= 0 || hash.Length == 0)
            return;

        var my = p.YOf(0);
        var x = p.GhostX - p.HashWidth / 2;
        grid.Clear(x, x + p.HashWidth - 1, my - 1, my - 1);
    }

    // Blanks the chip's cells from keep on, while they are chip cells. Nothing happens when no chip was drawn.
    private static void TrimChip(ArtGrid grid, int x, int y, int keep)
    {
        var cx = x + keep;
        while (grid[cx, y].Bg == CellBg.Chip)
        {
            grid.Clear(cx, cx, y, y);
            cx++;
        }
    }

    // A cell of the trail: where it is, and the glyph that goes there.
    private readonly record struct TrailCell(int X, int Y, char Ch);
}
