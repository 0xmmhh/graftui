using graftui.Git;
using graftui.Ui.App;

namespace graftui.Tests;

/// <summary>
/// Where the Commands pane's log jumps after a branch change. After an aborted rebase it jumps to the abort, so the abort is on screen
/// below the failing command's long output; otherwise it jumps to the first anchor, as it always did.
/// </summary>
public class BranchAnimatorAnchorTests
{
    private static GitResult Result(string command, int exit, params string[] args) =>
        new(command, exit, "", "") { Args = args };

    [Fact]
    public void After_an_aborted_rebase_the_log_jumps_to_the_abort_not_to_the_failing_rebase()
    {
        var failed = Result("git rebase -i --no-autosquash --empty=drop main", 1, "rebase", "-i", "--no-autosquash", "--empty=drop", "main");
        var abort = Result("git rebase --abort", 0, "rebase", "--abort");
        var commands = new[] { failed, abort };

        var anchorOn = BranchAnimator.AnchorFor(commands);

        Assert.False(anchorOn(failed));
        Assert.True(anchorOn(abort));
    }

    [Fact]
    public void Without_an_abort_the_usual_anchors_are_used()
    {
        // The log takes the first command that matches (LogCommands); a rebase and a merge both match the usual predicate.
        var rebase = Result("git rebase -i main", 0, "rebase", "-i", "main");
        var merge = Result("git merge main", 0, "merge", "main");

        var anchorOn = BranchAnimator.AnchorFor([rebase, merge]);

        Assert.True(anchorOn(rebase));
        Assert.True(anchorOn(merge));
    }
}
