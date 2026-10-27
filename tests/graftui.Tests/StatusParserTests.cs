using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

public class StatusParserTests
{
    private const string H = "0123456789abcdef0123456789abcdef01234567";

    // One ordinary ("1") record, NUL-terminated.
    private static string Entry(string xy, string path) => $"1 {xy} N... 100644 100644 100644 {H} {H} {path}\0";

    [Fact]
    public void Unstaged_modified_file_gives_one_unstaged_row()
    {
        var s = StatusParser.Parse(Entry(".M", "app.txt"));

        Assert.Empty(s.Staged);
        var row = Assert.Single(s.Unstaged);
        Assert.Equal(new FileChange("app.txt", null, ChangeSide.Unstaged, ChangeKind.Modified), row);
        Assert.Equal('M', row.Letter);
    }

    [Fact]
    public void Modified_in_both_areas_gives_a_staged_row_and_an_unstaged_row()
    {
        var s = StatusParser.Parse(Entry("MM", "app.txt"));

        Assert.Equal(new FileChange("app.txt", null, ChangeSide.Staged, ChangeKind.Modified), Assert.Single(s.Staged));
        Assert.Equal(new FileChange("app.txt", null, ChangeSide.Unstaged, ChangeKind.Modified), Assert.Single(s.Unstaged));
        Assert.Equal(1, s.ChangedPaths);
    }

    [Fact]
    public void Staged_and_unstaged_deletes_are_parsed_on_their_own_side()
    {
        var staged = StatusParser.Parse(Entry("D.", "gone.txt"));
        Assert.Equal(ChangeKind.Deleted, Assert.Single(staged.Staged).Kind);
        Assert.Empty(staged.Unstaged);

        var unstaged = StatusParser.Parse(Entry(".D", "gone.txt"));
        Assert.Empty(unstaged.Staged);
        Assert.Equal(ChangeKind.Deleted, Assert.Single(unstaged.Unstaged).Kind);
    }

    [Fact]
    public void Intent_to_add_is_an_unstaged_added_row()
    {
        var s = StatusParser.Parse(Entry(".A", "new.txt"));

        Assert.Equal(ChangeKind.Added, Assert.Single(s.Unstaged).Kind);
    }

    [Fact]
    public void Rename_takes_the_original_path_from_the_next_record_and_parsing_continues()
    {
        var input = $"2 R. N... 100644 100644 100644 {H} {H} R100 new name.txt\0old.txt\0{Entry(".M", "app.txt")}";

        var s = StatusParser.Parse(input);

        var renamed = Assert.Single(s.Staged);
        Assert.Equal(new FileChange("new name.txt", "old.txt", ChangeSide.Staged, ChangeKind.Renamed), renamed);
        Assert.Equal('R', renamed.Letter);
        Assert.Equal("app.txt", Assert.Single(s.Unstaged).Path);
    }

    [Fact]
    public void Untracked_names_keep_their_spaces()
    {
        var s = StatusParser.Parse("? un tracked.md\0");

        var row = Assert.Single(s.Unstaged);
        Assert.Equal("un tracked.md", row.Path);
        Assert.Equal(ChangeKind.Untracked, row.Kind);
        Assert.False(row.IsNestedRepo);
    }

    [Fact]
    public void Untracked_directory_with_a_trailing_slash_is_a_nested_repo()
    {
        var row = Assert.Single(StatusParser.Parse("? nested/\0").Unstaged);

        Assert.True(row.IsNestedRepo);
    }

    [Fact]
    public void Conflict_gives_one_unstaged_conflicted_row_and_sets_HasConflicts()
    {
        var input = $"u UU N... 100644 100644 100644 100644 {H} {H} {H} app.txt\0";

        var s = StatusParser.Parse(input);

        Assert.Empty(s.Staged);
        var row = Assert.Single(s.Unstaged);
        Assert.Equal(new FileChange("app.txt", null, ChangeSide.Unstaged, ChangeKind.Conflicted), row);
        Assert.Equal('U', row.Letter);
        Assert.True(s.HasConflicts);
    }

    [Fact]
    public void Ignored_and_header_records_are_skipped()
    {
        var s = StatusParser.Parse("# branch.oid " + H + "\0! ignored.log\0");

        Assert.Empty(s.Staged);
        Assert.Empty(s.Unstaged);
    }

    [Fact]
    public void Empty_input_is_an_empty_status()
    {
        var s = StatusParser.Parse("");

        Assert.Empty(s.Rows);
        Assert.Equal(0, s.ChangedPaths);
        Assert.False(s.HasConflicts);
    }

    [Fact]
    public void Rows_list_staged_then_unstaged_in_git_order()
    {
        var s = StatusParser.Parse(Entry("M.", "b.txt") + Entry(".M", "a.txt") + "? c.txt\0");

        Assert.Equal(["b.txt", "a.txt", "c.txt"], s.Rows.Select(r => r.Path).ToArray());
    }

    [Fact]
    public void A_submodule_record_sets_IsSubmodule_on_its_rows()
    {
        var input = $"1 .M S.M. 160000 160000 160000 {H} {H} sub\0";

        var s = StatusParser.Parse(input);

        var row = Assert.Single(s.Unstaged);
        Assert.Equal(new FileChange("sub", null, ChangeSide.Unstaged, ChangeKind.Modified, IsSubmodule: true), row);
        Assert.True(row.IsSubmodule);
    }

    [Fact]
    public void An_ordinary_record_leaves_IsSubmodule_false()
    {
        var s = StatusParser.Parse(Entry(".M", "app.txt"));

        Assert.False(Assert.Single(s.Unstaged).IsSubmodule);
    }

    [Fact]
    public void A_conflicted_submodule_sets_IsSubmodule()
    {
        var input = $"u UU S.M. 160000 160000 160000 160000 {H} {H} {H} sub\0";

        var row = Assert.Single(StatusParser.Parse(input).Unstaged);

        Assert.Equal(ChangeKind.Conflicted, row.Kind);
        Assert.True(row.IsSubmodule);
    }
}
