using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The todo scene's animation: the hold until git answers, the play-out after it, and what is drawn at the end (the layout after a finished
/// rebase, the paused scene with the pause's caption).
/// </summary>
public class TodoAnimationTests
{
    private static readonly ArtGeometry G = ArtGeometry.For(66, 9)!;

    private static GraphLayout Layout()
    {
        var a = new CommitInfo("a1111111", "a111111", 1, "Fix typo in README");
        var b = new CommitInfo("b2222222", "b222222", 1, "Add logging");
        var c = new CommitInfo("c3333333", "c333333", 1, "Bump version");
        var s1 = new CommitInfo("d4444444", "d444444", 1, "Change line 1 on main");
        var shared = new CommitInfo("0000000000", "0000000", 1, "Initial");
        return new GraphLayout("main", false, "feature", "c1", "d2", "b0", [a, b, c], false, [s1], false, [shared], false, a, s1, 3, 1,
            GraphKind.Diverged);
    }

    private static IReadOnlyList<RebaseRow> Plan(params RebaseAction[] actions)
    {
        var names = new[] { ("a1111111", "a111111", "Fix typo in README"), ("b2222222", "b222222", "Add logging"), ("c3333333", "c333333", "Bump version") };
        return names.Select((n, i) => new RebaseRow(n.Item1, n.Item2, n.Item3, actions[i])).ToList();
    }

    private static BranchResult Result(string title, string caption, SequenceOutcome? seq, GraphLayout? after = null) =>
        new(seq?.Kind == SequenceOutcomeKind.Done, title, new Caption(caption, new Style(Ink.Ok, true)), after, Sequence: seq);

    private static SequenceOutcome Outcome(SequenceOutcomeKind kind, SequenceStop? stop = null) =>
        new(kind, SequenceKind.Rebase, "feature", "main", "old", "new", stop, [], [], [], [], [], [], "message", []);

    private static SequenceStop ConflictAt(IReadOnlyList<RebaseRow> rows, int index) =>
        new(SequenceKind.Rebase, PauseKind.Conflict, "feature", "main", rows[index].Hash, rows[index].ShortHash, rows[index].Subject,
            [new ConflictFile("app.txt", false)], index + 1, rows.Count, "", [], Ours: true, Rows: rows);

    private static string Dump(ArtGrid g) =>
        string.Join("\n", Enumerable.Range(0, g.Height).Select(y => string.Concat(Enumerable.Range(0, g.Width).Select(x => g[x, y].Ch))));

    [Fact]
    public void Until_git_answers_the_scene_holds_at_the_cut_and_the_title_is_the_running_one()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var anim = new TodoAnimation(Layout(), rows, TodoScenes.Plan(rows, 0, -1));

        Assert.Equal(RebaseText.RunTitle, anim.Title(0));
        Assert.False(anim.IsDone(100000));
        Assert.Equal(Dump(anim.Render(G, 1000).Grid), Dump(anim.Render(G, 9000).Grid));
    }

    [Fact]
    public void A_wait_past_the_limit_shows_the_wait_text()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var anim = new TodoAnimation(Layout(), rows, TodoScenes.Plan(rows, 0, -1));

        Assert.Equal(MergeScenes.WaitingText, anim.Render(G, MergeAnimation.WaitAfterMs).Caption!.Text);
        Assert.NotEqual(MergeScenes.WaitingText, anim.Render(G, 100).Caption!.Text);
    }

    [Fact]
    public void A_finished_run_plays_out_from_the_hold_point_after_the_answer_and_then_shows_the_layout_after_the_change()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var anim = new TodoAnimation(Layout(), rows, TodoScenes.Plan(rows, 0, -1));
        var answer = Result(RebaseText.DoneTitle, "✔ done", Outcome(SequenceOutcomeKind.Done), Layout());

        anim.SetResult(answer, 1000);

        // The answer arrived at 1000 ms, 400 ms after the hold point (600 ms), so the run's 2980 ms end is reached at 3380 ms.
        Assert.False(anim.IsDone(3379));
        Assert.True(anim.IsDone(3380));
        Assert.Equal("✔ done", anim.Render(G, 3380).Caption!.Text);
    }

    [Fact]
    public void A_conflict_stop_ends_on_the_stop_frame_with_the_pause_caption()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var anim = new TodoAnimation(Layout(), rows, TodoScenes.Plan(rows, 0, -1));
        var stop = ConflictAt(rows, 0);
        var answer = Result(RebaseText.PausedTitle, "✕ CONFLICT in app.txt", Outcome(SequenceOutcomeKind.Paused, stop));

        anim.SetResult(answer, 1000);

        // The first row's flight ends at 1300 ms of scene time: 400 ms after the hold point is passed (1000 ms on the clock), at 1700 ms.
        Assert.False(anim.IsDone(1699));
        Assert.True(anim.IsDone(1700));
        var frame = anim.Render(G, 1700);
        Assert.Equal("✕ CONFLICT in app.txt", frame.Caption!.Text);
        Assert.Equal('✕', frame.Grid[28, 6].Ch);
        Assert.Equal(RebaseText.PausedTitle, anim.Title(1700));
    }

    [Fact]
    public void A_paused_run_ends_at_its_stop_even_when_the_clock_goes_on()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var anim = new TodoAnimation(Layout(), rows, TodoScenes.Plan(rows, 0, -1));
        anim.SetResult(Result(RebaseText.PausedTitle, "paused", Outcome(SequenceOutcomeKind.Paused, ConflictAt(rows, 0))), 1000);

        var late = anim.Render(G, 9000);

        Assert.Equal("paused", late.Caption!.Text);
        Assert.Equal('✕', late.Grid[28, 6].Ch);
    }

    [Fact]
    public void An_abort_plays_its_flash_and_return_and_then_shows_the_layout_it_returned_to()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var run = TodoScenes.Aborting(TodoScenes.Plan(rows, 0, -1), 0, PauseKind.Conflict);
        var anim = new TodoAnimation(Layout(), rows, run);
        var answer = new BranchResult(false, RebaseText.AbortedTitle, new Caption("✕ Rebase aborted: feature unchanged", new Style(Ink.Bad, true)),
            Layout(), Sequence: null);

        anim.SetResult(answer, 500);

        Assert.True(anim.IsDone(500 + TodoScenes.Flash + TodoScenes.Ret));
        Assert.False(anim.IsDone(500 + TodoScenes.Flash));
        Assert.Equal("✕ Rebase aborted: feature unchanged", anim.Render(G, 500 + TodoScenes.Flash + TodoScenes.Ret).Caption!.Text);
    }
}
