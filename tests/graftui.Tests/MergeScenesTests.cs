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
}
