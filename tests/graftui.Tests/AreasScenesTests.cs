using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

public class AreasScenesTests
{
    private static readonly ArtGeometry Wide = ArtGeometry.For(69, 11)!;
    private static readonly ArtGeometry Narrow = ArtGeometry.For(30, 8) ?? throw new InvalidOperationException("30x8 is not a supported size");

    private static FileChange Unstaged(string path, ChangeKind kind = ChangeKind.Modified) =>
        new(path, null, ChangeSide.Unstaged, kind);

    private static FileChange Staged(string path, ChangeKind kind = ChangeKind.Modified) =>
        new(path, null, ChangeSide.Staged, kind);

    private static HeadLine Head(int commits, string branch = "main") =>
        new(branch, "a111111", Enumerable.Range(0, commits).Select(i => Layouts.C($"{i + 1}{i + 1}{i + 1}1111111", 1, $"Commit {i}")).ToList(), false);

    private static AreasModel Model(IReadOnlyList<FileChange>? unstaged = null, IReadOnlyList<FileChange>? staged = null, HeadLine? head = null) =>
        new(unstaged ?? [], staged ?? [], head ?? Head(3));

    private static string Row(ArtGrid grid, int y) =>
        string.Concat(Enumerable.Range(0, grid.Width).Select(x => grid[x, y].Ch));

    private static string Text(ArtGrid grid) =>
        string.Join('\n', Enumerable.Range(0, grid.Height).Select(y => Row(grid, y)));

    [Fact]
    public void The_layout_at_69_by_11_matches_the_design()
    {
        var l = AreasLayout.For(69, 11);

        Assert.Equal(21, l.BoxW);
        Assert.Equal(4, l.ListRows);
        Assert.Equal(5, l.BoxBottom);
        Assert.Equal(9, l.LineY);
        Assert.Equal(30, l.LineX);
        Assert.Equal(6, l.Step);
        Assert.Equal(8, l.SubjectRow);
        Assert.Equal(9, l.BodyRow);
        Assert.Equal(7, l.FormLabelRow);
        Assert.Equal(30 + 6 * 2, l.SlotX(2));
    }

    [Fact]
    public void The_layout_at_30_by_8_is_the_short_one()
    {
        var l = AreasLayout.For(30, 8);

        Assert.Equal(3, l.ListRows);
        Assert.Equal(4, l.BoxBottom);
        Assert.Equal(7, l.LineY);
        Assert.Equal(-1, l.FormLabelRow);
    }

    [Fact]
    public void Working_tree_title_sits_in_row_zero_from_column_two()
    {
        var grid = AreasScenes.Static(Wide, Model());

        Assert.Equal(3, Row(grid, 0).IndexOf("Working tree", StringComparison.Ordinal));
        Assert.Equal('┌', grid[0, 0].Ch);
    }

    [Fact]
    public void The_arrow_sits_in_the_gap_after_the_first_box()
    {
        var grid = AreasScenes.Static(Wide, Model());

        Assert.Equal('─', grid[21, 2].Ch);
        Assert.Equal('▶', grid[22, 2].Ch);
    }

    [Fact]
    public void The_first_unstaged_letter_is_at_two_one()
    {
        var grid = AreasScenes.Static(Wide, Model(unstaged: [Unstaged("app.txt")]));

        Assert.Equal('M', grid[2, 1].Ch);
        Assert.Equal('a', grid[4, 1].Ch);
    }

    [Fact]
    public void The_tip_dot_sits_on_the_last_slot_and_the_marker_is_above_it()
    {
        var grid = AreasScenes.Static(Wide, Model(head: Head(3)));
        var l = AreasLayout.For(69, 11);

        Assert.Equal('●', grid[l.SlotX(2), 9].Ch);
        Assert.Equal('▾', grid[l.SlotX(2), 8].Ch);
    }

    [Fact]
    public void Six_files_show_three_more_on_the_last_row()
    {
        var files = Enumerable.Range(0, 6).Select(i => Unstaged($"f{i}.txt")).ToList();

        var grid = AreasScenes.Static(Wide, Model(unstaged: files));

        Assert.StartsWith("+3 more", Row(grid, 4)[2..]);
    }

    [Fact]
    public void The_repository_box_keeps_its_border_at_the_narrow_size()
    {
        var grid = AreasScenes.Static(Narrow, Model(head: Head(3)));

        var l = AreasLayout.For(30, 8);
        var right = l.BoxX(2) + l.BoxWidth(2) - 1;
        Assert.Equal('│', grid[right, 2].Ch);
        Assert.Equal('│', grid[right, 3].Ch);
    }

    [Fact]
    public void At_thirty_by_eight_the_layout_draws_without_error_and_the_box_bottom_is_row_four()
    {
        var grid = AreasScenes.Static(Narrow, Model(head: Head(3)));

        Assert.Equal('└', grid[0, 4].Ch);
        var l = AreasLayout.For(30, 8);
        Assert.Equal('●', grid[l.SlotX(0), 7].Ch);
    }

    [Fact]
    public void A_clean_diff_says_nothing_to_commit()
    {
        var frame = AreasScenes.Diff(Wide, null);

        Assert.Contains("✔ Nothing to commit, working tree clean", Text(frame.Grid));
        Assert.Contains("Tab goes back to Branches.", Text(frame.Grid));
    }

    [Fact]
    public void A_diff_draws_each_line_by_kind_from_column_one()
    {
        var diff = new FileDiff(Unstaged("app.txt"),
            [new DiffLine(DiffLineKind.Hunk, "@@ -1 +1 @@"), new DiffLine(DiffLineKind.Removed, "-old"), new DiffLine(DiffLineKind.Added, "+new")]);

        var grid = AreasScenes.Diff(Wide, diff).Grid;

        Assert.Equal('@', grid[1, 0].Ch);
        Assert.Equal(new Style(Ink.Cur), grid[1, 0].Style);
        Assert.Equal(new Style(Ink.Bad), grid[1, 1].Style);
        Assert.Equal(new Style(Ink.Ok), grid[1, 2].Style);
    }

    [Fact]
    public void The_form_shows_its_label_the_subject_and_the_counter()
    {
        var frame = AreasScenes.Form(Wide, Model(), amend: false, subjectCols: 0, longestBodyCols: 0);

        Assert.StartsWith("subject", Row(frame.Grid, 8));
        Assert.Equal("0/50", Row(frame.Grid, 8).Substring(63, 4));
        Assert.Contains("Commit message", Row(frame.Grid, 7));
    }

    [Fact]
    public void The_amend_form_has_its_own_label()
    {
        var frame = AreasScenes.Form(Wide, Model(), amend: true, subjectCols: 21, longestBodyCols: 80);

        Assert.Contains("Amend the last commit: new message", Row(frame.Grid, 7));
        Assert.Equal("21/50", Row(frame.Grid, 8).Substring(63, 5));
        Assert.Equal("80/72", Row(frame.Grid, 9).Substring(63, 5));
    }

    [Fact]
    public void Fly_token_blanks_seventeen_cells_then_draws_the_letter_and_a_cut_path()
    {
        var grid = new ArtGrid(40, 2);
        AreasScenes.FlyToken(grid, (0, 0), (0, 0), 0, 'M', "a-very-long-file-name.txt", new Style(Ink.Warn));

        Assert.Equal('M', grid[0, 0].Ch);
        Assert.Equal(new Style(Ink.Warn, true), grid[0, 0].Style);
        Assert.Equal('a', grid[2, 0].Ch);
        Assert.Equal('…', grid[16, 0].Ch);
        Assert.Equal(' ', grid[17, 0].Ch);
    }

    [Fact]
    public void Letter_colours_follow_the_status()
    {
        Assert.Equal(new Style(Ink.Ok, true), AreasScenes.LetterStyle(Staged("a", ChangeKind.Added)));
        Assert.Equal(new Style(Ink.Warn, true), AreasScenes.LetterStyle(Unstaged("a")));
        Assert.Equal(new Style(Ink.Inc, true), AreasScenes.LetterStyle(Unstaged("a", ChangeKind.Untracked)));
        Assert.Equal(new Style(Ink.Bad, true), AreasScenes.LetterStyle(Unstaged("a", ChangeKind.Deleted)));
        Assert.Equal(new Style(Ink.Ok, true), AreasScenes.LetterStyle(Unstaged("a", ChangeKind.Added)));
        Assert.Equal(new Style(Ink.Bad, true), AreasScenes.LetterStyle(Unstaged("a", ChangeKind.Conflicted)));
    }
}
