using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class CommitPanelsTests
{
    private const int Width = 62;      // the art pane's commit area: side by side, the left column is 29 wide
    private const int Divider = 30;    // the │ sits at column 30

    // feature/picks is 3 ahead of main (its own commits are p1..p3, oldest first) and 1 behind (main's m1).
    private static GraphLayout Picks() =>
        new("main", false, "feature/picks", Layouts.Cur, Layouts.Sel, Layouts.Shared,
            [Layouts.C("m1111111", subject: "main work")], false,
            [Layouts.C("p1111111", subject: "pick one"), Layouts.C("p2222222", subject: "pick two"), Layouts.C("p3333333", subject: "pick three")], false,
            [Layouts.C(Layouts.Shared)], false,
            Layouts.C(Layouts.Cur), Layouts.C(Layouts.Sel), 3, 1, GraphKind.Diverged);

    private static IReadOnlyList<string> Lines(CommitPanel p, int width, int rows, bool sideBySide) =>
        CommitPanels.Render(p, width, rows, sideBySide).Select(l => string.Concat(l.Select(s => s.Text))).ToList();

    [Fact]
    public void Idle_lists_the_selected_branch_on_the_left_and_main_on_the_right()
    {
        var panel = CommitLists.Idle(Picks());

        Assert.Equal("feature/picks  3 new", string.Concat(panel.Left.Header.Select(s => s.Text)));
        Assert.Equal(["pick three", "pick two", "pick one"], panel.Left.Rows.Select(r => r.Subject));
        Assert.Equal(3, panel.Left.Total);
        Assert.NotNull(panel.Right);
        Assert.Equal("main (current)  1 new", string.Concat(panel.Right.Header.Select(s => s.Text)));
        Assert.Equal(["main work"], panel.Right.Rows.Select(r => r.Subject));
        Assert.Equal(1, panel.Right.Total);
        Assert.Empty(panel.Footer);
    }

    [Fact]
    public void Main_shows_its_tip_when_the_branch_has_nothing_main_lacks()
    {
        var panel = CommitLists.Idle(Layouts.FastForward());

        Assert.Equal("main (current)  tip", string.Concat(panel.Right!.Header.Select(s => s.Text)));
        Assert.Equal(["c111111"], panel.Right.Rows.Select(r => r.Hash));
    }

    [Fact]
    public void After_a_merge_commit_main_lists_the_merge_then_the_merged_commits()
    {
        var outcome = new MergeOutcome(MergeKind.MergeCommit, "feature/picks", "main", "e333333333", [], "", []);
        var merge = Layouts.C("e333333333", parents: 2, subject: "Merge feature/picks");

        var panel = CommitLists.After(Picks(), outcome, merge);

        Assert.Null(panel.Right);
        Assert.Equal("main (current)  4 new", string.Concat(panel.Left.Header.Select(s => s.Text)));
        Assert.Equal(["Merge feature/picks", "pick three", "pick two", "pick one"], panel.Left.Rows.Select(r => r.Subject));
        Assert.Equal('◉', panel.Left.Rows[0].Dot);
    }

    [Fact]
    public void After_a_fast_forward_main_lists_the_branch_commits_it_gained()
    {
        var outcome = new MergeOutcome(MergeKind.FastForward, "feature", "main", "d222222222", [], "", []);

        var panel = CommitLists.After(Layouts.FastForward(), outcome, null);

        Assert.Null(panel.Right);
        Assert.Equal("main (current)  1 new", string.Concat(panel.Left.Header.Select(s => s.Text)));
        Assert.Equal(["d222222"], panel.Left.Rows.Select(r => r.Hash));
    }

    [Fact]
    public void Side_by_side_at_62_columns_has_one_header_row_then_both_groups()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 4, sideBySide: true);

        Assert.Equal(4, lines.Count);
        Assert.Equal("feature/picks  3 new".PadRight(29) + " │ " + "main (current)  1 new".PadRight(30), lines[0]);
        Assert.Contains("pick three", lines[1]);
        Assert.Contains("main work", lines[1]);
        Assert.Contains("pick two", lines[2]);
        Assert.Contains("pick one", lines[3]);
        Assert.All(lines, l => Assert.Equal(Divider, l.IndexOf('│')));
        Assert.All(lines, l => Assert.Equal(Width, TextFit.Columns(l)));
    }

    [Fact]
    public void Side_by_side_with_3_rows_shows_the_newest_row_and_a_count_of_the_rest()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 3, sideBySide: true);

        Assert.Equal(3, lines.Count);
        Assert.Contains("pick three", lines[1]);
        Assert.Contains("+2 more", lines[2]);
        Assert.DoesNotContain("pick two", lines[2]);
    }

    [Fact]
    public void Side_by_side_with_2_rows_shows_only_the_newest_row_of_each_group()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 2, sideBySide: true);

        Assert.Equal(2, lines.Count);
        Assert.Contains("pick three", lines[1]);
        Assert.Contains("main work", lines[1]);
        Assert.DoesNotContain("more", lines[1]);
    }

    [Fact]
    public void Side_by_side_with_1_row_shows_the_newest_row_of_each_group_and_no_header()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 1, sideBySide: true);

        var line = Assert.Single(lines);
        Assert.Contains("pick three", line);
        Assert.Contains("main work", line);
        Assert.DoesNotContain("feature/picks", line);
        Assert.Equal(Divider, line.IndexOf('│'));
    }

    [Fact]
    public void Stacked_with_4_rows_gives_each_group_a_header_and_one_row()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 4, sideBySide: false);

        Assert.Equal(4, lines.Count);
        Assert.StartsWith("feature/picks  3 new", lines[0]);
        Assert.Contains("pick three", lines[1]);
        Assert.StartsWith("main (current)  1 new", lines[2]);
        Assert.Contains("main work", lines[3]);
        Assert.All(lines, l => Assert.Equal(Width, TextFit.Columns(l)));
        Assert.All(lines, l => Assert.DoesNotContain("│", l));
    }

    [Fact]
    public void Stacked_with_3_rows_drops_the_headers_and_shows_the_first_row_of_each_group()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 3, sideBySide: false);

        Assert.Equal(2, lines.Count);
        Assert.Contains("pick three", lines[0]);
        Assert.Contains("main work", lines[1]);
    }

    [Fact]
    public void Stacked_with_1_row_shows_the_selected_branches_newest_commit()
    {
        var lines = Lines(CommitLists.Idle(Picks()), Width, 1, sideBySide: false);

        Assert.Equal(["● p333333 pick three" + new string(' ', Width - 20)], lines);
    }

    [Fact]
    public void A_conflict_lists_its_files_as_a_footer_under_both_groups()
    {
        var outcome = new MergeOutcome(MergeKind.Conflict, "feature", "main", null, ["app.txt"], "merge aborted", []);
        var panel = CommitLists.After(Layouts.Diverged(), outcome, null);

        var lines = Lines(panel, Width, 4, sideBySide: true);

        // header, one row per side, then the file row across the full width
        Assert.Equal(3, lines.Count);
        Assert.Contains("│", lines[0]);
        Assert.Contains("app.txt", lines[2]);
        Assert.Contains("conflict (merge aborted)", lines[2]);
        Assert.DoesNotContain("│", lines[2]);
    }

    [Fact]
    public void A_single_group_has_a_header_and_its_tip_and_no_divider()
    {
        var lines = Lines(CommitLists.Idle(Layouts.UpToDate()), Width, 4, sideBySide: true);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("main (current)  tip", lines[0]);
        Assert.Contains("c111111", lines[1]);
        Assert.DoesNotContain("│", lines[1]);
    }

    [Fact]
    public void A_long_subject_is_cut_so_the_divider_stays_in_its_column()
    {
        var longSubject = new string('x', 200);
        var layout = Picks() with { SelectedOwn = [Layouts.C("p1111111", subject: longSubject), Layouts.C("p2222222", subject: longSubject), Layouts.C("p3333333", subject: "short")] };

        var lines = Lines(CommitLists.Idle(layout), Width, 4, sideBySide: true);

        Assert.All(lines, l => Assert.Equal(Divider, l.IndexOf('│')));
        Assert.All(lines, l => Assert.Equal(Width, TextFit.Columns(l)));
        Assert.Contains("…", lines[2]);
    }
}
