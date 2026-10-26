using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

public class DiffParserTests
{
    [Fact]
    public void Headers_are_dropped_and_hunk_lines_keep_their_kind()
    {
        const string diff = """
            diff --git a/app.txt b/app.txt
            index 1111111..2222222 100644
            --- a/app.txt
            +++ b/app.txt
            @@ -1 +1,2 @@
            -line 1
            +line 1 (edited)
            +extra
             context
            """;

        var lines = DiffParser.Parse(diff);

        Assert.Equal(
            [
                new DiffLine(DiffLineKind.Hunk, "@@ -1 +1,2 @@"),
                new DiffLine(DiffLineKind.Removed, "-line 1"),
                new DiffLine(DiffLineKind.Added, "+line 1 (edited)"),
                new DiffLine(DiffLineKind.Added, "+extra"),
                new DiffLine(DiffLineKind.Context, " context"),
            ],
            lines);
    }

    [Fact]
    public void A_removed_line_that_starts_with_dashes_is_content_inside_a_hunk()
    {
        const string diff = "--- a/q.sql\n+++ b/q.sql\n@@ -1,2 +1 @@\n-- old comment\n select 1\n";

        var lines = DiffParser.Parse(diff);

        Assert.Equal(new DiffLine(DiffLineKind.Removed, "-- old comment"), lines[1]);
    }

    [Fact]
    public void Metadata_lines_become_notes()
    {
        const string diff = """
            diff --git a/old.txt b/new name.txt
            similarity index 100%
            rename from old.txt
            rename to new name.txt
            """;

        var lines = DiffParser.Parse(diff);

        Assert.All(lines, l => Assert.Equal(DiffLineKind.Note, l.Kind));
        Assert.Contains(lines, l => l.Text == "rename from old.txt");
    }

    [Fact]
    public void Binary_files_and_missing_newline_markers_are_notes()
    {
        var binary = DiffParser.Parse("Binary files a/x.bin and b/x.bin differ\n");
        var noNewline = DiffParser.Parse("@@ -1 +1 @@\n-a\n+b\n\\ No newline at end of file\n");

        Assert.Equal(DiffLineKind.Note, Assert.Single(binary).Kind);
        Assert.Equal(new DiffLine(DiffLineKind.Note, "\\ No newline at end of file"), noNewline[3]);
    }

    [Fact]
    public void Tabs_expand_to_four_spaces()
    {
        var lines = DiffParser.Parse("@@ -0,0 +1 @@\n+\tx\n");

        Assert.Equal("+    x", lines[1].Text);
    }

    [Fact]
    public void Output_is_capped_at_max_lines()
    {
        var body = string.Concat(Enumerable.Range(0, 300).Select(i => $"+line {i}\n"));

        var lines = DiffParser.Parse("@@ -0,0 +1,300 @@\n" + body);

        Assert.Equal(DiffParser.DefaultMaxLines, lines.Count);
    }

    [Fact]
    public void Empty_output_is_a_single_no_changes_note()
    {
        var lines = DiffParser.Parse("");

        Assert.Equal(new DiffLine(DiffLineKind.Note, "(no changes to show)"), Assert.Single(lines));
    }
}
