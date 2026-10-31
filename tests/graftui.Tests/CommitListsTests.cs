using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class CommitListsTests
{
    [Fact]
    public void After_a_conflict_lists_each_conflicting_file_last()
    {
        var outcome = new MergeOutcome(MergeKind.Conflict, "feature", "main", null, ["app.txt"], "merge aborted", []);

        var panel = CommitLists.After(Layouts.Diverged(), outcome, null);

        var last = panel.Footer[^1];
        Assert.Equal('✕', last.Dot);
        Assert.Equal("app.txt", last.Hash);
    }

    private static string HeaderText(CommitGroup g) => string.Concat(g.Header.Select(s => s.Text));

    [Fact]
    public void Switched_keeps_the_columns_and_moves_current_to_the_new_branch()
    {
        // Before the switch: feature is on the left, main (current) on the right.
        var before = CommitLists.Idle(Layouts.Diverged());
        Assert.Equal("feature  1 new", HeaderText(before.Left));
        Assert.Equal("main (current)  1 new", HeaderText(before.Right!));

        var panel = CommitLists.Switched(Layouts.Diverged());

        Assert.Equal("feature (current)  1 new", HeaderText(panel.Left));
        Assert.Equal("main  1 new", HeaderText(panel.Right!));
        Assert.Equal(Ink.Cur, panel.Left.Header[0].Style.Ink);
        Assert.Equal(Ink.Inc, panel.Right!.Header[0].Style.Ink);
    }

    [Fact]
    public void Switched_to_a_branch_that_is_behind_shows_the_tip_on_the_right()
    {
        var panel = CommitLists.Switched(Layouts.FastForward());

        Assert.Equal("feature (current)  1 new", HeaderText(panel.Left));
        Assert.Equal("main  tip", HeaderText(panel.Right!));
    }

    [Fact]
    public void Switched_to_an_ancestor_shows_the_single_tip_of_the_new_current()
    {
        var panel = CommitLists.Switched(Layouts.UpToDate());

        Assert.Null(panel.Right);
        Assert.Equal("feature (current)  tip", HeaderText(panel.Left));
    }

    [Fact]
    public void Idle_lists_every_commit_the_layout_has_newest_first()
    {
        var newest = new[] { Layouts.C("e5000000", subject: "s5"), Layouts.C("e4000000", subject: "s4"), Layouts.C("e3000000", subject: "s3"),
            Layouts.C("e2000000", subject: "s2"), Layouts.C("e1000000", subject: "s1") };
        var layout = Layouts.Diverged() with { Ahead = 5, SelectedNew = newest };

        var panel = CommitLists.Idle(layout);

        Assert.Equal(new[] { "s5", "s4", "s3", "s2", "s1" }, panel.Left.Rows.Select(r => r.Subject));
        Assert.Equal(5, panel.Left.Total);
    }

    [Fact]
    public void After_a_merge_commit_the_merge_row_carries_the_merge_commit_date()
    {
        var when = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var outcome = new MergeOutcome(MergeKind.MergeCommit, "feature", "main", "e333333333", [], "", []);
        var merge = Layouts.C("e333333333", parents: 2, subject: "Merge feature") with { Date = when };

        var panel = CommitLists.After(Layouts.Diverged(), outcome, merge);

        Assert.Equal(when, panel.Left.Rows[0].Date);
    }
}
