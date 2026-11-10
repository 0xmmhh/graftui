using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The interactive rebase's scene, drawn at key times: the cut and hold, the flights, the landings, the drops, the stops and the abort. The
/// geometry is the 9-row art at 66 columns (step 6, the base at column 16, row A at 2 and row B at 6). The onto branch has one commit of its own,
/// so the first slot is at column 28. The plan is three commits: a pick, a fixup into it, and a drop.
/// </summary>
public class TodoScenesTests
{
    private static readonly ArtGeometry G = ArtGeometry.For(66, 9)!;
    private static readonly Style Dim = new(Ink.Dim), Bad = new(Ink.Bad), BadB = new(Ink.Bad, true), WarnB = new(Ink.Warn, true);
    private static readonly Style OkB = new(Ink.Ok, true), MrgB = new(Ink.Mrg, true), IncB = new(Ink.Inc, true);

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

    private static SequenceOutcome Outcome(SequenceOutcomeKind kind, SequenceStop? stop = null) =>
        new(kind, SequenceKind.Rebase, "feature", "main", "old", kind == SequenceOutcomeKind.Done ? "new" : null, stop, [], [], [], [], [], [],
            "message", []);

    private static SequenceStop Stop(PauseKind pause, IReadOnlyList<RebaseRow> rows, int index, params string[] folded) =>
        new(SequenceKind.Rebase, pause, "feature", "main", rows[index].Hash, rows[index].ShortHash, rows[index].Subject,
            pause == PauseKind.Conflict ? [new ConflictFile("app.txt", false)] : [], index + 1, rows.Count, "", folded, Ours: true, Rows: rows);

    /// <summary>The bold ink of the current branch's dots in the test layout.</summary>
    private static Style CurB() => new(BranchInks.Current(Layout()), true);

    private static TodoRun Run(IReadOnlyList<RebaseRow> rows) => TodoScenes.Plan(rows, 0, -1);

    private static TodoRun Done(IReadOnlyList<RebaseRow> rows) => TodoScenes.Ended(Run(rows), rows, Outcome(SequenceOutcomeKind.Done));

    private static TodoRun Paused(IReadOnlyList<RebaseRow> rows, PauseKind pause, int index, params string[] folded) =>
        TodoScenes.Ended(Run(rows), rows, Outcome(SequenceOutcomeKind.Paused, Stop(pause, rows, index, folded)));

    private static ArtGrid Frame(IReadOnlyList<RebaseRow> rows, TodoRun run, long te, long t = 0, bool waiting = false) =>
        TodoScenes.Rebase(G, Layout(), rows, run, te, t, waiting).Grid;

    private static Caption? CaptionOf(IReadOnlyList<RebaseRow> rows, TodoRun run, long te, long t = 0, bool waiting = false) =>
        TodoScenes.Rebase(G, Layout(), rows, run, te, t, waiting).Caption;

    [Fact]
    public void Before_the_cut_row_A_is_whole_with_the_scissors_after_the_base()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var grid = Frame(rows, Done(rows), 0);

        Assert.Equal('●', grid[16, 2].Ch);
        Assert.Equal('✂', grid[19, 2].Ch);
        Assert.Equal('●', grid[22, 2].Ch);
        Assert.Equal('✕', grid[34, 2].Ch);
    }

    [Fact]
    public void Each_row_starts_one_flight_and_one_gap_after_the_one_before_it()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        Assert.Equal(new long[] { 600, 1440, 2280 }, Run(rows).Starts);
    }

    [Fact]
    public void A_run_that_plays_to_the_end_ends_at_the_last_landing()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        Assert.Equal(2280 + TodoScenes.Fly, TodoScenes.Total(Done(rows)));
    }

    [Fact]
    public void At_the_hold_point_the_first_flight_is_at_its_commit_and_the_scene_waits_there_for_git()
    {
        // The first row starts at the hold point: its dot sits on its commit until git answers.
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var grid = Frame(rows, Done(rows), TodoScenes.Cut);

        Assert.Equal(IncB, grid[22, 2].Style);
        Assert.Equal('●', grid[22, 2].Ch);
        Assert.EndsWith("Replaying a111111 Fix typo in README  (1/3)", CaptionOf(rows, Done(rows), TodoScenes.Cut)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void While_git_is_slow_the_caption_is_the_wait_text_in_warn()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var caption = CaptionOf(rows, Done(rows), TodoScenes.Cut, waiting: true);

        Assert.Equal(MergeScenes.WaitingText, caption!.Text);
        Assert.Equal(new Style(Ink.Warn), caption.Style);
    }

    [Fact]
    public void A_flight_arcs_up_from_its_commit_and_its_dot_is_bold_inc_at_the_middle()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        // The middle of the first flight: its dot is at column 25, on the arc's top row (row A). The trail is bright and dim.
        var grid = Frame(rows, Done(rows), 600 + TodoScenes.Fly / 2);

        Assert.Equal('●', grid[25, 2].Ch);
        Assert.Equal(IncB, grid[25, 2].Style);
    }

    [Fact]
    public void The_flight_caption_names_the_commit_and_its_place_in_the_plan_with_the_spinner_in_front()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var caption = CaptionOf(rows, Done(rows), 600 + 100, t: 0);

        Assert.EndsWith("Replaying a111111 Fix typo in README  (1/3)", caption!.Text, StringComparison.Ordinal);
        Assert.Equal(new Style(Ink.Fg), caption.Style);
    }

    [Fact]
    public void A_landed_pick_is_a_bright_ok_dot_in_its_slot_for_a_moment_then_the_current_colour()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);
        var landing = 600 + TodoScenes.Fly;

        Assert.Equal('●', Frame(rows, Done(rows), landing + 100)[28, 6].Ch);
        Assert.Equal(OkB, Frame(rows, Done(rows), landing + 100)[28, 6].Style);
        Assert.Equal(CurB(), Frame(rows, Done(rows), landing + 400)[28, 6].Style);
    }

    [Fact]
    public void A_fold_lands_in_the_slot_above_it_in_the_merge_colour_for_a_moment()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);
        var landing = 1440 + TodoScenes.Fly;

        Assert.Equal(MrgB, Frame(rows, Done(rows), landing + 100)[28, 6].Style);
        Assert.Equal(CurB(), Frame(rows, Done(rows), landing + 400)[28, 6].Style);
    }

    [Fact]
    public void A_fold_caption_says_squashing_with_its_place()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var caption = CaptionOf(rows, Done(rows), 1440 + 100);

        Assert.EndsWith("Squashing b222222 Add logging  (2/3)", caption!.Text, StringComparison.Ordinal);
        Assert.Equal(new Style(Ink.Mrg), caption.Style);
    }

    [Fact]
    public void A_drop_blinks_on_row_A_while_it_flies_and_is_a_dim_cross_after()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);
        var start = 2280;

        // The drop is the third row, so its commit is at column 34 on row A.
        Assert.Equal(Bad, Frame(rows, Done(rows), start)[34, 2].Style);
        Assert.Equal(Dim, Frame(rows, Done(rows), start + TodoScenes.Fly + 50)[34, 2].Style);
        Assert.Equal('✕', Frame(rows, Done(rows), start + TodoScenes.Fly + 50)[34, 2].Ch);
    }

    [Fact]
    public void A_drop_caption_says_dropping_in_bad()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var caption = CaptionOf(rows, Done(rows), 2280 + 100);

        Assert.EndsWith("Dropping c333333 Bump version", caption!.Text, StringComparison.Ordinal);
        Assert.Equal(new Style(Ink.Bad), caption.Style);
    }

    [Fact]
    public void A_flown_row_leaves_a_dim_ghost_on_row_A()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);

        var grid = Frame(rows, Done(rows), 1440 + 50);

        Assert.Equal('◌', grid[22, 2].Ch);
        Assert.Equal(Dim, grid[22, 2].Style);
    }

    [Fact]
    public void A_conflict_stop_draws_a_bad_bar_and_a_cross_one_cell_short_of_its_slot_at_the_end_of_its_flight()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);
        var run = Paused(rows, PauseKind.Conflict, 0);

        var grid = Frame(rows, run, TodoScenes.Total(run));

        Assert.Equal('━', grid[24, 6].Ch);
        Assert.Equal(new Style(Ink.Bad), grid[24, 6].Style);
        Assert.Equal('✕', grid[28, 6].Ch);
        Assert.Equal(BadB, grid[28, 6].Style);
        Assert.Equal(1300, TodoScenes.Total(run));
    }

    [Fact]
    public void An_edit_stop_lands_warm_in_its_slot()
    {
        var rows = Plan(RebaseAction.Edit, RebaseAction.Pick, RebaseAction.Drop);
        var run = Paused(rows, PauseKind.Edit, 0);

        var grid = Frame(rows, run, TodoScenes.Total(run));

        Assert.Equal('●', grid[28, 6].Ch);
        Assert.Equal(WarnB, grid[28, 6].Style);
        Assert.DoesNotContain('✕', string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, 6].Ch)));
    }

    [Fact]
    public void A_squash_stop_is_at_the_last_fold_of_its_group_and_lands_warm_on_the_target_slot()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Squash, RebaseAction.Drop);
        var run = Paused(rows, PauseKind.Squash, 0, rows[1].Hash);

        Assert.Equal(1, run.StopAt);
        Assert.Equal(1440 + TodoScenes.Fly, TodoScenes.Total(run));
        Assert.Equal(WarnB, Frame(rows, run, TodoScenes.Total(run))[28, 6].Style);
    }

    [Fact]
    public void An_abort_crosses_the_stopped_row_at_once_and_flashes_before_it_returns()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);
        var run = TodoScenes.Aborting(TodoScenes.Plan(rows, 0, -1), 0, PauseKind.Conflict);

        Assert.Equal(TodoScenes.Flash + TodoScenes.Ret, TodoScenes.Total(run));
        Assert.Equal('✕', Frame(rows, run, 0)[28, 6].Ch);
        Assert.Equal("✕ Aborting the rebase…", CaptionOf(rows, run, 0)!.Text);
        Assert.Equal("✕ Aborting the rebase…", CaptionOf(rows, run, 900)!.Text);
    }

    [Fact]
    public void A_resume_from_a_conflict_plays_from_the_stopped_row_and_leaves_the_rows_before_it_landed()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var stop = Stop(PauseKind.Conflict, rows, 1);

        var from = TodoScenes.ResumeFrom(rows, stop, ResumeAction.Continue);
        var run = TodoScenes.Plan(rows, from, -1);

        Assert.Equal(1, from);
        Assert.Equal(-1, run.Starts[0]);
        Assert.Equal(600, run.Starts[1]);
        Assert.Equal('●', Frame(rows, run, 600)[22, 6].Ch);
    }

    [Fact]
    public void A_resume_that_skips_draws_the_skipped_row_as_a_dim_cross_and_flies_the_next_one()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var stop = Stop(PauseKind.Conflict, rows, 0);

        var from = TodoScenes.ResumeFrom(rows, stop, ResumeAction.Skip);
        var run = TodoScenes.Plan(rows, from, from);

        Assert.Equal(-1, run.Starts[0]);
        Assert.Equal(600, run.Starts[1]);
        Assert.Equal(Dim, Frame(rows, run, 600)[16 + 6, 2].Style);
        Assert.Equal('✕', Frame(rows, run, 600)[22, 2].Ch);
    }

    [Fact]
    public void A_resume_after_an_edit_or_a_reword_starts_at_the_row_after_its_stop()
    {
        var rows = Plan(RebaseAction.Edit, RebaseAction.Pick, RebaseAction.Pick);

        Assert.Equal(1, TodoScenes.ResumeFrom(rows, Stop(PauseKind.Edit, rows, 0), ResumeAction.AmendAndContinue));
        Assert.Equal(1, TodoScenes.ResumeFrom(rows, Stop(PauseKind.Reword, rows, 0), ResumeAction.Message));
    }

    [Fact]
    public void A_resume_after_a_squash_starts_after_the_last_fold_of_its_group()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Squash, RebaseAction.Pick);

        Assert.Equal(2, TodoScenes.ResumeFrom(rows, Stop(PauseKind.Squash, rows, 0, rows[1].Hash), ResumeAction.Message));
    }

    [Fact]
    public void Slots_are_taken_by_kept_rows_and_a_fold_takes_the_slot_above_it()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop);
        var four = new[] { RebaseAction.Pick, RebaseAction.Fixup, RebaseAction.Drop, RebaseAction.Pick };
        var rowsFour = four.Select((a, i) => new RebaseRow("h" + i, "h" + i, "s", a)).ToList();

        Assert.Equal(new[] { 0, 0, -1 }, TodoScenes.SlotsOf(rows));
        Assert.Equal(new[] { 0, 0, -1, 1 }, TodoScenes.SlotsOf(rowsFour));
    }

    [Fact]
    public void A_fold_with_nothing_kept_above_it_has_no_slot()
    {
        var rows = Plan(RebaseAction.Fixup, RebaseAction.Pick, RebaseAction.Pick);

        Assert.Equal(new[] { -1, 0, 1 }, TodoScenes.SlotsOf(rows));
    }

    [Fact]
    public void The_run_of_a_stop_is_the_stop_row_and_a_stop_outside_the_plan_halts_the_run()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var outside = Stop(PauseKind.Conflict, rows, 0) with { Commit = "ffffffff" };

        var halted = TodoScenes.Ended(Run(rows), rows, Outcome(SequenceOutcomeKind.Paused, outside));

        Assert.Equal(TodoEnd.Halt, halted.End);
        Assert.Equal(TodoEnd.Stop, Paused(rows, PauseKind.Conflict, 2).End);
        Assert.Equal(2, Paused(rows, PauseKind.Conflict, 2).StopAt);
    }

    [Fact]
    public void A_refusal_or_a_failure_halts_the_run_before_anything_flies()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);

        var refused = TodoScenes.Ended(Run(rows), rows, Outcome(SequenceOutcomeKind.Refused));

        Assert.Equal(TodoEnd.Halt, refused.End);
        Assert.Equal(TodoScenes.HaltAt, TodoScenes.Total(refused));
        Assert.Equal(Dump(Frame(rows, refused, 0)), Dump(Frame(rows, refused, TodoScenes.Total(refused))));
    }

    [Fact]
    public void A_resume_whose_stop_is_not_in_the_plan_starts_at_its_step_and_names_no_row()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);
        var stop = Stop(PauseKind.Conflict, rows, 2) with { Commit = "ffffffff" };

        Assert.Equal(-1, TodoScenes.StopIndex(rows, stop));
        Assert.Equal(2, TodoScenes.ResumeFrom(rows, stop, ResumeAction.Skip));
    }

    [Theory]
    [InlineData(3, 2980)]
    [InlineData(5, 4660)]
    [InlineData(6, 4660)]
    [InlineData(20, 4646)]
    [InlineData(50, 4636)]
    public void The_replay_ends_at_today_s_time_up_to_five_rows_and_within_the_budget_after_that(int n, long total)
    {
        var run = Run(Picks(n));

        Assert.Equal(total, TodoScenes.Total(run));
        Assert.True(TodoScenes.Total(run) <= TodoScenes.Cut + TodoScenes.ReplayMs);
    }

    [Fact]
    public void A_long_replay_keeps_its_flights_in_order_and_each_one_takes_some_time()
    {
        var run = Run(Picks(50));

        Assert.True(run.Fly >= 1);
        for (var j = 1; j < run.Starts.Count; j++)
            Assert.True(run.Starts[j] > run.Starts[j - 1]);
    }

    [Theory]
    [InlineData(41)]
    [InlineData(23)]
    public void At_the_smallest_geometries_a_conflict_on_the_fourth_row_is_drawn_on_row_B(int width)
    {
        var geo = ArtGeometry.For(width, 9)!;
        var rows = Picks(5);
        var run = TodoScenes.Ended(Run(rows), rows, Outcome(SequenceOutcomeKind.Paused, Stop(PauseKind.Conflict, rows, 3)));

        var grid = TodoScenes.Rebase(geo, ThreeOwn(), rows, run, TodoScenes.Total(run), 0, waiting: false).Grid;

        Assert.Equal('✕', grid[width - 1, geo.B].Ch);
    }

    [Fact]
    public void A_conflict_on_the_fifth_row_at_64_columns_is_drawn_inside_the_art_on_row_B()
    {
        var geo = ArtGeometry.For(64, 9)!;
        var rows = Picks(5);
        var run = TodoScenes.Ended(Run(rows), rows, Outcome(SequenceOutcomeKind.Paused, Stop(PauseKind.Conflict, rows, 4)));

        var grid = TodoScenes.Rebase(geo, ThreeOwn(), rows, run, TodoScenes.Total(run), 0, waiting: false).Grid;

        Assert.Equal('✕', grid[58, geo.B].Ch);
    }

    [Fact]
    public void A_resume_draws_the_branch_by_name_with_its_colour_and_lands_the_stopped_commit_in_the_slot_after_the_onto_tip()
    {
        var rows = Plan(RebaseAction.Edit, RebaseAction.Pick, RebaseAction.Pick);
        var layout = ResumedLayout();
        var from = TodoScenes.ResumeFrom(rows, Stop(PauseKind.Edit, rows, 0), ResumeAction.AmendAndContinue);
        var run = TodoScenes.Plan(rows, from, -1);

        var frame = TodoScenes.Rebase(G, layout, rows, run, TodoScenes.Total(run), 0, waiting: false);
        var text = string.Join("\n", Enumerable.Range(0, G.Height).Select(y => RowText(frame.Grid, y)));

        Assert.Contains("feature (current)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HEAD", text, StringComparison.Ordinal);
        Assert.Contains("Rebasing feature onto main", frame.Caption!.Text, StringComparison.Ordinal);
        Assert.Equal('●', frame.Grid[22, G.B].Ch);
        Assert.Equal(new Style(BranchInks.Current(layout), true), frame.Grid[28, G.B].Style);
    }

    [Fact]
    public void A_resume_after_a_squash_keeps_the_fold_target_in_its_own_slot_while_the_next_commit_flies()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Squash, RebaseAction.Pick);
        var layout = ResumedLayout();
        var stop = Stop(PauseKind.Squash, rows, 0, rows[1].Hash);
        var run = TodoScenes.Plan(rows, TodoScenes.ResumeFrom(rows, stop, ResumeAction.Message), -1);

        var mid = TodoScenes.Rebase(G, layout, rows, run, run.Starts[2] + run.Fly / 2, 0, waiting: false).Grid;
        var end = TodoScenes.Rebase(G, layout, rows, run, TodoScenes.Total(run), 0, waiting: false).Grid;

        Assert.Equal('●', mid[22, G.B].Ch);
        Assert.Equal('●', mid[28, G.B].Ch);
        Assert.Equal('●', end[28, G.B].Ch);
        Assert.Equal(new Style(BranchInks.Current(layout), true), end[28, G.B].Style);
    }

    /// <summary>The branch as a resume reads it: feature at its old tip (named, HEAD is detached) against main, which has one commit of its own.</summary>
    private static GraphLayout ResumedLayout()
    {
        var a = new CommitInfo("a1111111", "a111111", 1, "Fix typo in README");
        var b = new CommitInfo("b2222222", "b222222", 1, "Add logging");
        var c = new CommitInfo("c3333333", "c333333", 1, "Bump version");
        var s1 = new CommitInfo("d4444444", "d444444", 1, "Change line 1 on main");
        var shared = new CommitInfo("0000000000", "0000000", 1, "Initial");
        var layout = new GraphLayout("feature", false, "main", "a1111111", "d4444444", "0000000000", [a, b, c], false, [s1], false, [shared], false,
            a, s1, 3, 1, GraphKind.Diverged);
        return layout.WithLanes([new Branch("feature", "a1111111", "a111111", false), new Branch("main", "d4444444", "d444444", false)]);
    }

    private static string RowText(ArtGrid grid, int y) => string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    /// <summary>n picks with made-up hashes, for the runs whose length is the point of a test.</summary>
    private static IReadOnlyList<RebaseRow> Picks(int n) =>
        Enumerable.Range(0, n).Select(i => new RebaseRow("h" + i, "h" + i, "subject " + i, RebaseAction.Pick)).ToList();

    /// <summary>The onto branch with three commits of its own, so its first slot is three steps past the base's tip.</summary>
    private static GraphLayout ThreeOwn()
    {
        var a = new CommitInfo("a1111111", "a111111", 1, "Fix typo in README");
        var b = new CommitInfo("b2222222", "b222222", 1, "Add logging");
        var c = new CommitInfo("c3333333", "c333333", 1, "Bump version");
        var s1 = new CommitInfo("e5555555", "e555555", 1, "Second on main");
        var s2 = new CommitInfo("f6666666", "f666666", 1, "Third on main");
        var s3 = new CommitInfo("g7777777", "g777777", 1, "Fourth on main");
        var shared = new CommitInfo("0000000000", "0000000", 1, "Initial");
        return new GraphLayout("main", false, "feature", "c1", "d2", "b0", [a, b, c], false, [s1, s2, s3], false, [shared], false, a, s3, 3, 1,
            GraphKind.Diverged);
    }

    private static string Dump(ArtGrid g) =>
        string.Join("\n", Enumerable.Range(0, g.Height).Select(y => string.Concat(Enumerable.Range(0, g.Width).Select(x => g[x, y].Ch))));
}
