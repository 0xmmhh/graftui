using graftui.Model;
using graftui.Ui.App;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The text, the log and the exit code that a stash-merge-pop shows for each outcome. A step that could not be saved is said so
/// (no u), and a conflict or a failure exits 1 as a failed merge does.
/// </summary>
public class StashMergeFinishTests
{
    private static readonly Style Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true), BadB = new(Ink.Bad, true);
    private const string Rescue = "your state from before is saved as refs/graftui/rescue/stash-merge";

    private static UndoRecord Step() => new(UndoKind.StashMerge, "merge of topic over your changes", [], Branch: "main");

    private static BranchesTab.BranchFinish Finish(StashMergeKind kind, string message, IReadOnlyList<string>? files = null, UndoRecord? undo = null)
    {
        var outcome = new StashMergeOutcome(kind, null, files ?? [], message, [], undo);
        return BranchesTab.StashMergeFinish(outcome, "topic", "main", 2, null, null);
    }

    [Fact]
    public void A_merge_whose_undo_step_could_not_be_saved_shows_the_message_as_a_warning_and_exits_0()
    {
        var message = "merged topic into main; your changes are back, but the undo step could not be made; " + Rescue;

        var f = Finish(StashMergeKind.Merged, message);

        Assert.Equal(new Caption(message, Warn), f.Result.Caption);
        Assert.Contains((message, WarnB), f.Log);
        Assert.Equal("graftui: " + message, f.ExitLine);
        Assert.Equal(0, f.ExitCode);
    }

    [Fact]
    public void A_merge_with_its_undo_step_keeps_the_success_caption_and_exits_0()
    {
        var f = Finish(StashMergeKind.Merged, "merged topic into main; your changes are back", undo: Step());

        Assert.Contains("your 2 changed files are back", f.Result.Caption.Text);
        Assert.Equal(0, f.ExitCode);
    }

    [Fact]
    public void A_pop_conflict_without_an_undo_step_offers_no_u_and_shows_the_message()
    {
        var message = "merged, but the pop did not apply and its undo step could not be saved (disk full); your changes are in stash@{0}; " + Rescue;

        var f = Finish(StashMergeKind.PopConflict, message, ["app.txt"]);

        Assert.Equal(new Caption(message, Warn), f.Result.Caption);
        Assert.DoesNotContain("u undoes", f.Result.Overlay);
        Assert.Contains("Z tries again", f.Result.Overlay);
        Assert.Contains("app.txt", f.Result.Overlay);
        Assert.All(f.Log, line => Assert.DoesNotContain("u undoes", line.Text));
        Assert.Contains((message, WarnB), f.Log);
        Assert.Equal(1, f.ExitCode);
    }

    [Fact]
    public void A_pop_conflict_with_an_undo_step_offers_u_and_exits_1()
    {
        var f = Finish(StashMergeKind.PopConflict, "merged, but the pop conflicted in app.txt: changes kept in the stash", ["app.txt"], Step());

        Assert.Equal("✕ Merged, but the pop conflicted in app.txt: changes kept in the stash", f.Result.Caption.Text);
        Assert.Contains("u undoes the merge", f.Result.Overlay);
        Assert.Equal(BadB, f.Result.Caption.Style);
        Assert.Equal(1, f.ExitCode);
    }

    [Fact]
    public void A_merge_conflict_is_aborted_and_exits_1()
    {
        var f = Finish(StashMergeKind.MergeConflict, "merge aborted; your changes are back as they were", ["app.txt"]);

        Assert.StartsWith("✕ CONFLICT in app.txt", f.Result.Caption.Text);
        Assert.Equal(1, f.ExitCode);
    }

    [Fact]
    public void A_failure_exits_1()
    {
        var f = Finish(StashMergeKind.Failed, "boom");

        Assert.Equal("graftui: stash, merge, pop failed: boom", f.ExitLine);
        Assert.Equal(1, f.ExitCode);
    }
}
