using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>Which change the three-areas animation shows: a stage or unstage, or a commit or an amend.</summary>
public enum AreasOp { Stage, Unstage, Commit, Amend }

/// <summary>
/// What git returned, as the animation shows it. After is the areas once the change landed (ignored on failure).
/// Message is the failure text in the overlay; null shows the caption's text instead. Arrived is what a pop brought back:
/// the rows of After that were not there before, each flying in from the stash (ignored by the other changes).
/// </summary>
public sealed record AreasResult(bool Success, AreasModel After, Caption Caption, string Title, string? Message = null,
    IReadOnlyList<FileChange>? Arrived = null);

/// <summary>
/// Drives one change's animation (the mock's stage, unstage and doCommit). Like <see cref="MergeAnimation"/>, the scene is
/// held at its hold point until git answers; then it plays out from there. A failure jumps straight to the final frame.
/// </summary>
public sealed class ChangesAnimation(AreasOp op, AreasModel before, IReadOnlyList<FileChange> moving, string runningText) : IAreasAnimation
{
    private const long MoveFly = 600, MoveStep = 90, MoveTail = 50;
    private const long CommitFly = 550, CommitBlink = 250, CommitDrop = 650, CommitTail = 250, CommitLanded = 120;

    private static readonly Style Ok = new(Ink.Ok), OkB = new(Ink.Ok, true);
    private static readonly Style CurB = new(Ink.Cur, true);
    private static readonly Style MrgB = new(Ink.Mrg, true);
    private static readonly Style Fg = new(Ink.Fg), Warn = new(Ink.Warn);

    // Commit timing: T1 the tokens fly in, T2 the dot blinks ("git is working"), T3 the dot drops onto the line.
    private readonly long _t1 = moving.Count > 0 ? CommitFly : 0;
    private long _offset;

    public long HoldMs { get; } = op is AreasOp.Commit or AreasOp.Amend
        ? (moving.Count > 0 ? CommitFly : 0) + CommitBlink
        : MoveFly + MoveStep * (Math.Max(1, Math.Min(moving.Count, 4)) - 1);

    public long TotalMs { get; } = op is AreasOp.Commit or AreasOp.Amend
        ? (moving.Count > 0 ? CommitFly : 0) + CommitBlink + CommitDrop + CommitTail
        : MoveFly + MoveStep * (Math.Max(1, Math.Min(moving.Count, 4)) - 1) + MoveTail;

    public AreasResult? Result { get; private set; }

    private bool IsCommit => op is AreasOp.Commit or AreasOp.Amend;

    /// <inheritdoc/>
    public void SetResult(AreasResult r, long elapsedMs)
    {
        Result = r;
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
            AreasOp.Stage => "Staging",
            AreasOp.Unstage => "Unstaging",
            AreasOp.Commit => "Committing",
            _ => "Amending",
        };
    }

    /// <inheritdoc/>
    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        if (IsDone(elapsedMs))
            return Done(g);

        var te = TimeOf(elapsedMs);
        var grid = IsCommit ? RenderCommit(g, elapsedMs, te) : RenderMove(g, elapsedMs, te);
        return new SceneFrame(grid, Running(elapsedMs));
    }

    // Time the scene has reached: clamped to the hold point until the result arrives, then offset from the clock.
    private long TimeOf(long elapsedMs) => Result is null ? Math.Min(elapsedMs, HoldMs) : elapsedMs - _offset;

    private Caption Running(long e) =>
        Result is null && e >= MergeAnimation.WaitAfterMs
            ? new Caption(MergeScenes.WaitingText, Warn)
            : new Caption($"{MergeScenes.Spin(e)} {runningText}", Fg);

    private SceneFrame Done(ArtGeometry g)
    {
        var r = Result!;
        if (!r.Success)
        {
            var failed = AreasScenes.Static(g, before);
            MergeScenes.MessageOverlay(failed, "Failed", r.Message ?? r.Caption.Text);
            return new SceneFrame(failed, r.Caption);
        }

        var ghost = op == AreasOp.Amend ? (int?)AreasLayout.For(g.Width, g.Height).SlotX(LineBefore().Count - 1) : null;
        return new SceneFrame(AreasScenes.Static(g, r.After, new AreasOptions(GhostX: ghost)), r.Caption);
    }

    // Stage and unstage: each moving file flies across the boxes, four at most, 90 ms apart.
    private ArtGrid RenderMove(ArtGeometry g, long e, long te)
    {
        var L = AreasLayout.For(g.Width, g.Height);
        var hide = moving.Select(f => f.Path).ToHashSet();
        var grid = AreasScenes.Static(g, before, new AreasOptions(HideUnstaged: hide, HideStaged: hide));

        var stage = op == AreasOp.Stage;
        var source = stage ? before.Unstaged : before.Staged;
        var destinationCount = stage ? before.Staged.Count : before.Unstaged.Count;
        var tokens = Math.Min(moving.Count, 4);
        for (var i = 0; i < tokens; i++)
        {
            var f = moving[i];
            var p = Math.Clamp((te - MoveStep * i) / (double)MoveFly, 0, 1);
            var from = stage ? L.WtPos(IndexOf(source, f.Path, i)) : L.IxPos(IndexOf(source, f.Path, i));
            var to = stage ? L.IxPos(destinationCount + i) : L.WtPos(destinationCount + i);
            if (stage)
                AreasScenes.FlyToken(grid, from, to, MergeScenes.Ease(p), f.LetterAfterStage, f.Path, Ok);
            else
                AreasScenes.FlyToken(grid, from, to, MergeScenes.Ease(p), f.LetterAfterUnstage, f.Path,
                    AreasScenes.LetterStyleOf(f.LetterAfterUnstage));
        }
        return grid;
    }

    // Commit and amend: the staged files fly to the repository, gather into one dot, which blinks, then drops onto the line.
    private ArtGrid RenderCommit(ArtGeometry g, long e, long te)
    {
        var L = AreasLayout.For(g.Width, g.Height);
        var line = LineBefore();
        var slot = op == AreasOp.Amend ? line.Count - 1 : line.Count;
        var t2End = _t1 + CommitBlink;
        var t3End = t2End + CommitDrop;

        if (te >= t3End)
        {
            // Landed: the commit is on the line; the dot is current-coloured for the last 120 ms.
            var ghost = op == AreasOp.Amend ? (int?)L.SlotX(slot) : null;
            var landed = AreasScenes.Static(g, Result!.After, new AreasOptions(GhostX: ghost));
            landed.Put(L.SlotX(slot), L.LineY, '●', te < TotalMs - CommitLanded ? OkB : CurB);
            return landed;
        }

        // While git works the scene stays in the blink (it is held at t2End). Only a landed result moves the dot.
        // Amend drops its dot onto a line without the old tip, with a faded ghost where the old tip was.
        var dropping = Result is not null && te >= t2End;
        var drawn = line;
        int? amendGhost = null;
        if (op == AreasOp.Amend && dropping)
        {
            drawn = line.Take(line.Count - 1).ToList();
            amendGhost = L.SlotX(slot);
        }
        var hide = moving.Select(f => f.Path).ToHashSet();
        var grid = AreasScenes.Static(g, before, new AreasOptions(HideStaged: hide, Line: drawn, GhostX: amendGhost));

        if (te < _t1)
        {
            var p = MergeScenes.Ease(te / (double)_t1);
            for (var k = 0; k < moving.Count; k++)
                AreasScenes.FlyToken(grid, L.IxPos(k), L.FoldPos, p, moving[k].Letter, moving[k].Path, Ok);
        }
        else if (Result is null || te < t2End)
        {
            grid.Put(L.FoldPos.X, L.FoldPos.Y, '●', MergeScenes.Blink(e, OkB, MrgB));
        }
        else
        {
            var p = MergeScenes.Ease((te - t2End) / (double)CommitDrop);
            var x = (int)Math.Round(L.FoldPos.X + (L.SlotX(slot) - L.FoldPos.X) * p, MidpointRounding.AwayFromZero);
            var y = (int)Math.Round(L.FoldPos.Y + (L.LineY - L.FoldPos.Y) * p, MidpointRounding.AwayFromZero);
            grid.Put(x, y, '●', OkB);
        }
        return grid;
    }

    // The commit line before the change: the last three commits for a commit, four for an amend.
    private IReadOnlyList<CommitInfo> LineBefore()
    {
        var all = before.Head.Commits;
        var n = op == AreasOp.Amend ? 4 : 3;
        return all.Skip(Math.Max(0, all.Count - n)).ToList();
    }

    private static int IndexOf(IReadOnlyList<FileChange> list, string path, int fallback)
    {
        for (var j = 0; j < list.Count; j++)
            if (list[j].Path == path)
                return j;
        return fallback;
    }
}
