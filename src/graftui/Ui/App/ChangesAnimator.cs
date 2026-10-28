using System.Diagnostics;
using graftui.Git;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// Plays a change's animation (stage, unstage, commit, amend) on the art pane, and reports it when it ends:
/// freezes the last frame, logs the commands, clears Busy and then runs the callback (refresh and reload).
/// </summary>
internal sealed class ChangesAnimator(AppController app)
{
    private static readonly Style Bad = new(Ink.Bad);

    private IAreasAnimation? _anim;
    private Stopwatch? _clock;
    private (AreasResult Result, IReadOnlyList<GitResult> Commands, string? FailureLog, Action OnDone)? _pending;

    /// <summary>True from <see cref="Begin"/> until the animation has finished.</summary>
    internal bool Running => _anim is not null;

    /// <summary>Starts the scene on the art pane. The result may arrive later through <see cref="SetPending"/>.</summary>
    internal void Begin(IAreasAnimation anim, Func<int, IReadOnlyList<Seg>> direction, IReadOnlyList<IReadOnlyList<Seg>> below)
    {
        var clock = Stopwatch.StartNew();
        _anim = anim;
        _clock = clock;
        _pending = null; // a result from an earlier change must never be applied to this one

        app.Art.Title = anim.Title(0);
        app.Art.Direction = direction;
        app.Art.Below = below;
        app.Art.Commits = [];
        app.Art.FallbackCaption = null;
        app.Art.Scene = geo => anim.Render(geo, clock.ElapsedMilliseconds);
        app.Art.SetNeedsDraw();

        app.App.AddTimeout(TimeSpan.FromMilliseconds(50), Tick);
    }

    /// <summary>Records git's answer. The animation ends once it has played out past this point.</summary>
    internal void SetPending(AreasResult r, IReadOnlyList<GitResult> commands, string? failureLog, Action onDone) =>
        _pending = (r, commands, failureLog, onDone);

    private bool Tick()
    {
        if (_anim is null || _clock is null)
            return false;

        var e = _clock.ElapsedMilliseconds;
        if (_pending is { } p && _anim.Result is null)
            _anim.SetResult(p.Result, e);

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

        var anchor = app.LogCommands(pending.Commands, ShowOutput, IsAnchor);
        if (pending.FailureLog is not null)
            app.LogMessage(pending.FailureLog, Bad);
        if (anchor >= 0)
            app.Log.SetAnchor(anchor);

        _anim = null;
        _clock = null;
        _pending = null;

        app.Busy = false;
        app.Keys.Busy = false;
        app.Keys.SetNeedsDraw();
        app.Art.SetNeedsDraw();
        // Once graftui has begun to quit the terminal is being restored, so a result that lands now was not seen on screen.
        if (!app.Gate.IsQuitting)
            app.MarkChangeOutcomeShown();
        pending.OnDone();
    }

    /// <summary>The log jumps to the first commit or stash-change command of the change.</summary>
    private static bool IsAnchor(GitResult r) => IsCommit(r) || IsStashChange(r);

    /// <summary>`git commit` itself, not the `git commit-tree` calls that save undo steps.</summary>
    private static bool IsCommit(GitResult r) =>
        r.Command == "git commit" || r.Command.StartsWith("git commit ", StringComparison.Ordinal);

    /// <summary>A stash push, pop or store: the commands that move the stash entry.</summary>
    private static bool IsStashChange(GitResult r) =>
        r.Command.StartsWith("git stash push", StringComparison.Ordinal)
        || r.Command.StartsWith("git stash pop", StringComparison.Ordinal)
        || r.Command.StartsWith("git stash store", StringComparison.Ordinal);

    /// <summary>
    /// Shows the output of a commit (the new commit line and the stat), of a stash push, pop or store, and of any failed index or
    /// work-tree command. Successful quiet commands stay out of the log's output.
    /// </summary>
    private static bool ShowOutput(GitResult r) =>
        IsCommit(r)
        || IsStashChange(r)
        || (!r.Success && (r.Command.StartsWith("git add", StringComparison.Ordinal)
            || r.Command.StartsWith("git restore", StringComparison.Ordinal)
            || r.Command.StartsWith("git rm", StringComparison.Ordinal)
            || r.Command.StartsWith("git clean", StringComparison.Ordinal)
            || r.Command.StartsWith("git reset", StringComparison.Ordinal)
            || r.Command.StartsWith("git update-index", StringComparison.Ordinal)
            || r.Command.StartsWith("git read-tree", StringComparison.Ordinal)));
}
