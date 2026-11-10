using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The paused rebase's picture, drawn from its stop alone, and the art while a pause is continued. The geometry is the 9-row art at 60
/// columns (step 6, the base at column 16, row A at 2 and row B at 6). Four plan rows: the stop is at step 3 unless a test says otherwise.
/// </summary>
public class RebasePausedSceneTests
{
    private static readonly Style Dim = new(Ink.Dim), BadB = new(Ink.Bad, true), WarnB = new(Ink.Warn, true);
    private static readonly ArtGeometry G = ArtGeometry.For(60, 9)!;

    private static RebaseRow Row(string h, RebaseAction a = RebaseAction.Pick) => new(h, h, "subject " + h, a);

    private static IReadOnlyList<RebaseRow> Four(params (int Index, RebaseAction Action)[] changes)
    {
        var rows = new[] { Row("a1"), Row("a2"), Row("a3"), Row("a4") }.ToList();
        foreach (var (index, action) in changes)
            rows[index] = rows[index] with { Action = action };
        return rows;
    }

    private static SequenceStop Stop(PauseKind pause, int step, IReadOnlyList<RebaseRow> rows) =>
        new(SequenceKind.Rebase, pause, "feature", "main", "a3", "a3", "subject a3", [new ConflictFile("app.txt", false)], step, rows.Count,
            "old message", [], Ours: true, Rows: rows);

    private static ArtGrid Paint(SequenceStop stop) => RebaseScenes.Paused(G, stop, Ink.Lane0, Ink.Lane1).Grid;

    private static string RowText(ArtGrid grid, int y) => string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    [Fact]
    public void A_conflict_draws_the_landed_commits_and_a_bad_cross_one_cell_short_of_the_next_slot()
    {
        var grid = Paint(Stop(PauseKind.Conflict, 3, Four()));

        Assert.Equal('●', grid[16, 2].Ch);
        Assert.Equal('╰', grid[16, 6].Ch);
        Assert.Equal('●', grid[22, 6].Ch);
        Assert.Equal('●', grid[28, 6].Ch);
        Assert.Equal('●', grid[34, 6].Ch);
        Assert.Equal('━', grid[36, 6].Ch);
        Assert.Equal(new Style(Ink.Bad), grid[36, 6].Style);
        Assert.Equal('✕', grid[39, 6].Ch);
        Assert.Equal(BadB, grid[39, 6].Style);
        Assert.Equal(' ', grid[40, 6].Ch);
    }

    [Fact]
    public void Row_B_runs_a_line_from_the_onto_corner_to_the_onto_tip_in_the_onto_style()
    {
        var grid = Paint(Stop(PauseKind.Conflict, 3, Four()));

        Assert.Equal('╰', grid[16, 6].Ch);
        for (var x = 17; x <= 21; x++)
        {
            Assert.Equal('─', grid[x, 6].Ch);
            Assert.Equal(new Style(Ink.Lane1), grid[x, 6].Style);
        }
        Assert.Equal('●', grid[22, 6].Ch);
    }

    [Fact]
    public void Row_A_has_the_base_and_one_dim_ghost_per_plan_row()
    {
        var grid = Paint(Stop(PauseKind.Conflict, 3, Four()));

        Assert.Equal(Dim, grid[16, 2].Style);
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal('◌', grid[16 + 6 * i, 2].Ch);
            Assert.Equal(Dim, grid[16 + 6 * i, 2].Style);
        }
    }

    [Fact]
    public void The_branch_is_labelled_above_the_stop_with_its_progress_and_the_onto_branch_below_the_onto_tip()
    {
        var grid = Paint(Stop(PauseKind.Conflict, 3, Four()));

        Assert.Equal('▾', grid[39, 5].Ch);
        Assert.Contains("feature (rebasing 3/4)", RowText(grid, 4), StringComparison.Ordinal);
        Assert.Equal('▴', grid[22, 7].Ch);
        Assert.Contains("main", RowText(grid, 8), StringComparison.Ordinal);
    }

    [Fact]
    public void An_edit_stop_is_a_warm_dot_in_its_slot_with_no_cross()
    {
        var grid = Paint(Stop(PauseKind.Edit, 3, Four()));

        Assert.Equal('●', grid[40, 6].Ch);
        Assert.Equal(WarnB, grid[40, 6].Style);
        Assert.DoesNotContain(RowText(grid, 6), "✕", StringComparison.Ordinal);
        Assert.Equal('─', grid[38, 6].Ch);
    }

    [Fact]
    public void A_reword_stop_turns_its_own_ghost_on_row_A_warm_and_draws_nothing_past_the_onto_tip()
    {
        var grid = Paint(Stop(PauseKind.Reword, 1, Four((0, RebaseAction.Reword))));

        Assert.Equal('●', grid[22, 2].Ch);
        Assert.Equal(WarnB, grid[22, 2].Style);
        Assert.Equal('▾', grid[22, 1].Ch);
        Assert.Equal(' ', grid[28, 6].Ch);
    }

    [Fact]
    public void A_squash_stop_is_drawn_like_a_reword_stop_on_its_own_ghost()
    {
        var grid = Paint(Stop(PauseKind.Squash, 2, Four((1, RebaseAction.Squash))));

        Assert.Equal('●', grid[28, 2].Ch);
        Assert.Equal(WarnB, grid[28, 2].Style);
    }

    [Fact]
    public void A_stop_with_no_plan_rows_draws_an_empty_art()
    {
        var grid = Paint(Stop(PauseKind.Conflict, 1, []));

        Assert.All(Enumerable.Range(0, grid.Height), y => Assert.DoesNotContain("●", RowText(grid, y), StringComparison.Ordinal));
    }

    private static IReadOnlyList<RebaseRow> Eight() => Enumerable.Range(1, 8).Select(i => Row("a" + i)).ToList();

    [Fact]
    public void A_landing_longer_than_three_commits_is_drawn_as_three_and_a_dim_tail_and_the_stop_stays_on_the_art()
    {
        // Step 7 of 8: six commits have landed, so the stop's slot would be off the 60-column art if every landed commit were drawn.
        var grid = Paint(Stop(PauseKind.Conflict, 7, Eight()));

        Assert.Equal('●', grid[40, 6].Ch);
        Assert.Equal('┄', grid[42, 6].Ch);
        Assert.Equal(Dim, grid[42, 6].Style);
        Assert.Equal('━', grid[44, 6].Ch);
        Assert.Equal('✕', grid[45, 6].Ch);
        Assert.Equal(BadB, grid[45, 6].Style);
    }

    [Fact]
    public void A_warm_edit_stop_after_a_long_landing_sits_in_its_slot_on_the_art()
    {
        var grid = Paint(Stop(PauseKind.Edit, 8, Eight()));

        Assert.Equal('●', grid[46, 6].Ch);
        Assert.Equal(WarnB, grid[46, 6].Style);
    }

    [Fact]
    public void A_reword_stop_after_four_steps_of_eight_keeps_its_warm_dot_and_label_on_row_A()
    {
        // Step 5 of 8: the stop is the fifth ghost, which the old four-ghost cap dropped.
        var grid = Paint(Stop(PauseKind.Reword, 5, Eight().Select((r, i) => i == 4 ? r with { Action = RebaseAction.Reword } : r).ToList()));

        Assert.Equal('●', grid[46, 2].Ch);
        Assert.Equal(WarnB, grid[46, 2].Style);
        Assert.Equal('▾', grid[46, 1].Ch);
        Assert.Contains("feature (rebasing 5/8)", RowText(grid, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_squash_stop_past_the_last_ghost_the_art_has_room_for_moves_the_window_so_it_is_still_drawn()
    {
        // The 60-column art has room for seven ghosts. Step 8 of 8 is the eighth row, so the window starts at row two and the base is a ┄.
        var rows = Eight().Select((r, i) => i == 7 ? r with { Action = RebaseAction.Squash } : r).ToList();
        var grid = Paint(Stop(PauseKind.Squash, 8, rows));

        Assert.Equal('┄', grid[16, 2].Ch);
        Assert.Equal('●', grid[58, 2].Ch);
        Assert.Equal(WarnB, grid[58, 2].Style);
        Assert.Contains("feature (rebasing 8/8)", RowText(grid, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_plan_keeps_the_base_at_the_start_of_row_A()
    {
        var grid = Paint(Stop(PauseKind.Conflict, 1, Four()));

        Assert.Equal('●', grid[16, 2].Ch);
        Assert.Equal('◌', grid[22, 2].Ch);
    }

    [Fact]
    public void The_running_art_is_the_paused_picture_with_the_running_caption_then_the_result()
    {
        var stop = Stop(PauseKind.Conflict, 3, Four());
        var anim = new PausedAnimation(stop, RebaseText.Running("feature", "main"), Ink.Lane0, Ink.Lane1);

        Assert.Equal(RebaseText.RunTitle, anim.Title(0));
        Assert.False(anim.IsDone(0));
        var running = anim.Render(G, 0);
        Assert.EndsWith("Rebasing feature onto main…", running.Caption!.Text, StringComparison.Ordinal);
        Assert.Equal('✕', running.Grid[39, 6].Ch);

        anim.SetResult(new BranchResult(true, RebaseText.PausedTitle, new Caption("kept", WarnB), null), 10);
        var done = anim.Render(G, 20);
        Assert.True(anim.IsDone(20));
        Assert.Equal(RebaseText.PausedTitle, anim.Title(20));
        Assert.Equal("kept", done.Caption!.Text);
        Assert.Equal('✕', done.Grid[39, 6].Ch);
    }
}
