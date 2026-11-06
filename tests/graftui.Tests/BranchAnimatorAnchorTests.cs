using graftui.Git;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;

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
    public void The_log_shows_the_output_of_an_abort_and_of_a_failure_but_not_of_a_plain_command()
    {
        Assert.True(BranchAnimator.ShowOutput(Result("git rebase --abort", 0, "rebase", "--abort")));
        Assert.True(BranchAnimator.ShowOutput(Result("git fetch", 1, "fetch")));
        Assert.False(BranchAnimator.ShowOutput(Result("git status", 0, "status")));
    }

    [Fact]
    public void A_finished_change_keeps_its_own_panel_or_shows_the_idle_list_of_its_snapshot_or_none()
    {
        var caption = new Caption("done", new Style(Ink.Ok));
        var layout = Layouts.Diverged();
        var own = CommitLists.Idle(layout);

        Assert.Same(own, BranchAnimator.FinishPanel(new BranchResult(true, "Done", caption, layout, Panel: own)));
        Assert.Equal(CommitLists.Idle(layout).Left.Rows.Select(r => r.Subject),
            BranchAnimator.FinishPanel(new BranchResult(true, "Done", caption, layout))!.Left.Rows.Select(r => r.Subject));
        Assert.Null(BranchAnimator.FinishPanel(new BranchResult(false, "Failed", caption, null)));
    }

    [Fact]
    public void A_resumed_conflict_logs_from_the_staging_command_not_from_the_continue()
    {
        var add = Result("git add -- app.txt", 0, "add", "--", "app.txt");
        var continueRebase = Result("git rebase --continue", 0, "rebase", "--continue");

        var commands = new[] { add, continueRebase };

        // Both are anchors; the log jumps to the first one that matches, which is the staging.
        Assert.Same(add, commands.First(BranchAnimator.AnchorFor(commands)));
    }

    [Fact]
    public void A_log_anchored_on_the_continue_scrolls_the_staging_line_above_the_top()
    {
        // The staging (entry 0), then the continue (entry 1) and three more lines of its step, in a pane of three rows. The anchor is the
        // top line unless the newest lines would leave the pane half empty, so the staging is above the top only when anchored on the continue.
        int[] entryOfLine = [0, 1, 1, 1, 1];
        int[] heights = [1, 1, 1, 1, 1];

        var onStaging = LogView.TopLine(heights, rows: 3, anchor: LogView.AnchorLine(entryOfLine, 0));
        var onContinue = LogView.TopLine(heights, rows: 3, anchor: LogView.AnchorLine(entryOfLine, 1));

        Assert.Equal(0, onStaging);
        Assert.Equal(1, onContinue);
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
