using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class ChangesAnimationTests
{
    private static readonly ArtGeometry Geo = ArtGeometry.For(69, 11)!;

    private static FileChange Unstaged(string path) => new(path, null, ChangeSide.Unstaged, ChangeKind.Modified);

    private static HeadLine Head(params string[] hashes) =>
        new("main", "a111111", hashes.Select(h => Layouts.C(h, 1, "subject " + h[..3])).ToList(), false);

    private static AreasModel Before(HeadLine head) => new([], [], head);

    private static AreasResult Success(AreasModel after, string title, string text) =>
        new(true, after, new Caption(text, new Style(Ink.Ok, true)), title);

    private static AreasResult Failure(AreasModel before, string message) =>
        new(false, before, new Caption("✕ failed: " + message, new Style(Ink.Bad, true)), "Failed", message);

    private static string Row(ArtGrid grid, int y) =>
        string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    [Fact]
    public void A_stage_holds_at_its_hold_point_with_the_token_at_the_staging_area_before_git_answers()
    {
        var before = Before(Head("a111111111", "b222222222"));
        var anim = new ChangesAnimation(AreasOp.Stage, before, [Unstaged("app.txt")], "Staging app.txt…");

        var frame = anim.Render(Geo, 10_000);

        Assert.Equal(600, anim.HoldMs);
        Assert.Equal(650, anim.TotalMs);
        Assert.False(anim.IsDone(10_000));
        Assert.Equal('M', frame.Grid[26, 1].Ch);
        Assert.Equal("Staging", anim.Title(10_000));
    }

    [Fact]
    public void A_success_result_finishes_the_scene_and_shows_its_caption_and_title()
    {
        var before = Before(Head("a111111111"));
        var anim = new ChangesAnimation(AreasOp.Stage, before, [Unstaged("app.txt")], "Staging app.txt…");
        var caption = "✔ Staged app.txt: it goes into the next commit";
        anim.SetResult(Success(before, "Staged", caption), 700);

        Assert.False(anim.IsDone(749));
        Assert.True(anim.IsDone(750));
        Assert.Equal("Staged", anim.Title(750));
        Assert.Equal(caption, anim.Render(Geo, 750).Caption?.Text);
    }

    [Fact]
    public void A_failure_result_is_done_at_once_with_a_failed_overlay()
    {
        var before = Before(Head("a111111111"));
        var anim = new ChangesAnimation(AreasOp.Stage, before, [Unstaged("app.txt")], "Staging app.txt…");

        anim.SetResult(Failure(before, "index.lock exists"), 300);

        Assert.True(anim.IsDone(300));
        Assert.Equal("Failed", anim.Title(300));
        var frame = anim.Render(Geo, 300);
        Assert.Contains("Failed", string.Concat(Enumerable.Range(0, frame.Grid.Height).Select(y => Row(frame.Grid, y))));
        Assert.Contains("index.lock exists", string.Concat(Enumerable.Range(0, frame.Grid.Height).Select(y => Row(frame.Grid, y))));
        Assert.Equal("✕ failed: index.lock exists", frame.Caption?.Text);
    }

    [Fact]
    public void A_commit_waiting_for_git_blinks_the_dot_at_the_fold()
    {
        var before = Before(Head("a111111111", "b222222222"));
        var anim = new ChangesAnimation(AreasOp.Commit, before, [Unstaged("app.txt")], "Committing…");
        var fold = AreasLayout.For(69, 11).FoldPos;

        var frame = anim.Render(Geo, 5_000);

        Assert.Equal('●', frame.Grid[fold.X, fold.Y].Ch);
        Assert.False(anim.IsDone(5_000));
    }

    [Fact]
    public void A_waiting_change_shows_the_wait_warning_after_five_seconds()
    {
        var anim = new ChangesAnimation(AreasOp.Stage, Before(Head("a111111111")), [Unstaged("a.txt")], "Staging a.txt…");

        var caption = anim.Render(Geo, MergeAnimation.WaitAfterMs).Caption;

        Assert.Equal(MergeScenes.WaitingText, caption?.Text);
        Assert.Equal(new Style(Ink.Warn), caption?.Style);
    }

    [Fact]
    public void An_amend_that_landed_shows_a_ghost_where_the_old_tip_was()
    {
        var before = Before(Head("a111111111", "b222222222", "c333333333", "d444444444"));
        var after = Before(Head("a111111111", "b222222222", "c333333333", "e555555555"));
        var anim = new ChangesAnimation(AreasOp.Amend, before, [], "Amending…");
        // git answers when the scene is held at its hold point; the rest of the animation then plays out.
        anim.SetResult(Success(after, "Amended", "✔ Amended: e555555 replaces d444444"), anim.HoldMs);

        var frame = anim.Render(Geo, anim.TotalMs + 1);

        var slot = AreasLayout.For(69, 11).SlotX(3);
        Assert.Equal('◌', frame.Grid[slot, 10].Ch);
        Assert.Equal("Amended", anim.Title(anim.TotalMs + 1));
    }

    [Fact]
    public void The_commit_timings_follow_the_mock()
    {
        var withFiles = new ChangesAnimation(AreasOp.Commit, Before(Head("a111111111")), [Unstaged("a.txt")], "Committing…");
        var noFiles = new ChangesAnimation(AreasOp.Amend, Before(Head("a111111111")), [], "Amending…");

        Assert.Equal(800, withFiles.HoldMs);
        Assert.Equal(550 + 250 + 650 + 250, withFiles.TotalMs);
        Assert.Equal(250, noFiles.HoldMs);
        Assert.Equal(250 + 650 + 250, noFiles.TotalMs);
    }
}
