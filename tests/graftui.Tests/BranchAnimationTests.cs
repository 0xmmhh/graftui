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

    [Fact]
    public void Switch_at_the_start_has_the_current_token_on_the_current_name_row()
    {
        var anim = new BranchAnimation(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");

        var frame = anim.Render(Geo, 0);

        Assert.Equal(Geo.A - 2, RowOf(frame.Grid, '◆'));
    }

    [Fact]
    public void Switch_ends_with_the_token_gone_and_current_under_the_new_name()
    {
        var anim = new BranchAnimation(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");
        anim.SetResult(Success(Alone("feature", Layouts.Sel), "Switched", "✔ Switched to feature"), 100);

        var frame = anim.Render(Geo, 2_000);

        Assert.True(anim.IsDone(2_000));
        Assert.Equal(-1, RowOf(frame.Grid, '◆'));
        Assert.Equal("feature (current)", Row(frame.Grid, Geo.A - 2).Trim());
        Assert.Equal("✔ Switched to feature", frame.Caption?.Text);
        Assert.Equal("Switched", anim.Title(2_000));
    }

    [Fact]
    public void Switch_is_not_done_before_git_answers()
    {
        var anim = new BranchAnimation(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");

        Assert.False(anim.IsDone(50_000));
        Assert.Equal("Switching", anim.Title(50_000));
        Assert.Equal("⠋ Switching to feature…", anim.Render(Geo, 0).Caption?.Text);
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
        var anim = new BranchAnimation(BranchOp.Switch, Layouts.Diverged(), "feature", null, 0, "Switching to feature…");

        Assert.Equal(MergeScenes.WaitingText, anim.Render(Geo, 5_000).Caption?.Text);
    }
}
