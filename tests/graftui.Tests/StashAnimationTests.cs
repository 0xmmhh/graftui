using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class StashAnimationTests
{
    private static readonly ArtGeometry Geo = ArtGeometry.For(69, 11)!;

    private static FileChange Unstaged(string path, ChangeKind kind = ChangeKind.Modified) => new(path, null, ChangeSide.Unstaged, kind);

    private static FileChange Staged(string path, ChangeKind kind = ChangeKind.Modified) => new(path, null, ChangeSide.Staged, kind);

    private static HeadLine Head() => new("main", "a111111", [Layouts.C("a111111111", 1, "Add app")], false);

    private static AreasModel Model(int stashCount, IReadOnlyList<FileChange>? unstaged = null, IReadOnlyList<FileChange>? staged = null) =>
        new(unstaged ?? [], staged ?? [], Head(), new StashSummary(stashCount, "a1b2c3d", "On main: wip"));

    private static AreasResult Success(AreasModel after, string title, string text, IReadOnlyList<FileChange>? arrived = null) =>
        new(true, after, new Caption(text, new Style(Ink.Ok, true)), title, null, arrived);

    private static AreasResult Failure(AreasModel before, string title, string message) =>
        new(false, before, new Caption("✕ failed: " + message, new Style(Ink.Bad, true)), title, message);

    private static string Row(ArtGrid grid, int y) =>
        string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    private static string Text(ArtGrid grid) =>
        string.Join('\n', Enumerable.Range(0, grid.Height).Select(y => Row(grid, y)));

    // The first cell in column x that shows the letter: where a token's letter is drawn.
    private static Cell? FindLetter(ArtGrid grid, int x, char letter)
    {
        for (var y = 0; y < grid.Height; y++)
            if (grid[x, y].Ch == letter)
                return grid[x, y];
        return null;
    }

    [Fact]
    public void A_discard_at_800_draws_the_token_in_dim()
    {
        var before = Model(1, unstaged: [Unstaged("app.txt")]);
        var after = Model(2);
        var anim = new StashAnimation(StashOp.Discard, before, [Unstaged("app.txt")], "Discarding app.txt…");
        anim.SetResult(Success(after, "Discarded", "✔ Discarded app.txt"), 400);

        var frame = anim.Render(Geo, 800);

        Assert.Equal(new Style(Ink.Dim), FindLetter(frame.Grid, 2, 'M')?.Style);
        Assert.Equal(' ', frame.Grid[2, 1].Ch);
    }

    [Fact]
    public void A_discard_holds_with_the_letter_at_its_row_until_git_answers()
    {
        var anim = new StashAnimation(StashOp.Discard, Model(0, unstaged: [Unstaged("app.txt")]), [Unstaged("app.txt")], "Discarding app.txt…");

        var frame = anim.Render(Geo, 1_000);

        Assert.Equal(400, anim.HoldMs);
        Assert.Equal(1050, anim.TotalMs);
        Assert.False(anim.IsDone(10_000));
        Assert.Equal('M', frame.Grid[2, 1].Ch);
        Assert.Equal("Discarding", anim.Title(10_000));
        Assert.Contains("Discarding app.txt…", frame.Caption?.Text);
    }

    [Fact]
    public void A_discard_shows_the_new_count_once_the_token_has_faded()
    {
        var anim = new StashAnimation(StashOp.Discard, Model(1, unstaged: [Unstaged("app.txt")]), [Unstaged("app.txt")], "Discarding app.txt…");
        anim.SetResult(Success(Model(2), "Discarded", "✔ Discarded app.txt: saved as stash"), 400);

        var during = anim.Render(Geo, 900);
        var done = anim.Render(Geo, 1050);

        Assert.Equal(" Stash (1) ", Row(during.Grid, 7).Substring(2, 11));
        Assert.Equal(" Stash (2) ", Row(done.Grid, 7).Substring(2, 11));
        Assert.Equal("Discarded", anim.Title(1050));
        Assert.Equal("✔ Discarded app.txt: saved as stash", done.Caption?.Text);
    }

    [Fact]
    public void A_failed_discard_jumps_to_the_failure_overlay_with_the_file_back_in_its_row()
    {
        var before = Model(1, unstaged: [Unstaged("app.txt")]);
        var anim = new StashAnimation(StashOp.Discard, before, [Unstaged("app.txt")], "Discarding app.txt…");

        anim.SetResult(Failure(before, "Failed", "index.lock exists"), 100);

        Assert.True(anim.IsDone(100));
        var frame = anim.Final(Geo);
        Assert.Contains("index.lock exists", Text(frame.Grid));
        Assert.Equal('M', frame.Grid[2, 1].Ch);
        Assert.Equal("Failed", anim.Title(100));
    }

    [Fact]
    public void A_stash_keeps_the_old_count_until_it_is_done_and_then_shows_the_new_one()
    {
        var before = Model(1, unstaged: [Unstaged("a.txt"), Unstaged("b.txt")]);
        var anim = new StashAnimation(StashOp.Stash, before, [Unstaged("a.txt"), Unstaged("b.txt")], "Stashing…");
        anim.SetResult(Success(Model(2), "Stashed", "✔ Stashed 2 files: the working tree is clean"), 0);

        var midway = anim.Render(Geo, 790); // the last token has landed; git has not answered
        var final = anim.Final(Geo);

        Assert.Equal(700 + 80, anim.HoldMs);
        Assert.Equal(780 + 100, anim.TotalMs);
        Assert.Equal(" Stash (1) ", Row(midway.Grid, 7).Substring(2, 11));
        Assert.Equal(" Stash (2) ", Row(final.Grid, 7).Substring(2, 11));
    }

    [Fact]
    public void A_stash_flies_each_row_to_the_stash_box_and_the_rows_leave_the_areas()
    {
        var before = Model(0, unstaged: [Unstaged("a.txt")], staged: [Staged("s.txt")]);
        var anim = new StashAnimation(StashOp.Stash, before, [Staged("s.txt"), Unstaged("a.txt")], "Stashing…");

        var frame = anim.Render(Geo, 300);

        Assert.Equal(700 + 80, anim.HoldMs);
        Assert.Equal(' ', frame.Grid[2, 1].Ch);
        Assert.Equal(' ', frame.Grid[AreasLayout.For(69, 11).IxPos(0).X, 1].Ch);
        Assert.NotNull(FindLetter(frame.Grid, 2, 'M'));
    }

    [Fact]
    public void A_pop_with_no_result_holds_at_300_with_the_stash_title_blinking()
    {
        var anim = new StashAnimation(StashOp.Pop, Model(1), [], "Bringing the stash back…");

        var frame = anim.Render(Geo, 1_000);

        Assert.Equal(300, anim.HoldMs);
        Assert.False(anim.IsDone(10_000));
        Assert.Equal("Popping", anim.Title(10_000));
        Assert.Contains("Bringing the stash back…", frame.Caption?.Text);
        Assert.Equal('┌', frame.Grid[0, 7].Ch);
        Assert.Equal(new Style(Ink.Fg, true), frame.Grid[2, 7].Style); // 1 000 ms is the odd blink phase: the title is FgB
    }

    [Fact]
    public void A_pop_conflict_jumps_to_the_conflict_overlay_with_the_caption_above()
    {
        var before = Model(1, unstaged: [Unstaged("other.txt")]);
        var anim = new StashAnimation(StashOp.Pop, before, [], "Bringing the stash back…");
        var caption = "✕ CONFLICT in app.txt: pop undone, the stash entry is kept";
        anim.SetResult(new AreasResult(false, before, new Caption(caption, new Style(Ink.Bad, true)), "Conflict",
            "app.txt changed both in the stash and on main; the pop was undone and the stash entry is kept"), 350);

        Assert.True(anim.IsDone(350));
        var frame = anim.Final(Geo);
        Assert.Contains("Conflict", Text(frame.Grid));
        Assert.Contains("app.txt changed both", Text(frame.Grid));
        Assert.Equal(caption, frame.Caption?.Text);
        Assert.Equal("Conflict", anim.Title(350));
    }

    [Fact]
    public void A_pop_flies_a_brought_back_row_into_its_place_and_shows_it_when_it_lands()
    {
        var before = Model(1);
        var arrived = new[] { Unstaged("app.txt") };
        var after = new AreasModel(arrived, [], Head(), new StashSummary(0, null, null));
        var anim = new StashAnimation(StashOp.Pop, before, [], "Bringing the stash back…");
        anim.SetResult(Success(after, "Popped", "✔ Restored 1 file from the stash", arrived), 300);

        var inFlight = anim.Render(Geo, 600);
        var landed = anim.Render(Geo, 1000);

        Assert.Equal(300 + 700 + 100, anim.TotalMs);
        Assert.Equal(' ', inFlight.Grid[2, 1].Ch);
        Assert.NotNull(FindLetter(inFlight.Grid, 2, 'M'));
        Assert.Equal('M', landed.Grid[2, 1].Ch);
        Assert.Equal("Popped", anim.Title(1100));
    }

    [Fact]
    public void An_undo_shows_the_spinner_over_the_old_areas_and_then_the_new_ones()
    {
        var before = Model(0, unstaged: [Unstaged("app.txt")]);
        var anim = new StashAnimation(StashOp.Undo, before, [], "Undoing the discard of app.txt…");

        var running = anim.Render(Geo, 200);
        anim.SetResult(Success(Model(1), "Undone", "↶ Brought back your changes to app.txt"), 500);

        Assert.Equal("Undoing", anim.Title(200));
        Assert.Contains("Undoing the discard of app.txt…", running.Caption?.Text);
        Assert.False(anim.IsDone(799));
        Assert.True(anim.IsDone(800));
        Assert.Equal("Undone", anim.Title(800));
        Assert.Equal(" Stash (1) ", Row(anim.Final(Geo).Grid, 7).Substring(2, 11));
        Assert.Equal("↶ Brought back your changes to app.txt", anim.Final(Geo).Caption?.Text);
    }
}
