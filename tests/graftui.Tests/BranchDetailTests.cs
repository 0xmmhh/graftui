using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The detail block under the branch list, as text: one case per row of the table in the PR 4b plan, the file rows, the cap on files,
/// and how the list and the block share the rows. Pure, so no repo and no screen.
/// </summary>
public class BranchDetailTests
{
    // The detail width at the 120x40 and 80x24 sizes (30-column list, 27 columns for the block).
    private const int Width = 27;

    // feature/picks: three commits ahead of main, from the shared base (Layouts.Diverged with its counts changed).
    private static GraphLayout Picks(int ahead = 3) => Layouts.Diverged() with { Selected = "feature/picks", Ahead = ahead };

    private static BranchDetailInput Input(GraphLayout? layout, IncomingFiles? files = null,
        IncomingFilesState state = IncomingFilesState.NotNeeded, bool current = false, bool detached = false, string name = "feature/picks") =>
        new(name, Ink.Lane1, current, detached, layout, files, state);

    private static IncomingFiles Files(params IncomingFile[] files) => new(files, files.Length);

    private static string LeftText(DetailLine line) => string.Concat(line.Left.Select(s => s.Text));

    private static string RightText(DetailLine line) => string.Concat(line.Right.Select(s => s.Text));

    // ---------- the current branch ----------

    [Fact]
    public void The_current_branch_shows_its_three_newest_commits_under_latest_on()
    {
        var layout = Layouts.Diverged() with
        {
            Current = "main", Selected = "main", Kind = GraphKind.Same,
            // Oldest first, as the layout keeps them: the newest is the last.
            Shared =
            [
                Layouts.C("34e4a2e0000000", 1, "Initial commit"),
                Layouts.C("8c5ceee0000000", 1, "Add app"),
                Layouts.C("be0126e0000000", 1, "Change line 1 on main"),
            ],
        };

        var lines = BranchDetail.Lines(Input(layout, current: true, name: "main"), Width, 6);

        Assert.Equal(5, lines.Count);
        Assert.Equal("latest on main", LeftText(lines[0]));
        Assert.Equal("", LeftText(lines[1]));
        Assert.Equal("be0126e Change line 1 on m…", LeftText(lines[2]));
        Assert.Equal("8c5ceee Add app", LeftText(lines[3]));
        Assert.Equal("34e4a2e Initial commit", LeftText(lines[4]));
    }

    [Fact]
    public void The_current_branch_hash_is_in_the_hash_ink_and_the_subject_in_fg()
    {
        var layout = Layouts.Diverged() with { Current = "main", Selected = "main", Kind = GraphKind.Same, Shared = [Layouts.C("be0126e0000000", 1, "Add app")] };

        var line = BranchDetail.Lines(Input(layout, current: true, name: "main"), Width, 6)[2];

        Assert.Equal(new Style(Ink.Hash), line.Left[0].Style);
        Assert.Equal(new Style(Ink.Fg), line.Left[1].Style);
    }

    [Fact]
    public void The_current_branch_while_its_layout_loads_says_nothing()
    {
        var lines = BranchDetail.Lines(Input(null, current: true, name: "main"), Width, 6);

        Assert.Equal(2, lines.Count);
        Assert.Equal("latest on main", LeftText(lines[0]));
        Assert.Equal("", LeftText(lines[1]));
    }

    // ---------- the other rows of the table ----------

    [Fact]
    public void A_detached_head_shows_the_name_and_that_head_is_detached()
    {
        var lines = BranchDetail.Lines(Input(null, detached: true, name: "feature/x"), Width, 6);

        Assert.Equal("feature/x", LeftText(lines[0]));
        Assert.Equal(Ink.Lane1, lines[0].Left[0].Style.Ink);
        Assert.Equal("HEAD is detached", LeftText(lines[1]));
        Assert.Equal(Ink.Warn, lines[1].Left[0].Style.Ink);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_branch_whose_layout_is_loading_shows_its_name_and_says_nothing()
    {
        var lines = BranchDetail.Lines(Input(null), Width, 6);

        Assert.Equal("feature/picks", LeftText(lines[0]));
        Assert.Equal("", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_blank_block_has_empty_rows_and_claims_nothing()
    {
        var lines = BranchDetail.Lines(Input(null, name: ""), Width, 6);

        Assert.Equal("", LeftText(lines[0]));
        Assert.Equal("", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_branch_on_the_same_commit_as_main_says_so()
    {
        var layout = Layouts.Diverged() with { Kind = GraphKind.Same, Ahead = 0 };

        var lines = BranchDetail.Lines(Input(layout), 40, 6);

        Assert.Equal("same commit as main", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_branch_main_already_has_says_nothing_new_to_merge()
    {
        var lines = BranchDetail.Lines(Input(Layouts.UpToDate()), 40, 6);

        Assert.Equal("nothing new to merge", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void An_unrelated_branch_gives_its_commit_count_and_unrelated()
    {
        var layout = Picks(ahead: 1) with { Kind = GraphKind.Unrelated, MergeBase = null };

        var lines = BranchDetail.Lines(Input(layout), 40, 6);

        Assert.Equal("1 new commit · unrelated", LeftText(lines[1]));
    }

    [Fact]
    public void While_the_files_read_the_summary_says_reading()
    {
        var lines = BranchDetail.Lines(Input(Picks(), state: IncomingFilesState.Reading), 40, 6);

        Assert.Equal("3 new commits · reading…", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_failed_files_read_gives_the_summary_only()
    {
        var lines = BranchDetail.Lines(Input(Picks(), state: IncomingFilesState.Failed), 40, 6);

        Assert.Equal("3 new commits", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_read_with_no_files_says_no_file_changes()
    {
        var lines = BranchDetail.Lines(Input(Picks(ahead: 1), Files(), IncomingFilesState.Read), 40, 6);

        Assert.Equal("1 new commit · no file changes", LeftText(lines[1]));
        Assert.Equal(2, lines.Count);
    }

    // ---------- the files ----------

    [Fact]
    public void The_picks_read_as_a_summary_a_blank_row_and_three_files_with_counts()
    {
        var files = Files(
            new IncomingFile("README.md", null, 1, 1),
            new IncomingFile("VERSION", null, 1, 0),
            new IncomingFile("log.txt", null, 1, 0));

        var lines = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), Width, 8);

        Assert.Equal(6, lines.Count);
        Assert.Equal("3 new commits · 3 files", LeftText(lines[1]));
        Assert.Equal("", LeftText(lines[2]));
        Assert.Equal("README.md", LeftText(lines[3]));
        Assert.Equal("+1 -1", RightText(lines[3]));
        Assert.Equal("VERSION", LeftText(lines[4]));
        Assert.Equal("+1", RightText(lines[4]));
        Assert.Equal("log.txt", LeftText(lines[5]));
        Assert.Equal("+1", RightText(lines[5]));
    }

    [Fact]
    public void A_file_row_is_its_path_then_its_counts_ending_on_the_last_column()
    {
        var files = Files(new IncomingFile("README.md", null, 1, 1));

        var line = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), Width, 4)[3];

        // The path, the spaces that fill the row, then the counts: 27 columns in all.
        Assert.Equal("README.md" + new string(' ', 13) + "+1 -1", LeftText(line) + new string(' ', 13) + RightText(line));
        Assert.Equal(Ink.Ok, line.Right[0].Style.Ink);
        Assert.Equal(Ink.Bad, line.Right[2].Style.Ink);
    }

    [Fact]
    public void The_counts_are_plus_minus_or_bin_renamed_or_zero_as_the_table_says()
    {
        Assert.Equal("+1 -1", SegText(BranchDetail.Counts(new IncomingFile("a", null, 1, 1))));
        Assert.Equal("+1", SegText(BranchDetail.Counts(new IncomingFile("a", null, 1, 0))));
        Assert.Equal("-3", SegText(BranchDetail.Counts(new IncomingFile("a", null, 0, 3))));
        Assert.Equal("bin", SegText(BranchDetail.Counts(new IncomingFile("a", null, null, null))));
        Assert.Equal("renamed", SegText(BranchDetail.Counts(new IncomingFile("new", "old", 0, 0))));
        Assert.Equal("±0", SegText(BranchDetail.Counts(new IncomingFile("a", null, 0, 0))));
    }

    [Fact]
    public void A_binary_file_is_dim_and_a_rename_shows_its_new_path()
    {
        var files = Files(new IncomingFile("new.png", "old.png", null, null));

        var line = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), Width, 4)[3];

        Assert.Equal("new.png", LeftText(line));
        Assert.Equal("bin", RightText(line));
        Assert.Equal(Ink.Dim, line.Right[0].Style.Ink);
    }

    [Fact]
    public void A_long_path_keeps_its_end()
    {
        var files = Files(new IncomingFile("src/graftui/Ui/App/BranchesTab.cs", null, 120, 4));

        var line = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), Width, 4)[3];

        Assert.Equal("…App/BranchesTab.cs", LeftText(line));
        Assert.Equal("+120 -4", RightText(line));
    }

    [Fact]
    public void Control_characters_in_a_path_become_spaces()
    {
        var files = Files(new IncomingFile("odd\u0007name.txt", null, 1, 0));

        var line = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), Width, 4)[3];

        Assert.Equal("odd name.txt", LeftText(line));
    }

    [Fact]
    public void Ten_files_in_eight_rows_show_four_and_then_the_rest_as_more_files()
    {
        var files = Files(Enumerable.Range(0, 10).Select(i => new IncomingFile($"f{i}.txt", null, 1, 0)).ToArray());

        var lines = BranchDetail.Lines(Input(Picks(ahead: 10), files, IncomingFilesState.Read), Width, 8);

        Assert.Equal(8, lines.Count);
        Assert.Equal("f0.txt", LeftText(lines[3]));
        Assert.Equal("f3.txt", LeftText(lines[6]));
        Assert.Equal("+6 more files", LeftText(lines[7]));
    }

    [Fact]
    public void More_files_than_the_kept_maximum_say_how_many_are_left_out()
    {
        var kept = Enumerable.Range(0, IncomingFiles.FilesMax).Select(i => new IncomingFile($"f{i}.txt", null, 1, 0)).ToArray();
        var files = new IncomingFiles(kept, IncomingFiles.FilesMax + 50);

        var lines = BranchDetail.Lines(Input(Picks(ahead: 250), files, IncomingFilesState.Read), Width, 300);

        Assert.Equal(3 + IncomingFiles.FilesMax + 1, lines.Count);
        Assert.Equal("+50 more files", LeftText(lines[^1]));
        Assert.Equal("250 new commits · 250 files", LeftText(lines[1]));
    }

    [Fact]
    public void Summary_and_file_rows_are_fitted_to_a_narrow_block()
    {
        var files = Files(new IncomingFile("README.md", null, 1, 1));

        var lines = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), 21, 4);

        Assert.True(TextFit.Columns(LeftText(lines[1])) <= 21);
        Assert.EndsWith("…", LeftText(lines[1]));
        Assert.Equal("README.md", LeftText(lines[3]));
        Assert.Equal("+1 -1", RightText(lines[3]));
    }

    // ---------- the rows and the width ----------

    [Fact]
    public void One_row_is_the_name_only()
    {
        var lines = BranchDetail.Lines(Input(Picks()), Width, 1);

        Assert.Equal(new[] { "feature/picks" }, lines.Select(LeftText));
    }

    [Fact]
    public void Zero_rows_or_zero_width_give_no_lines_and_no_throw()
    {
        Assert.Empty(BranchDetail.Lines(Input(Picks()), Width, 0));
        Assert.Empty(BranchDetail.Lines(Input(Picks()), 0, 5));
        Assert.Empty(BranchDetail.Lines(Input(Picks()), Width, -1));
    }

    [Fact]
    public void A_very_narrow_block_never_throws()
    {
        var files = Files(new IncomingFile("a/b/c.txt", null, 12345, 67890));

        var lines = BranchDetail.Lines(Input(Picks(), files, IncomingFilesState.Read), 1, 6);

        Assert.True(lines.Count <= 6);
    }

    [Theory]
    [InlineData(34, 6, 6, 28)]
    [InlineData(18, 6, 6, 12)]
    [InlineData(34, 30, 26, 8)]
    [InlineData(18, 15, 10, 8)]
    [InlineData(12, 6, 6, 6)]
    [InlineData(8, 6, 8, 0)]
    [InlineData(13, 12, 13, 0)]
    public void The_rows_are_split_between_the_list_and_the_block(int listH, int count, int listRows, int detailRows)
    {
        Assert.Equal((listRows, detailRows), BranchDetail.Split(listH, count));
    }

    private static string SegText(IReadOnlyList<Seg> segs) => string.Concat(segs.Select(s => s.Text));
}
