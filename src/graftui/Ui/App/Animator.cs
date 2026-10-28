using System.Diagnostics;
using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The merge's screens: the refusal, the animation while git runs, and the final frame with its log lines.
/// </summary>
internal sealed class Animator
{
    private static readonly Style WarnB = new(Ink.Warn, true);
    private static readonly Style Bad = new(Ink.Bad);

    private readonly AppController _app;
    private MergeAnimation? _anim;
    private Stopwatch? _clock;
    private (MergeOutcome Outcome, RepoSnapshot? After, CommitInfo? MergeCommit)? _pending;

    internal Animator(AppController app)
    {
        _app = app;
    }

    internal void ShowRefused(MergeCheck check, string selName, GraphLayout? layout)
    {
        _app.LogCommands(check.Commands);
        var reason = check.Refusal ?? "";
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);

        var detached = check.CurrentBranch.Length == 0;
        _app.Art.Title = "Refused";
        _app.Art.Direction = cols => DirectionLine.Merge(selName, check.CurrentBranch, detached, cols);
        _app.Art.Panel = layout is null ? null : CommitLists.Idle(layout);
        _app.Art.FallbackCaption = null;
        _app.Art.Scene = geo =>
        {
            var frame = layout is null
                ? new SceneFrame(new ArtGrid(geo.Width, geo.Height), null)
                : MergeScenes.Preview(geo, layout);
            MergeScenes.RefusedOverlay(frame.Grid, RefusalText.Overlay(reason));
            return new SceneFrame(frame.Grid, new Caption(RefusalText.Caption(reason), WarnB));
        };

        _app.Result = selName;
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();
        _app.Art.SetNeedsDraw();

        // Starting a merge bumped the snapshot sequence, which drops an in-flight counts load. Nothing merged, so
        // reload the counts and the status (clean or dirty, branch) from git.
        _app.ReloadCountsAsync();
    }

    internal void BeginAnimation(MergeCheck check, GraphLayout layout)
    {
        _app.LogCommands(check.Commands);
        var anim = new MergeAnimation(layout);
        var clock = Stopwatch.StartNew();
        _anim = anim;
        _clock = clock;
        _pending = null; // an outcome from an earlier merge must never be applied to this one
        var detached = layout.Detached;

        _app.Art.Title = anim.Title(0);
        _app.Art.Direction = cols => DirectionLine.Merge(layout.Selected, layout.Current, detached, cols);
        _app.Art.Panel = CommitLists.Idle(layout);
        _app.Art.FallbackCaption = null;
        _app.Art.Scene = geo => anim.Render(geo, clock.ElapsedMilliseconds);
        _app.Art.SetNeedsDraw();

        _app.App.AddTimeout(TimeSpan.FromMilliseconds(50), Tick);
    }

    internal void SetPending(MergeOutcome o, RepoSnapshot? after, CommitInfo? mergeCommit) =>
        _pending = (o, after, mergeCommit);

    private bool Tick()
    {
        if (_anim is null || _clock is null)
            return false;

        var e = _clock.ElapsedMilliseconds;
        if (_pending is { } p && _anim.Outcome is null)
            _anim.SetOutcome(p.Outcome, e);

        _app.Art.Title = _anim.Title(e);
        _app.Art.SetNeedsDraw();

        if (_anim.IsDone(e))
        {
            FinishAnimation();
            return false;
        }
        return true;
    }

    private void FinishAnimation()
    {
        var anim = _anim!;
        var pending = _pending!.Value;
        var outcome = pending.Outcome;
        var final = _clock!.ElapsedMilliseconds;
        _app.MarkOutcomeShown();

        // Freeze the scene at its final time so the art stops moving.
        _app.Art.Scene = geo => anim.Render(geo, final);
        _app.Art.Title = anim.Title(final);
        _app.Art.Panel = CommitLists.After(anim.Layout, outcome, pending.MergeCommit);

        var mergeIndex = _app.LogCommands(outcome.Commands);
        if (outcome.ConflictFiles.Count > 0)
            _app.LogMessage("Conflicting files: " + string.Join(", ", outcome.ConflictFiles), Bad);
        if (outcome.Kind == MergeKind.Failed)
            _app.LogMessage("Merge failed: " + outcome.Message, Bad);
        if (mergeIndex >= 0)
            _app.Log.SetAnchor(mergeIndex);

        if (pending.After is not null)
            _app.ApplySnapshot(pending.After);

        _app.Result = anim.Layout.Selected;
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();
        _app.Art.SetNeedsDraw();
        _anim = null;
        _clock = null;
        _pending = null;
    }
}
