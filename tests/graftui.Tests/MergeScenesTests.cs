using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class MergeScenesTests
{
    private static readonly ArtGeometry Geo = ArtGeometry.For(80, 11)!;

    // The Branches tab's 9-row art: the same graph with no empty row above or below.
    private static readonly ArtGeometry Geo9 = ArtGeometry.For(80, 9)!;

    // Where the two branch lines join on the 80x11 grid.
    private const int JoinX = 28;
    private const int JoinY = 3;

    private static readonly MergeOutcome Conflict =
        new(MergeKind.Conflict, "feature", "main", null, ["app.txt"], "merge aborted", []);

    [Fact]
    public void Diverged_merge_before_git_answers_shows_a_pending_join()
    {
        var frame = MergeScenes.Merge(Geo, Layouts.Diverged(), 500, 500, null, false);

        Assert.Equal('◌', frame.Grid[JoinX, JoinY].Ch);
    }

    [Fact]
    public void Conflict_draws_a_cross_at_the_join_and_names_the_file_in_the_caption()
    {
        var mid = MergeScenes.Merge(Geo, Layouts.Diverged(), 1100, 1100, Conflict, false);
        Assert.Equal('✕', mid.Grid[JoinX, JoinY].Ch);

        var end = MergeScenes.Merge(Geo, Layouts.Diverged(), 2250, 2250, Conflict, false);
        Assert.Equal('✕', end.Grid[JoinX, JoinY].Ch);
        Assert.Equal("✕ CONFLICT in app.txt: merge aborted, main unchanged", end.Caption?.Text);
    }

    [Fact]
    public void Merge_commit_draws_a_merge_dot_and_captions_the_new_commit()
    {
        var outcome = new MergeOutcome(MergeKind.MergeCommit, "feature", "main", "e333333333", [], "merge commit created", []);

        var frame = MergeScenes.Merge(Geo, Layouts.Diverged(), 1650, 1650, outcome, false);

        Assert.Equal('◉', frame.Grid[JoinX, JoinY].Ch);
        Assert.Equal("✔ Merge commit e333333 created on main", frame.Caption?.Text);
    }

    [Fact]
    public void Fast_forward_captions_the_new_tip()
    {
        var outcome = new MergeOutcome(MergeKind.FastForward, "feature", "main", Layouts.Sel, [], "fast-forward", []);

        var frame = MergeScenes.FastForward(Geo, Layouts.FastForward(), 1600, 1600, outcome, false);

        Assert.Equal("✔ Fast-forward: main is now at d222222 (+1 commit, no merge commit)", frame.Caption?.Text);
    }

    [Fact]
    public void Up_to_date_says_the_branch_is_already_part_of_the_current_one()
    {
        var outcome = new MergeOutcome(MergeKind.UpToDate, "feature", "main", Layouts.Cur, [], "already up to date", []);

        var frame = MergeScenes.UpToDate(Geo, Layouts.UpToDate(), 1000, 1000, outcome, false);

        Assert.Equal("Already up to date: feature is already part of main", frame.Caption?.Text);
    }

    [Fact]
    public void The_nine_row_preview_labels_the_current_branch_on_the_first_row_and_the_selected_one_on_the_last()
    {
        var frame = MergeScenes.Preview(Geo9, Layouts.Diverged());

        Assert.Contains("main", RowText(frame.Grid, 0), StringComparison.Ordinal);
        Assert.Contains("feature", RowText(frame.Grid, 8), StringComparison.Ordinal);
    }

    [Fact]
    public void The_nine_row_merge_commit_is_drawn_on_the_join_row()
    {
        var outcome = new MergeOutcome(MergeKind.MergeCommit, "feature", "main", "e333333333", [], "merge commit created", []);

        var frame = MergeScenes.Merge(Geo9, Layouts.Diverged(), 2250, 2250, outcome, false);

        Assert.Contains('◉', RowText(frame.Grid, 2));
    }

    [Fact]
    public void The_nine_row_conflict_draws_its_cross_on_the_join_row()
    {
        var frame = MergeScenes.Merge(Geo9, Layouts.Diverged(), 2250, 2250, Conflict, false);

        Assert.Contains('✕', RowText(frame.Grid, 2));
    }

    [Fact]
    public void A_refusal_box_with_three_lines_fits_inside_the_nine_row_art()
    {
        var grid = new ArtGrid(80, 9);

        MergeScenes.MessageOverlay(grid, "Refused", string.Concat(Enumerable.Repeat("a reason that runs on for a while ", 4)) + "end.");

        Assert.Contains("Refused", BoxText(grid), StringComparison.Ordinal);
        Assert.Contains(Enumerable.Range(0, grid.Height), y => RowText(grid, y).Contains('└'));
    }

    [Fact]
    public void Refused_overlay_writes_the_reason_on_one_row()
    {
        var grid = new ArtGrid(80, 11);

        MergeScenes.RefusedOverlay(grid, "working tree has uncommitted changes");

        Assert.Contains(Enumerable.Range(0, grid.Height), y => RowText(grid, y).Contains("Refused", StringComparison.Ordinal));
    }

    /// <summary>The art at 64x22 is 36x8: the unmerged delete question must read in full there, and a short form must take over when it cannot.</summary>
    [Fact]
    public void The_unmerged_delete_question_reads_in_full_in_the_64_by_22_art()
    {
        var grid = Art(36, 8);
        const string text = "feature/some-branch has 12 commits that main doesn't have. Deleting it needs git branch -D; "
            + "graftui records where it pointed, so u can bring it back.";

        MergeScenes.MessageOverlay(grid, "Delete", text);

        Assert.Contains("u can bring it back.", BoxText(grid));
    }

    [Fact]
    public void A_text_too_long_for_the_art_uses_the_short_form_rather_than_losing_its_end()
    {
        var grid = Art(36, 8);
        var text = string.Concat(Enumerable.Repeat("a long explanation that repeats itself ", 6)) + "end.";

        MergeScenes.MessageOverlay(grid, "Delete", text, "topic: 2 commits main doesn't have. Needs git branch -D; u brings it back.");

        Assert.Contains("u brings it back.", BoxText(grid));
        Assert.DoesNotContain("explanation", BoxText(grid));
    }

    /// <summary>Nothing from the art shows beside the box: every cell in the box's rows and outside its columns is blank.</summary>
    [Fact]
    public void The_overlay_leaves_nothing_of_the_art_beside_the_box()
    {
        var grid = Art(36, 8);

        MergeScenes.MessageOverlay(grid, "Uncommitted changes", "You have 3 uncommitted changes. Stash them, merge topic into main, then pop them back?");

        var top = Enumerable.Range(0, grid.Height).First(y => RowText(grid, y).Contains('┌'));
        var bottom = Enumerable.Range(0, grid.Height).Last(y => RowText(grid, y).Contains('└'));
        var left = RowText(grid, top).IndexOf('┌');
        var right = RowText(grid, top).IndexOf('┐');
        for (var y = top; y <= bottom; y++)
            for (var x = 0; x < grid.Width; x++)
                if (x < left || x > right)
                    Assert.Equal(' ', grid[x, y].Ch);
    }

    private static ArtGrid Art(int width, int height)
    {
        var grid = new ArtGrid(width, height);
        for (var y = 0; y < height; y++)
            grid.Text(0, y, new string('a', width), new Style(Ink.Fg));
        return grid;
    }

    /// <summary>The text inside the box's sides, one line per row, joined with single spaces.</summary>
    private static string BoxText(ArtGrid grid)
    {
        var rows = Enumerable.Range(0, grid.Height)
            .Select(y => RowText(grid, y))
            .Where(r => r.Contains('│'))
            .Select(r => r[(r.IndexOf('│') + 1)..r.LastIndexOf('│')].Trim());
        return string.Join(" ", rows).Replace("  ", " ");
    }

    private static string RowText(ArtGrid grid, int y) =>
        new(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch).ToArray());

    // ---------- branch colours ----------

    private static readonly GraphLayout Lanes03 = Layouts.Diverged() with { CurrentLane = 0, SelectedLane = 3 };

    // The label's name starts where the scene places it: its extra counts with it when the current label is drawn.
    private static int CurrentNameX(ArtGeometry g, GraphLayout L) =>
        MergeScenes.LabelTextX(g, MergeScenes.LabelSpots(g, L).CurX, MergeScenes.Runes(L.Current) + " (current)".Length);

    private static int SelectedNameX(ArtGeometry g, GraphLayout L) =>
        MergeScenes.LabelTextX(g, MergeScenes.LabelSpots(g, L).SelX, MergeScenes.Runes(L.Selected));

    // Every drawn (non-blank) cell of one row, from fromX on, has this ink.
    private static void AssertRowIs(ArtGrid grid, int y, Ink ink, int fromX = 0)
    {
        for (var x = fromX; x < grid.Width; x++)
        {
            var cell = grid[x, y];
            if (cell.Ch != ' ')
                Assert.True(cell.Style.Ink == ink, $"({x},{y}) '{cell.Ch}' is {cell.Style.Ink}, expected {ink}");
        }
    }

    [Fact]
    public void Diverged_preview_draws_each_line_and_its_label_in_the_branch_lane()
    {
        var grid = MergeScenes.Preview(Geo9, Lanes03).Grid;
        var s = MergeScenes.LabelSpots(Geo9, Lanes03);

        // Current: its own row (right of the dim shared history), its marker and its label's name.
        AssertRowIs(grid, Geo9.A, Ink.Lane0, Geo9.XB + 1);
        Assert.Equal(Ink.Lane0, grid[s.CurX, s.CurRow - 1].Style.Ink);
        Assert.Equal(Ink.Lane0, grid[CurrentNameX(Geo9, Lanes03), s.CurRow - 2].Style.Ink);

        // Selected: its row, the join, its marker and its label's name.
        AssertRowIs(grid, Geo9.B, Ink.Lane3);
        Assert.Equal(Ink.Lane3, grid[Geo9.XB, Geo9.A + 1].Style.Ink);
        Assert.Equal(Ink.Lane3, grid[s.SelX, s.SelRow + 1].Style.Ink);
        Assert.Equal(Ink.Lane3, grid[SelectedNameX(Geo9, Lanes03), s.SelRow + 2].Style.Ink);
    }

    [Fact]
    public void Fast_forward_preview_draws_the_selected_tip_in_its_lane_and_the_current_label_in_the_current_lane()
    {
        var L = Layouts.FastForward() with { CurrentLane = 0, SelectedLane = 3 };
        var grid = MergeScenes.Preview(Geo9, L).Grid;
        var s = MergeScenes.LabelSpots(Geo9, L);

        Assert.Equal(Ink.Lane3, grid[s.SelX, Geo9.A].Style.Ink);
        Assert.Equal(Ink.Dim, grid[Geo9.XB, Geo9.A].Style.Ink);
        Assert.Equal(Ink.Lane0, grid[s.CurX, s.CurRow - 1].Style.Ink);
        Assert.Equal(Ink.Lane0, grid[CurrentNameX(Geo9, L), s.CurRow - 2].Style.Ink);
        Assert.Equal(Ink.Lane3, grid[SelectedNameX(Geo9, L), s.SelRow + 2].Style.Ink);
    }

    [Fact]
    public void Up_to_date_preview_draws_the_current_tip_in_its_lane()
    {
        var L = Layouts.UpToDate() with { CurrentLane = 0, SelectedLane = 3 };
        var grid = MergeScenes.Preview(Geo9, L).Grid;
        var s = MergeScenes.LabelSpots(Geo9, L);

        Assert.Equal(Ink.Lane0, grid[s.CurX, Geo9.A].Style.Ink);
        Assert.Equal(Ink.Lane0, grid[CurrentNameX(Geo9, L), s.CurRow - 2].Style.Ink);
    }

    [Fact]
    public void A_branch_keeps_its_lane_when_it_becomes_the_current_branch()
    {
        // Before: main is current (lane 0), feature is selected (lane 3). After: feature is current, main is selected.
        var before = Lanes03;
        var after = new GraphLayout("feature", false, "main", Layouts.Sel, Layouts.Cur, Layouts.Shared,
            [Layouts.C(Layouts.Sel)], false, [Layouts.C(Layouts.Cur)], false, [Layouts.C(Layouts.Shared)], false,
            Layouts.C(Layouts.Sel), Layouts.C(Layouts.Cur), 1, 1, GraphKind.Diverged) with { CurrentLane = 3, SelectedLane = 0 };

        var beforeGrid = MergeScenes.Preview(Geo9, before).Grid;
        var afterGrid = MergeScenes.Preview(Geo9, after).Grid;

        // feature's own line is on row B before, and on row A after: the same ink either way.
        Assert.Equal(Ink.Lane3, beforeGrid[Geo9.TipX(1), Geo9.B].Style.Ink);
        Assert.Equal(Ink.Lane3, afterGrid[Geo9.TipX(1), Geo9.A].Style.Ink);
        // main's line keeps lane 0.
        Assert.Equal(Ink.Lane0, beforeGrid[Geo9.TipX(1), Geo9.A].Style.Ink);
        Assert.Equal(Ink.Lane0, afterGrid[Geo9.TipX(1), Geo9.B].Style.Ink);
    }

    [Fact]
    public void An_unstamped_layout_draws_the_branch_parts_in_the_plain_foreground()
    {
        var grid = MergeScenes.Preview(Geo9, Layouts.Diverged()).Grid;

        Assert.Equal(Ink.Fg, grid[Geo9.TipX(1), Geo9.A].Style.Ink);
        Assert.Equal(Ink.Fg, grid[Geo9.TipX(1), Geo9.B].Style.Ink);
    }

    [Fact]
    public void A_detached_head_label_is_in_the_foreground_while_the_selected_branch_keeps_its_lane()
    {
        var L = Layouts.Diverged() with { Current = "", Detached = true, CurrentLane = BranchLanes.None, SelectedLane = 3 };
        var grid = MergeScenes.Preview(Geo9, L).Grid;
        var s = MergeScenes.LabelSpots(Geo9, L);

        Assert.Equal(Ink.Fg, grid[s.CurX, s.CurRow - 1].Style.Ink);
        Assert.Equal(Ink.Lane3, grid[SelectedNameX(Geo9, L), s.SelRow + 2].Style.Ink);
    }

    [Fact]
    public void The_fast_forward_sweep_turns_each_commit_from_the_selected_lane_to_the_current_lane()
    {
        var L = Layouts.FastForward() with { CurrentLane = 0, SelectedLane = 3 };
        var outcome = new MergeOutcome(MergeKind.FastForward, "feature", "main", Layouts.Sel, [], "fast-forward", []);
        var tip = Geo9.TipX(1);

        // Before the tip has moved, the commit is still the selected branch's.
        var start = MergeScenes.FastForward(Geo9, L, 0, 0, outcome, false).Grid;
        Assert.Equal(Ink.Lane3, start[tip, Geo9.A].Style.Ink);

        // Once the sweep has reached it, it is the current branch's.
        var end = MergeScenes.FastForward(Geo9, L, MergeAnimation.FfGrow + MergeAnimation.FfFlash + 10, 0, outcome, false).Grid;
        Assert.Equal(Ink.Lane0, end[tip, Geo9.A].Style.Ink);
    }

    [Fact]
    public void A_merge_commit_keeps_its_gold_and_moves_the_current_label_onto_it_in_the_current_lane()
    {
        var outcome = new MergeOutcome(MergeKind.MergeCommit, "feature", "main", "e333333333", [], "merge commit created", []);
        var te = MergeAnimation.MergeGrow + MergeAnimation.MergeFlash + 10;

        var frame = MergeScenes.Merge(Geo, Lanes03, te, 0, outcome, false);
        var join = frame.Grid[JoinX, JoinY];
        var labelX = MergeScenes.LabelTextX(Geo, JoinX, MergeScenes.Runes(Lanes03.Current) + " (current)".Length);

        Assert.Equal('◉', join.Ch);
        Assert.Equal(Ink.Mrg, join.Style.Ink);
        Assert.Equal(Ink.Lane0, frame.Grid[labelX, JoinY - 2].Style.Ink);
        Assert.Equal(Ink.Lane3, frame.Grid[JoinX, Geo.B].Style.Ink);
        Assert.Equal(Ink.Lane0, frame.Grid[JoinX - 1, Geo.A].Style.Ink);
    }

    // ---------- dimmed shared history ----------

    // Three shared commits with older history left out: at step 6 with the base at 16, the dots sit at 4, 10 and 16.
    private static readonly GraphLayout Shared3 = Layouts.Diverged() with
    {
        Shared = [Layouts.C("a000000000"), Layouts.C("a111111111"), Layouts.C(Layouts.Shared)],
        SharedMore = true,
    };

    [Fact]
    public void Shared_commits_are_dim_small_dots_on_dim_segments_with_a_dim_tail()
    {
        var grid = MergeScenes.Preview(Geo9, Shared3).Grid;
        var dim = new Style(Ink.Dim);

        Assert.Equal(new Cell('·', dim), grid[4, Geo9.A]);
        Assert.Equal(new Cell('·', dim), grid[10, Geo9.A]);
        Assert.Equal(new Cell('·', dim), grid[16, Geo9.A]);
        Assert.Equal(new Cell('─', dim), grid[7, Geo9.A]);
        Assert.Equal(new Cell('─', dim), grid[13, Geo9.A]);
        Assert.Equal(new Cell('┄', dim), grid[2, Geo9.A]);
    }

    [Fact]
    public void The_own_commits_and_the_segment_from_the_fork_point_stay_bright()
    {
        var grid = MergeScenes.Preview(Geo9, Shared3).Grid;

        Assert.Equal('●', grid[Geo9.TipX(1), Geo9.A].Ch);
        Assert.NotEqual(Ink.Dim, grid[Geo9.TipX(1), Geo9.A].Style.Ink);
        Assert.NotEqual(Ink.Dim, grid[Geo9.TipX(1) - 2, Geo9.A].Style.Ink);
    }

    [Fact]
    public void The_shared_label_sits_under_the_dots_clear_of_the_fork_points_column()
    {
        var grid = MergeScenes.Preview(Geo9, Shared3).Grid;
        var row = RowText(grid, Geo9.A + 1);

        Assert.Contains(MergeScenes.SharedLabel, row, StringComparison.Ordinal);
        Assert.Equal(new Style(Ink.Dim), grid[row.IndexOf(MergeScenes.SharedLabel, StringComparison.Ordinal), Geo9.A + 1].Style);
        // The two cells left of the connector stay blank, and the connector is still drawn.
        Assert.Equal(' ', grid[Geo9.XB - 2, Geo9.A + 1].Ch);
        Assert.Equal(' ', grid[Geo9.XB - 1, Geo9.A + 1].Ch);
        Assert.Equal('│', grid[Geo9.XB, Geo9.A + 1].Ch);
    }

    [Fact]
    public void No_shared_commits_means_no_shared_label()
    {
        var unrelated = Layouts.Diverged() with { Shared = [], MergeBase = null, Kind = GraphKind.Unrelated };

        var grid = MergeScenes.Preview(Geo9, unrelated).Grid;

        Assert.DoesNotContain(MergeScenes.SharedLabel, RowText(grid, Geo9.A + 1), StringComparison.Ordinal);
    }

    [Fact]
    public void A_branch_shown_alone_keeps_its_history_bright_with_no_shared_label()
    {
        var same = Shared3 with { Selected = Shared3.Current, SelectedTip = Shared3.CurrentTip, Kind = GraphKind.Same };

        var grid = MergeScenes.Preview(Geo9, same).Grid;

        Assert.DoesNotContain(MergeScenes.SharedLabel, RowText(grid, Geo9.A + 1), StringComparison.Ordinal);
        Assert.DoesNotContain("·", RowText(grid, Geo9.A), StringComparison.Ordinal);
        Assert.NotEqual(new Style(Ink.Dim), grid[Geo9.XB, Geo9.A].Style);
    }

    [Fact]
    public void The_narrowest_art_still_fits_the_shared_label()
    {
        var narrow = ArtGeometry.For(40, 9)!;

        var grid = MergeScenes.Preview(narrow, Layouts.Diverged()).Grid;

        Assert.Contains(MergeScenes.SharedLabel, RowText(grid, narrow.A + 1), StringComparison.Ordinal);
        Assert.Equal('│', grid[narrow.XB, narrow.A + 1].Ch);
    }

    [Fact]
    public void A_long_ascii_file_name_in_a_conflict_caption_keeps_its_last_23_characters()
    {
        var name = "a-very-long-directory-name/and-a-file-name-that-goes-on.txt";

        var text = MergeScenes.ConflictFiles([name]);

        Assert.Equal("…" + name[^23..], text);
    }

    [Fact]
    public void A_wide_file_name_in_a_conflict_caption_is_shortened_by_columns_without_throwing()
    {
        // 13 wide characters are 26 columns but only 13 UTF-16 units: a cut by units would run past the start of the name.
        var name = new string('日', 13) + ".txt";

        var text = MergeScenes.ConflictFiles([name]);

        Assert.StartsWith("…", text);
        Assert.EndsWith(".txt", text);
        Assert.True(TextFit.Columns(text) <= 24);
    }
}
