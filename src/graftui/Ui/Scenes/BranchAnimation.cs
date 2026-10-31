using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>Which branch change the animation shows.</summary>
public enum BranchOp { Switch, Create, Rename, Delete }

/// <summary>
/// Drives one branch change's animation (the mock's switchTo, createBranch, doRename and doDelete). Like <see cref="StashAnimation"/>,
/// the scene is held at its hold point until git answers, then plays on from there; a failure jumps straight to its final frame.
/// Before the result, the spinner caption uses the real elapsed time. A switch is the exception: its graph never moves, only the
/// "(current)" marker fades from the old label to the new one (see <see cref="SwitchScene"/>).
/// </summary>
public sealed class BranchAnimation(BranchOp op, GraphLayout before, string name, string? newName, int lost, string runningText) : IBranchAnimation
{
    // Switch: the timeline starts when git answers (hold 0), so a failed switch never moves the marker. "(current)" fades off the old
    // label over SwitchFade ms and onto the new one over the next SwitchFade ms; the result's caption appears at SwitchCaptionAt.
    private const long SwitchHold = 0, SwitchTotal = 600, SwitchFade = 250, SwitchCaptionAt = 500;
    private const long CreateHold = 700, CreateTotal = 900;
    private const long RenameHold = 700, RenameTotal = 900;
    private const long DeleteHold = 300, DeleteTotal = 1000;

    // Delete: the grey ghosts of the commits no other branch holds appear at 450 ms; the name is bold until 300, plain until 600, Dim until 900.
    private const long GhostAt = 450, NameBright = 300, NameInc = 600, NameDim = 900;

    private static readonly Style Fg = new(Ink.Fg), Warn = new(Ink.Warn);
    private static readonly Style Dim = new(Ink.Dim), Faint = new(Ink.Frame);
    private static readonly Style IncB = new(Ink.Inc, true);
    private static readonly Style OkB = new(Ink.Ok, true);

    private long _offset;

    public long HoldMs { get; } = op switch
    {
        BranchOp.Switch => SwitchHold,
        BranchOp.Create => CreateHold,
        BranchOp.Rename => RenameHold,
        _ => DeleteHold,
    };

    public long TotalMs { get; } = op switch
    {
        BranchOp.Switch => SwitchTotal,
        BranchOp.Create => CreateTotal,
        BranchOp.Rename => RenameTotal,
        _ => DeleteTotal,
    };

    public BranchResult? Result { get; private set; }

    /// <inheritdoc/>
    public void SetResult(BranchResult r, long elapsedMs)
    {
        Result = r;
        // A success continues from the hold point; a failure jumps straight to the end (its final frame).
        _offset = r.Success ? Math.Max(0, elapsedMs - HoldMs) : elapsedMs - TotalMs;
    }

    /// <inheritdoc/>
    public bool IsDone(long elapsedMs) => Result is not null && TimeOf(elapsedMs) >= TotalMs;

    /// <inheritdoc/>
    public string Title(long elapsedMs)
    {
        if (IsDone(elapsedMs))
            return Result!.Title;
        // A switch's title changes with its caption, so nothing changes when the animation ends.
        if (op == BranchOp.Switch && Result is { } r && TimeOf(elapsedMs) >= SwitchCaptionAt)
            return r.Title;
        return op switch
        {
            BranchOp.Switch => "Switching",
            BranchOp.Create => "New branch",
            BranchOp.Rename => "Renaming",
            _ => "Deleting",
        };
    }

    /// <inheritdoc/>
    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        if (IsDone(elapsedMs))
            return Done(g);

        var te = TimeOf(elapsedMs);
        var grid = Scene(g, te, elapsedMs);
        // A switch's caption appears at SwitchCaptionAt; until then the running caption stays.
        var caption = op == BranchOp.Switch && Result is { } r && te >= SwitchCaptionAt ? r.Caption : Running(elapsedMs);
        return new SceneFrame(grid, caption);
    }

    // Time the scene has reached: clamped to the hold point until the result arrives, then offset from the clock.
    private long TimeOf(long elapsedMs) => Result is null ? Math.Min(elapsedMs, HoldMs) : elapsedMs - _offset;

    private Caption Running(long elapsedMs) =>
        Result is null && elapsedMs >= MergeAnimation.WaitAfterMs
            ? new Caption(MergeScenes.WaitingText, Warn)
            : new Caption($"{MergeScenes.Spin(elapsedMs)} {runningText}", Fg);

    private SceneFrame Done(ArtGeometry g)
    {
        var r = Result!;
        var grid = DoneGrid(g, r);
        if (r.Overlay is not null)
            MergeScenes.MessageOverlay(grid, r.OverlayTitle ?? r.Title, r.Overlay);
        return new SceneFrame(grid, r.Caption);
    }

    private ArtGrid Scene(ArtGeometry g, long te, long elapsedMs) => op switch
    {
        BranchOp.Switch => SwitchScene(g, te),
        BranchOp.Create => CreateScene(g, te, elapsedMs),
        BranchOp.Rename => RenameScene(g, te),
        _ => DeleteScene(g, te),
    };

    // The "(current)" marker moves from the old label to the new one as two fades. The graph and the label places are the preview's:
    // only the two labels' marker, name and " (current)" are written over it, so nothing else on the grid changes in any frame.
    // Each label keeps its branch's colour in every frame; only its " (current)" fades (three steps per fade, the last one the resting
    // style, all from the palette: no colour mixing).
    private ArtGrid SwitchScene(ArtGeometry g, long te)
    {
        var grid = MergeScenes.Preview(g, before, new PreviewOptions(NoCurrentExtra: true, NoCurName: true, NoSelName: true)).Grid;
        var s = MergeScenes.LabelSpots(g, before);

        var oldT = Math.Clamp(te, 0, SwitchFade);
        var newT = Math.Clamp(te - SwitchFade, 0, SwitchFade);

        // The old label keeps the current branch's colour; " (current)" fades out. Its name is placed with the extra from the start,
        // as the preview places a current label.
        var oldInk = BranchInks.Current(before);
        var oldExtra = before.Detached ? " (detached)" : " (current)";
        LabelAt(grid, g, s.CurX, s.CurRow, up: true, CurrentText(before), oldExtra, reserveExtra: true,
            new Style(oldInk), new Style(oldInk, true), Step<Style?>(oldT, Dim, Faint, null));

        // The new label keeps the selected branch's colour; " (current)" fades in. Its name keeps the place the preview gives a plain
        // label until the fade-in starts, so a switch that fails (or waits on git) never moves it.
        var newInk = BranchInks.Selected(before);
        LabelAt(grid, g, s.SelX, s.SelRow, up: false, name, " (current)", reserveExtra: newT > 0,
            new Style(newInk), new Style(newInk, true), Step<Style?>(newT, null, Faint, Dim));
        return grid;
    }

    // One label as the preview places it: the marker on the branch line, the name on its row, and the extra after the name. With
    // reserveExtra the name's place counts the extra when it fits (the preview's rule for a current label); without it, the name's place
    // is the plain label's. The name is never moved by the extra's fade alone.
    private static void LabelAt(ArtGrid grid, ArtGeometry g, int x, int row, bool up, string text, string extra, bool reserveExtra,
        Style markerStyle, Style nameStyle, Style? extraStyle)
    {
        grid.Put(x, up ? row - 1 : row + 1, up ? '▾' : '▴', markerStyle);

        var shown = MergeScenes.Runes(text) > g.Width ? TextFit.Fit(text, g.Width) : text;
        var nameLen = MergeScenes.Runes(shown);
        var extraLen = MergeScenes.Runes(extra);
        var fits = nameLen + extraLen <= g.Width;
        var tx = MergeScenes.LabelTextX(g, x, nameLen + (fits && reserveExtra ? extraLen : 0));
        var ty = up ? row - 2 : row + 2;

        grid.Text(tx, ty, shown, nameStyle);
        if (fits && extraStyle is { } st)
            grid.Text(tx + nameLen, ty, extra, st);
    }

    /// <summary>The picture once the change is done: the branch before it when it failed, the branch after it when it moved, else the finished scene.</summary>
    private ArtGrid DoneGrid(ArtGeometry g, BranchResult r)
    {
        if (!r.Success)
            return MergeScenes.Preview(g, before).Grid;
        if (r.After is not null)
            return MergeScenes.Preview(g, r.After).Grid;
        return Scene(g, TotalMs, TotalMs);
    }

    // The value at the fade's time: the first style until a third of the fade, the second until two thirds, then the third.
    private static T Step<T>(long t, T first, T second, T last)
    {
        if (t < SwitchFade / 3)
            return first;
        if (t < SwitchFade * 2 / 3)
            return second;
        return last;
    }

    /// <summary>The colour of a name as it fades out: bold in its branch's colour, then plain in it, then dim, then none.</summary>
    private static Style? NameStyleAt(long te, Ink ink)
    {
        if (te < NameBright)
            return new Style(ink, true);
        if (te < NameInc)
            return new Style(ink);
        if (te < NameDim)
            return Dim;
        return null;
    }

    // The new branch sprouts from the current one: a marker blinks under the current label while the name is typed out.
    private ArtGrid CreateScene(ArtGeometry g, long te, long elapsedMs)
    {
        var grid = MergeScenes.Preview(g, before).Grid;
        grid.Put(g.XB, g.A + 1, '▴', MergeScenes.Blink(elapsedMs, OkB, IncB));

        var cells = Cells(name);
        var k = Math.Clamp(Round(Math.Clamp(te / (double)CreateHold, 0, 1) * cells.Length), 0, cells.Length);
        var typed = string.Concat(cells.Take(k));
        if (typed.Length > 0)
            grid.Text(MergeScenes.LabelTextX(g, g.XB, cells.Length), g.A + 2, typed, OkB);
        return grid;
    }

    // The old name becomes the new one letter by letter: the new letters come from the left, the old ones are left at the right.
    private ArtGrid RenameScene(ArtGeometry g, long te)
    {
        var renamingCurrent = before.Selected == before.Current;
        var grid = MergeScenes.Preview(g, before,
            renamingCurrent ? new PreviewOptions(NoCurName: true) : new PreviewOptions(NoSelName: true)).Grid;
        var s = MergeScenes.LabelSpots(g, before);

        var nu = newName ?? name;
        var oldCells = Cells(name);
        var newCells = Cells(nu);
        var longer = Math.Max(oldCells.Length, newCells.Length);
        var k = Math.Clamp(Round(Math.Clamp(te / (double)RenameHold, 0, 1) * longer), 0, longer);
        var shown = string.Concat(newCells.Take(Math.Min(k, newCells.Length))) + string.Concat(oldCells.Skip(Math.Min(k, oldCells.Length)));
        shown += new string(' ', Math.Max(0, longer - MergeScenes.Runes(shown)));

        // The current label gets its " (current)" after the name; the trailing blanks of the padding are where it goes.
        var core = shown.TrimEnd();
        var extra = renamingCurrent ? " (current)" : null;
        var x = renamingCurrent ? s.CurX : s.SelX;
        var row = renamingCurrent ? s.CurRow - 2 : s.SelRow + 2;
        var tx = MergeScenes.LabelTextX(g, x, Math.Max(longer, MergeScenes.Runes(core) + MergeScenes.Runes(extra ?? "")));
        grid.Text(tx, row, shown, OkB);
        if (extra is not null)
            grid.Text(tx + MergeScenes.Runes(core), row, extra, Dim);
        return grid;
    }

    // The selected branch's name fades out at its spot, and its ghosts (the commits only it held) appear on its line.
    private ArtGrid DeleteScene(ArtGeometry g, long te)
    {
        var ghosts = te > GhostAt ? lost : 0;
        var grid = MergeScenes.Preview(g, before, new PreviewOptions(NoSelName: true, GhostTail: ghosts)).Grid;
        var s = MergeScenes.LabelSpots(g, before);

        var shown = MergeScenes.Runes(name) > g.Width ? TextFit.Fit(name, g.Width) : name;
        var len = MergeScenes.Runes(shown);
        var tx = MergeScenes.LabelTextX(g, s.SelX, len);
        var style = NameStyleAt(te, BranchInks.Selected(before));
        if (style is { } st)
        {
            grid.Put(s.SelX, s.SelRow + 1, '▴', st);
            grid.Text(tx, s.SelRow + 2, shown, st);
        }
        else
        {
            grid.Put(s.SelX, s.SelRow + 1, ' ', Dim);
            grid.Text(tx, s.SelRow + 2, new string(' ', len), Dim);
        }
        return grid;
    }

    private static string CurrentText(GraphLayout L) => L.Detached ? "HEAD" : L.Current;

    private static string[] Cells(string s) => s.EnumerateRunes().Select(r => r.ToString()).ToArray();

    private static int Round(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);
}
