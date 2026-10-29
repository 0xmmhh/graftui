using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class StashMergeAnimationTests
{
    private static readonly ArtGeometry Geo = ArtGeometry.For(80, 11)!;

    private static readonly MergeOutcome FastForward =
        new(MergeKind.FastForward, "feature/x", "main", Layouts.Sel, [], "fast-forward", []);

    private static readonly MergeOutcome MergeCommit =
        new(MergeKind.MergeCommit, "feature/x", "main", "e333333333", [], "merge commit created", []);

    private static BranchResult Result(string title, string text, MergeOutcome? merge, GraphLayout? after = null) =>
        new(true, title, new Caption(text, new Style(Ink.Ok, true)), after, Merge: merge);

    [Fact]
    public void While_stashing_the_caption_says_so_with_the_changed_file_count()
    {
        var anim = new StashMergeAnimation(Layouts.FastForward(), 3);

        Assert.Equal("⠋ Stashing your 3 changed files…", anim.Render(Geo, 0).Caption?.Text);
        Assert.Equal("⠙ Stashing your 3 changed files…", anim.Render(Geo, 100).Caption?.Text);
        Assert.Equal("Merging", anim.Title(100));
    }

    [Fact]
    public void A_fast_forward_ends_with_the_result_caption_after_the_pop()
    {
        var anim = new StashMergeAnimation(Layouts.FastForward(), 3);
        var result = Result("Fast-forwarded", "✔ Fast-forward: main is now at e333333, your 3 changed files are back", FastForward);

        anim.SetResult(result, 600);

        // The fast-forward takes 1600 ms from the end of the stash (500 ms), then the pop takes 500 ms.
        Assert.False(anim.IsDone(2_599));
        Assert.Equal($"{MergeScenes.Spin(2_200)} Popping your changes back…", anim.Render(Geo, 2_200).Caption?.Text);
        Assert.True(anim.IsDone(2_600));
        Assert.Equal(result.Caption.Text, anim.Render(Geo, 3_000).Caption?.Text);
        Assert.Equal("Fast-forwarded", anim.Title(3_000));
    }

    [Fact]
    public void A_result_without_a_merge_jumps_to_the_final_frame()
    {
        var anim = new StashMergeAnimation(Layouts.FastForward(), 3);
        var failed = new BranchResult(false, "Failed", new Caption("✕ Stash, merge, pop failed: nothing was stashed", new Style(Ink.Bad, true)),
            null, "Failed", "nothing was stashed");

        anim.SetResult(failed, 100);
        var frame = anim.Render(Geo, 100);

        Assert.True(anim.IsDone(100));
        Assert.Equal(failed.Caption.Text, frame.Caption?.Text);
        Assert.Contains("nothing was stashed", string.Concat(Enumerable.Range(0, frame.Grid.Height)
            .Select(y => string.Concat(Enumerable.Range(0, frame.Grid.Width).Select(x => frame.Grid[x, y].Ch)))));
        Assert.Equal("Failed", anim.Title(100));
    }

    [Fact]
    public void Before_the_result_the_merge_is_not_done_and_its_own_scene_plays_after_the_stash()
    {
        var anim = new StashMergeAnimation(Layouts.Diverged(), 1);

        Assert.False(anim.IsDone(20_000));
        // Past the stash the merge scene is drawn with its own caption (the merge has no outcome yet).
        Assert.Contains("Merging", anim.Render(Geo, 1_000).Caption?.Text ?? "");
    }

    [Fact]
    public void A_merge_commit_result_plays_the_merge_then_pops()
    {
        var anim = new StashMergeAnimation(Layouts.Diverged(), 2);
        var result = Result("Merged", "✔ Merge commit e333333 created on main, your 2 changed files are back", MergeCommit);

        anim.SetResult(result, 700);

        // The merge scene ends 1650 ms after its start, which is 500 ms into the whole, plus the 500 ms pop.
        Assert.False(anim.IsDone(2_649));
        Assert.True(anim.IsDone(2_650));
        Assert.Equal(result.Caption.Text, anim.Render(Geo, 3_000).Caption?.Text);
        Assert.Equal("Merged", anim.Title(3_000));
    }
}
