using Graft.Model;
using Graft.Ui.Scenes;

namespace Graft.Tests;

public class MergeScenesTests
{
    private static readonly ArtGeometry Geo = ArtGeometry.For(80, 11)!;

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
    public void Refused_overlay_writes_the_reason_on_one_row()
    {
        var grid = new ArtGrid(80, 11);

        MergeScenes.RefusedOverlay(grid, "working tree has uncommitted changes");

        Assert.Contains(Enumerable.Range(0, grid.Height), y => RowText(grid, y).Contains("Refused", StringComparison.Ordinal));
    }

    private static string RowText(ArtGrid grid, int y) =>
        new(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch).ToArray());
}
