using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class RebaseAnimationTests
{
    private static readonly ArtGeometry G = ArtGeometry.For(62, 9)!;

    private static readonly IReadOnlyList<RebaseRow> Rows =
    [
        new("aaaaaaa1" + new string('0', 32), "aaaaaaa1", "one", RebaseAction.Pick),
    ];

    private static readonly GraphLayout L = Layouts.Diverged() with { Current = "feature/picks", Selected = "main" };

    private static BranchResult Result(string title = "Rebased", string text = "done") =>
        new(true, title, new Caption(text, new Style(Ink.Ok, true)), L);

    [Fact]
    public void The_title_is_Rebasing_until_the_result_is_in()
    {
        var anim = new RebaseAnimation(L, Rows);

        Assert.Equal("Rebasing", anim.Title(0));
        anim.SetResult(Result(), 100);
        Assert.Equal("Rebased", anim.Title(100));
    }

    [Fact]
    public void The_running_caption_names_the_branches_with_its_spinner()
    {
        var caption = new RebaseAnimation(L, Rows).Render(G, 100).Caption!;

        Assert.Equal($"{MergeScenes.Spin(100)} Rebasing feature/picks onto main…", caption.Text);
    }

    [Fact]
    public void After_the_wait_limit_without_git_the_wait_text_is_shown()
    {
        var caption = new RebaseAnimation(L, Rows).Render(G, MergeAnimation.WaitAfterMs).Caption!;

        Assert.Equal(MergeScenes.WaitingText, caption.Text);
    }

    [Fact]
    public void The_animation_is_done_as_soon_as_the_result_is_in()
    {
        var anim = new RebaseAnimation(L, Rows);
        Assert.False(anim.IsDone(10));

        anim.SetResult(Result(), 10);

        Assert.True(anim.IsDone(10));
        Assert.Same(L, anim.Result!.After);
    }

    [Fact]
    public void The_done_frame_carries_the_result_caption()
    {
        var anim = new RebaseAnimation(L, Rows);
        anim.SetResult(Result(text: "✔ Rebased feature/picks onto main: 1 commit replayed"), 10);

        var frame = anim.Render(G, 2000);

        Assert.Equal("✔ Rebased feature/picks onto main: 1 commit replayed", frame.Caption!.Text);
    }

    [Fact]
    public void A_result_with_an_overlay_is_drawn_over_the_final_frame()
    {
        var anim = new RebaseAnimation(L, Rows);
        anim.SetResult(Result("Refused") with { Overlay = "nothing to rebase" }, 10);

        var grid = anim.Render(G, 2000).Grid;
        var text = string.Concat(Enumerable.Range(0, grid.Height).Select(y => string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch))));

        Assert.Contains("nothing to rebase", text);
    }
}
