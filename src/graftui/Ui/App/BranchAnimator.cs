using System.Diagnostics;
using graftui.Git;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// Plays a branch change's animation (switch, create, rename, delete, stash-merge-pop) on the art pane, and reports it when it ends:
/// freezes the last frame, logs the commands and the lines of the change, clears Busy, then runs the callback (snapshot and selection).
/// The same shape as <see cref="ChangesAnimator"/>, for <see cref="IBranchAnimation"/>.
/// </summary>
internal sealed class BranchAnimator(AppController app)
{
    private IBranchAnimation? _anim;
    private Stopwatch? _clock;
    private (BranchResult Result, IReadOnlyList<GitResult> Commands, IReadOnlyList<(string Text, Style Style)> Log, Action OnDone)? _pending;

    /// <summary>True from <see cref="Begin"/> until the animation has finished.</summary>
    internal bool Running => _anim is not null;

    private bool _holdsEsc;

    /// <summary>
    /// True while a switch's animation holds Esc, once git has answered: Esc must not quit graftui before the last frame. During the wait
    /// for git Esc still quits, as it always has, so a hung git can be left.
    /// </summary>
    internal bool HoldsEsc => _holdsEsc && _anim?.Result is not null;

    /// <summary>Starts the scene on the art pane. The result may arrive later through <see cref="SetPending"/>.</summary>
    internal void Begin(IBranchAnimation anim, Func<int, IReadOnlyList<Seg>> direction, CommitPanel? panel, bool holdsEsc = false)
    {
        var clock = Stopwatch.StartNew();
        _anim = anim;
        _clock = clock;
        _holdsEsc = holdsEsc;
        _pending = null; // a result from an earlier change must never be applied to this one

        app.Art.Title = anim.Title(0);
        app.Art.Direction = direction;
        app.Art.Below = null;
        app.Art.Panel = panel;
        app.Art.FallbackCaption = null;
        app.Art.Scene = geo => anim.Render(geo, clock.ElapsedMilliseconds);
        app.Art.SetNeedsDraw();

        app.App.AddTimeout(TimeSpan.FromMilliseconds(50), Tick);
    }

    /// <summary>
    /// Records git's answer. <paramref name="log"/> is the lines the change adds to the log, each with its style (a refusal's
    /// lines are warnings, a failure's are bad). The animation ends once it has played out past this point.
    /// </summary>
    internal void SetPending(BranchResult r, IReadOnlyList<GitResult> commands, IReadOnlyList<(string Text, Style Style)> log, Action onDone) =>
        _pending = (r, commands, log, onDone);

    private bool Tick()
    {
        if (_anim is null || _clock is null)
            return false;

        var e = _clock.ElapsedMilliseconds;
        if (_pending is { } p && _anim.Result is null)
        {
            _anim.SetResult(p.Result, e);
            // The panel moves with the labels when the result is in; a failure or a refusal sets none, so the panel stays as it was.
            if (p.Result.Panel is { } panel)
                app.Art.Panel = panel;
        }

        app.Art.Title = _anim.Title(e);
        app.Art.SetNeedsDraw();

        if (_anim.IsDone(e))
        {
            Finish();
            return false;
        }
        return true;
    }

    private void Finish()
    {
        var anim = _anim!;
        var pending = _pending!.Value;
        var final = _clock!.ElapsedMilliseconds;

        // Freeze the scene at its final time so the art stops moving.
        app.Art.Scene = geo => anim.Render(geo, final);
        app.Art.Title = anim.Title(final);
        if (pending.Result.After is { } after)
            app.Art.Panel = CommitLists.Idle(after);

        var anchor = app.LogCommands(pending.Commands, ShowOutput, IsAnchor);
        foreach (var (text, style) in pending.Log)
            app.LogMessage(text, style);
        if (anchor >= 0)
            app.Log.SetAnchor(anchor);

        _anim = null;
        _clock = null;
        _pending = null;
        _holdsEsc = false;

        app.Busy = false;
        app.Keys.Busy = false;
        app.Keys.SetNeedsDraw();
        app.Art.SetNeedsDraw();
        // Once graftui has begun to quit the terminal is being restored, so a result that lands now was not seen on screen.
        if (!app.Gate.IsQuitting)
            app.MarkChangeOutcomeShown();
        pending.OnDone();
    }

    /// <summary>The log jumps to the first command that moves the branch or the stash: a switch, a branch change, a stash push or a merge.</summary>
    private static bool IsAnchor(GitResult r) =>
        Is(r, "switch")
        || (Is(r, "branch") && !r.Args.Contains("--contains"))
        || (Is(r, "stash") && Sub(r, "push"))
        || Is(r, "merge");

    /// <summary>The anchors' output, the stash pop and the merge abort, and the output of any failed command.</summary>
    private static bool ShowOutput(GitResult r) =>
        IsAnchor(r)
        || (Is(r, "stash") && Sub(r, "pop"))
        || (Is(r, "merge") && r.Args.Contains("--abort"))
        || !r.Success;

    private static bool Is(GitResult r, string verb) => r.Args.Count > 0 && r.Args[0] == verb;

    private static bool Sub(GitResult r, string sub) => r.Args.Count > 1 && r.Args[1] == sub;
}
