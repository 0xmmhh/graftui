using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class BranchAnimationTests
{
    private static readonly ArtGeometry Geo = ArtGeometry.For(80, 11)!;

    private static GraphLayout Alone(string name, string tip) =>
        new(name, false, name, tip, tip, tip, [], false, [], false, [Layouts.C(tip)], false, Layouts.C(tip), Layouts.C(tip), 0, 0,
            GraphKind.Same);

    private static BranchResult Success(GraphLayout? after, string title, string text) =>
        new(true, title, new Caption(text, new Style(Ink.Ok, true)), after);

    private static string Row(ArtGrid grid, int y) =>
        string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    private static string Text(ArtGrid grid) =>
        string.Join('\n', Enumerable.Range(0, grid.Height).Select(y => Row(grid, y)));

    // The row of the first cell that shows the glyph, or -1.
    private static int RowOf(ArtGrid grid, char ch)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y].Ch == ch)
                    return y;
        return -1;
    }

    private static int CountOf(ArtGrid grid, char ch) =>
        Enumerable.Range(0, grid.Height).Sum(y => Enumerable.Range(0, grid.Width).Count(x => grid[x, y].Ch == ch));

    // ----- switch -----

    // The diverged pair: main (current) on the top row, feature below it. Its labels sit at the tip of the one commit each.
    private static BranchAnimation Switch() =>
        new(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");

    private static BranchResult SwitchedOk(string text = "✔ Switched to feature") =>
        new(true, "Switched", new Caption(text, new Style(Ink.Ok, true)), null);

    private static bool SameCells(ArtGrid a, ArtGrid b)
    {
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                if (a[x, y] != b[x, y])
                    return false;
        return true;
    }

    // A label's cells: its marker, its name row and the " (current)" after it. Everything else on the grid is the graph.
    private static bool IsLabelCell(ArtGeometry g, int x, int y) =>
        (y == g.A - 2 || y == g.A - 1 || y == g.B + 1 || y == g.B + 2) && x >= g.TipX(1) && x < g.TipX(1) + 20;

    [Fact]
    public void Switch_timeline_at_0_125_250_375_and_600_ms_moves_current_between_the_labels()
    {
        var anim = Switch();
        anim.SetResult(SwitchedOk(), 0);
        var x = Geo.TipX(1);
        var oldName = Geo.A - 2;
        var oldMarker = Geo.A - 1;
        var newName = Geo.B + 2;
        var newMarker = Geo.B + 1;

        // 0 ms: the old label is current as in the preview; the new one is plain.
        var t0 = anim.Render(Geo, 0).Grid;
        Assert.Equal(new Cell('m', new Style(Ink.Cur, true)), t0[x, oldName]);
        Assert.Equal(new Cell('▾', new Style(Ink.Cur)), t0[x, oldMarker]);
        Assert.Equal(new Cell('(', new Style(Ink.Dim)), t0[x + 5, oldName]);
        Assert.Equal(new Cell('f', new Style(Ink.Inc, true)), t0[x, newName]);
        Assert.Equal(new Cell('▴', new Style(Ink.Inc)), t0[x, newMarker]);
        Assert.Equal(' ', t0[x + 8, newName].Ch);

        // 125 ms: " (current)" is fading off the old label, which is moving from the current style to the normal one.
        var t125 = anim.Render(Geo, 125).Grid;
        Assert.Equal(new Cell('m', new Style(Ink.Fg, true)), t125[x, oldName]);
        Assert.Equal(new Cell('▾', new Style(Ink.Fg)), t125[x, oldMarker]);
        Assert.Equal(new Cell('(', new Style(Ink.Frame)), t125[x + 5, oldName]);
        Assert.Equal(new Cell('f', new Style(Ink.Inc, true)), t125[x, newName]);
        Assert.Equal(' ', t125[x + 8, newName].Ch);

        // 250 ms: the old label is normal and " (current)" is gone; the new label has not started.
        var t250 = anim.Render(Geo, 250).Grid;
        Assert.Equal(new Cell('m', new Style(Ink.Inc, true)), t250[x, oldName]);
        Assert.Equal(new Cell('▾', new Style(Ink.Inc)), t250[x, oldMarker]);
        Assert.Equal(' ', t250[x + 5, oldName].Ch);
        Assert.Equal(new Cell('f', new Style(Ink.Inc, true)), t250[x, newName]);
        Assert.Equal(' ', t250[x + 8, newName].Ch);

        // 375 ms: " (current)" is fading onto the new label, which is moving to the current style.
        var t375 = anim.Render(Geo, 375).Grid;
        Assert.Equal(new Cell('f', new Style(Ink.Fg, true)), t375[x, newName]);
        Assert.Equal(new Cell('▴', new Style(Ink.Fg)), t375[x, newMarker]);
        Assert.Equal(new Cell('(', new Style(Ink.Frame)), t375[x + 8, newName]);
        Assert.Equal(' ', t375[x + 5, oldName].Ch);

        // 600 ms: the new label is current with " (current)" in the preview's dim; the old one is normal.
        var t600 = anim.Render(Geo, 600).Grid;
        Assert.Equal(new Cell('f', new Style(Ink.Cur, true)), t600[x, newName]);
        Assert.Equal(new Cell('▴', new Style(Ink.Cur)), t600[x, newMarker]);
        Assert.Equal(new Cell('(', new Style(Ink.Dim)), t600[x + 8, newName]);
        Assert.Equal(new Cell('m', new Style(Ink.Inc, true)), t600[x, oldName]);
        Assert.Equal(' ', t600[x + 5, oldName].Ch);
    }

    [Fact]
    public void Switch_frame_at_0_ms_is_the_browse_preview_cell_for_cell()
    {
        var before = Layouts.Diverged();
        var anim = Switch();
        anim.SetResult(SwitchedOk(), 0);

        Assert.True(SameCells(MergeScenes.Preview(Geo, before).Grid, anim.Render(Geo, 0).Grid));
    }

    [Fact]
    public void Switch_graph_cells_are_identical_in_every_frame_and_no_frame_has_a_diamond()
    {
        var before = Layouts.Diverged();
        var anim = Switch();
        anim.SetResult(SwitchedOk(), 0);
        var rest = MergeScenes.Preview(Geo, before).Grid;

        foreach (var t in new long[] { 0, 100, 125, 166, 249, 250, 300, 375, 450, 499, 500, 550, 599, 600, 2000 })
        {
            var grid = anim.Render(Geo, t).Grid;
            Assert.DoesNotContain('◆', Text(grid));
            for (var y = 0; y < Geo.Height; y++)
                for (var x = 0; x < Geo.Width; x++)
                    if (!IsLabelCell(Geo, x, y))
                        Assert.True(grid[x, y] == rest[x, y], $"graph cell ({x},{y}) changed at {t} ms");
        }
    }

    [Fact]
    public void Switch_final_frame_is_what_the_screen_keeps_and_it_still_shows_the_pre_switch_graph()
    {
        var before = Layouts.Diverged();
        var anim = Switch();
        anim.SetResult(SwitchedOk(), 0);

        var last = anim.Render(Geo, 600);
        var later = anim.Render(Geo, 3_000);

        Assert.True(anim.IsDone(600));
        Assert.True(SameCells(last.Grid, later.Grid));
        Assert.Equal(last.Caption, later.Caption);
        Assert.Equal("Switched", anim.Title(3_000));
        Assert.Equal("feature (current)", Row(last.Grid, Geo.B + 2).Trim());
        Assert.Equal("main", Row(last.Grid, Geo.A - 2).Trim());
        Assert.Equal("✔ Switched to feature", last.Caption?.Text);
    }

    [Fact]
    public void Switch_title_changes_with_the_caption_at_500_ms()
    {
        var anim = Switch();
        anim.SetResult(SwitchedOk(), 0);

        Assert.Equal("Switching", anim.Title(499));
        Assert.Equal("Switched", anim.Title(500));
        Assert.Equal("Switched", anim.Title(600));
    }

    [Fact]
    public void Switch_caption_appears_at_500_ms_and_not_before()
    {
        var anim = Switch();
        anim.SetResult(SwitchedOk(), 0);

        Assert.Equal("⠋ Switching to feature…", anim.Render(Geo, 0).Caption?.Text);
        Assert.EndsWith("Switching to feature…", anim.Render(Geo, 100).Caption?.Text);
        Assert.NotEqual("✔ Switched to feature", anim.Render(Geo, 499).Caption?.Text);
        Assert.Equal("✔ Switched to feature", anim.Render(Geo, 500).Caption?.Text);
    }

    [Fact]
    public void Switch_timeline_starts_when_git_answers_so_the_marker_waits_for_it()
    {
        var anim = Switch();
        Assert.True(SameCells(MergeScenes.Preview(Geo, Layouts.Diverged()).Grid, anim.Render(Geo, 5_000).Grid));

        anim.SetResult(SwitchedOk(), 2_000);

        var x = Geo.TipX(1);
        Assert.Equal('▾', anim.Render(Geo, 2_000).Grid[x, Geo.A - 1].Ch);
        Assert.Equal(new Cell('f', new Style(Ink.Fg, true)), anim.Render(Geo, 2_375).Grid[x, Geo.B + 2]);
        Assert.False(anim.IsDone(2_599));
        Assert.True(anim.IsDone(2_600));
    }

    [Fact]
    public void Switch_that_fails_never_moves_the_marker()
    {
        var before = Layouts.Diverged();
        var anim = Switch();
        var failed = new BranchResult(false, "Failed", new Caption("✕ Switch failed: boom", new Style(Ink.Bad, true)), null);

        anim.SetResult(failed, 100);
        var frame = anim.Render(Geo, 100);

        Assert.True(anim.IsDone(100));
        Assert.True(SameCells(MergeScenes.Preview(Geo, before).Grid, frame.Grid));
        Assert.Equal("✕ Switch failed: boom", frame.Caption?.Text);
        Assert.Equal("Failed", anim.Title(100));
    }

    [Fact]
    public void Switch_refused_by_git_keeps_the_marker_where_it_was()
    {
        var before = Layouts.Diverged();
        var anim = Switch();
        anim.SetResult(new BranchResult(false, "Refused", new Caption("Refused: local changes would be overwritten", new Style(Ink.Warn, true)),
            null), 400);

        var frame = anim.Render(Geo, 400);

        Assert.True(anim.IsDone(400));
        Assert.True(SameCells(MergeScenes.Preview(Geo, before).Grid, frame.Grid));
        Assert.Equal("Refused: local changes would be overwritten", frame.Caption?.Text);
    }

    [Fact]
    public void Switch_is_not_done_before_git_answers()
    {
        var anim = Switch();

        Assert.False(anim.IsDone(50_000));
        Assert.Equal("Switching", anim.Title(50_000));
        Assert.Equal("⠋ Switching to feature…", anim.Render(Geo, 0).Caption?.Text);
    }

    // main and feature point at one commit: one row of labels, the current one above the line and the selected one below it.
    private static GraphLayout SameCommit() =>
        new("main", false, "feature", Layouts.Cur, Layouts.Cur, Layouts.Cur,
            [], false, [], false, [Layouts.C(Layouts.Cur)], false, Layouts.C(Layouts.Cur), Layouts.C(Layouts.Cur), 0, 0, GraphKind.Same);

    // Detached HEAD on the diverged pair: the old label reads HEAD (detached).
    private static GraphLayout Detached() =>
        new("main", true, "feature", Layouts.Cur, Layouts.Sel, Layouts.Shared,
            [Layouts.C(Layouts.Cur)], false, [Layouts.C(Layouts.Sel)], false, [Layouts.C(Layouts.Shared)], false,
            Layouts.C(Layouts.Cur), Layouts.C(Layouts.Sel), 1, 1, GraphKind.Diverged);

    [Fact]
    public void Switch_to_a_branch_on_the_same_commit_moves_current_between_its_two_labels()
    {
        var before = SameCommit();
        var anim = new BranchAnimation(BranchOp.Switch, before, "feature", null, 0, "Switching to feature…");
        anim.SetResult(SwitchedOk(), 0);

        Assert.True(SameCells(MergeScenes.Preview(Geo, before).Grid, anim.Render(Geo, 0).Grid));
        var last = anim.Render(Geo, 600).Grid;
        Assert.Equal("main", Row(last, Geo.A - 2).Trim());
        Assert.Equal("feature (current)", Row(last, Geo.A + 2).Trim());
        Assert.Equal(new Cell('(', new Style(Ink.Dim)), last[Geo.XB + 8, Geo.A + 2]);

        var rest = MergeScenes.Preview(Geo, before).Grid;
        foreach (var t in new long[] { 0, 125, 250, 375, 600 })
        {
            var grid = anim.Render(Geo, t).Grid;
            for (var y = 0; y < Geo.Height; y++)
                for (var x = 0; x < Geo.Width; x++)
                {
                    var label = x >= Geo.XB && x < Geo.XB + 20 && (y == Geo.A - 2 || y == Geo.A - 1 || y == Geo.A + 1 || y == Geo.A + 2);
                    if (!label)
                        Assert.True(grid[x, y] == rest[x, y], $"graph cell ({x},{y}) changed at {t} ms");
                }
        }
    }

    [Fact]
    public void Switch_from_detached_head_fades_the_detached_extra_and_ends_on_the_branch()
    {
        var before = Detached();
        var anim = new BranchAnimation(BranchOp.Switch, before, "feature", null, 0, "Switching to feature…");
        anim.SetResult(SwitchedOk(), 0);
        var x = Geo.TipX(1);

        var t0 = anim.Render(Geo, 0).Grid;
        Assert.True(SameCells(MergeScenes.Preview(Geo, before).Grid, t0));
        Assert.Equal("HEAD (detached)", Row(t0, Geo.A - 2).Trim());
        Assert.Equal(new Cell('(', new Style(Ink.Dim)), t0[x + 5, Geo.A - 2]);

        // The extra fades out in the first half; the name is then normal, and it is never cut or moved.
        Assert.Equal(new Cell('(', new Style(Ink.Frame)), anim.Render(Geo, 125).Grid[x + 5, Geo.A - 2]);
        var mid = anim.Render(Geo, 250).Grid;
        Assert.Equal("HEAD", Row(mid, Geo.A - 2).Trim());
        Assert.Equal(new Cell('H', new Style(Ink.Inc, true)), mid[x, Geo.A - 2]);

        var end = anim.Render(Geo, 600).Grid;
        Assert.Equal("HEAD", Row(end, Geo.A - 2).Trim());
        Assert.Equal("feature (current)", Row(end, Geo.B + 2).Trim());
    }

    [Fact]
    public void Right_edge_name_moves_only_when_the_fade_in_starts_and_never_during_the_wait_or_on_failure()
    {
        // A grid 26 columns wide: "feature" plus " (current)" cannot fit to the right of the tip, so the name's place counts the extra.
        var narrow = new ArtGeometry(26, 11, 6, 3, 7, 16);
        var anim = new BranchAnimation(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");

        // While git has not answered, the name stays where the preview put it.
        Assert.Equal(19, Row(anim.Render(narrow, 5_000).Grid, narrow.B + 2).IndexOf("feature"));

        anim.SetResult(SwitchedOk(), 0);
        foreach (var t in new long[] { 0, 125, 250 })
            Assert.Equal(19, Row(anim.Render(narrow, t).Grid, narrow.B + 2).IndexOf("feature"));

        // The fade-in starts after 250 ms: the name moves left to make room for the extra, and the whole " (current)" is shown.
        Assert.Equal(9, Row(anim.Render(narrow, 375).Grid, narrow.B + 2).IndexOf("feature"));
        Assert.Equal("feature (current)", Row(anim.Render(narrow, 600).Grid, narrow.B + 2).Substring(9, 17));

        var failing = new BranchAnimation(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");
        failing.SetResult(new BranchResult(false, "Failed", new Caption("✕ Switch failed", new Style(Ink.Bad, true)), null), 0);
        Assert.Equal(19, Row(failing.Render(narrow, 0).Grid, narrow.B + 2).IndexOf("feature"));
    }

    // ----- create -----

    [Fact]
    public void Create_at_350_ms_has_typed_about_half_the_name()
    {
        var anim = new BranchAnimation(BranchOp.Create, Alone("main", Layouts.Cur), "topic", null, 0, "Creating topic…");

        var frame = anim.Render(Geo, 350);

        var typed = Row(frame.Grid, Geo.A + 2).Trim();
        Assert.Equal("top", typed);
        Assert.Equal('▴', frame.Grid[Geo.XB, Geo.A + 1].Ch);
    }

    [Fact]
    public void Create_ends_with_the_new_branch_as_current()
    {
        var anim = new BranchAnimation(BranchOp.Create, Alone("main", Layouts.Cur), "topic", null, 0, "Creating topic…");
        anim.SetResult(Success(Alone("topic", Layouts.Cur), "Branch created", "✔ Created topic at c111111 and switched to it"), 100);

        var frame = anim.Render(Geo, 2_000);

        Assert.Equal("topic (current)", Row(frame.Grid, Geo.A - 2).Trim());
        Assert.Equal("Branch created", anim.Title(2_000));
    }

    // ----- rename -----

    [Fact]
    public void Rename_morphs_the_old_name_into_the_new_one_letter_by_letter()
    {
        var before = Layouts.Diverged();
        var anim = new BranchAnimation(BranchOp.Rename, before, "feature", "topic", 0, "Renaming…");

        // 350 ms is half of the 700 ms morph over the longer name (7 letters): four letters of the new name, the rest of the old.
        var start = Row(anim.Render(Geo, 0).Grid, Geo.B + 2).Trim();
        var mid = Row(anim.Render(Geo, 350).Grid, Geo.B + 2).Trim();
        var end = Row(anim.Render(Geo, 700).Grid, Geo.B + 2).Trim();

        Assert.Equal("feature", start);
        Assert.Equal("topiure", mid);
        Assert.Equal("topic", end);
    }

    [Fact]
    public void Rename_of_the_current_branch_shows_current_after_the_new_name()
    {
        var before = Alone("main", Layouts.Cur);
        var anim = new BranchAnimation(BranchOp.Rename, before, "main", "trunk", 0, "Renaming…");
        anim.SetResult(Success(Alone("trunk", Layouts.Cur), "Renamed", "✔ Renamed main to trunk"), 100);

        var frame = anim.Render(Geo, 2_000);

        Assert.Equal("trunk (current)", Row(frame.Grid, Geo.A - 2).Trim());
        Assert.Equal("Renamed", anim.Title(2_000));
    }

    // ----- delete -----

    [Fact]
    public void Delete_at_950_ms_shows_the_ghost_and_no_name()
    {
        var anim = new BranchAnimation(BranchOp.Delete, Layouts.Diverged(), "feature", null, 1, "Deleting feature…");
        anim.SetResult(Success(Alone("main", Layouts.Cur), "Deleted", "✔ Deleted feature"), 300);

        var frame = anim.Render(Geo, 950);

        Assert.Equal('◌', frame.Grid[Geo.TipX(1), Geo.B].Ch);
        Assert.DoesNotContain("feature", Text(frame.Grid));
        Assert.Equal(' ', frame.Grid[Geo.TipX(1), Geo.B + 1].Ch);
    }

    [Fact]
    public void Delete_shows_the_name_fading_and_no_ghost_when_nothing_is_lost()
    {
        var anim = new BranchAnimation(BranchOp.Delete, Layouts.Diverged(), "feature", null, 0, "Deleting feature…");

        var frame = anim.Render(Geo, 200);

        Assert.Contains("feature", Row(frame.Grid, Geo.B + 2));
        Assert.Equal(0, CountOf(frame.Grid, '◌'));
        Assert.Equal('▴', frame.Grid[Geo.TipX(1), Geo.B + 1].Ch);
    }

    [Fact]
    public void Delete_ends_on_the_newly_selected_branch()
    {
        var anim = new BranchAnimation(BranchOp.Delete, Layouts.Diverged(), "feature", null, 0, "Deleting feature…");
        anim.SetResult(Success(Alone("main", Layouts.Cur), "Deleted", "✔ Deleted feature (was d222222)"), 300);

        var frame = anim.Render(Geo, 2_000);

        Assert.Equal("main (current)", Row(frame.Grid, Geo.A - 2).Trim());
        Assert.Equal("Deleted", anim.Title(2_000));
    }

    // ----- failures -----

    [Fact]
    public void A_failed_result_jumps_to_its_final_frame_with_the_overlay()
    {
        var anim = new BranchAnimation(BranchOp.Delete, Layouts.Diverged(), "feature", null, 0, "Deleting feature…");
        var failed = new BranchResult(false, "Refused", new Caption("Refused: main is the current branch", new Style(Ink.Bad, true)), null,
            "Refused", "main is the current branch: switch to another branch before deleting it");

        anim.SetResult(failed, 100);
        var frame = anim.Render(Geo, 100);

        Assert.True(anim.IsDone(100));
        Assert.Equal("Refused: main is the current branch", frame.Caption?.Text);
        Assert.Contains("Refused", Text(frame.Grid));
        Assert.Contains("current branch", Text(frame.Grid));
        Assert.Equal("Refused", anim.Title(100));
    }

    [Fact]
    public void Running_caption_turns_into_the_waiting_line_after_five_seconds()
    {
        var anim = Switch();

        Assert.Equal(MergeScenes.WaitingText, anim.Render(Geo, 5_000).Caption?.Text);
    }
}
