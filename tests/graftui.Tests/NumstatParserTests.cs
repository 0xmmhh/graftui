using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

public class NumstatParserTests
{
    [Fact]
    public void Three_plain_records_give_three_files_in_git_order()
    {
        var files = NumstatParser.Parse("1\t1\tREADME.md\0" + "1\t0\tVERSION\0" + "1\t0\tlog.txt\0");

        Assert.NotNull(files);
        Assert.Equal(3, files.Count);
        Assert.Equal(new IncomingFile("README.md", null, 1, 1), files.Files[0]);
        Assert.Equal(new IncomingFile("VERSION", null, 1, 0), files.Files[1]);
        Assert.Equal(new IncomingFile("log.txt", null, 1, 0), files.Files[2]);
    }

    [Fact]
    public void A_binary_file_has_no_counts()
    {
        var files = NumstatParser.Parse("-\t-\tlogo.png\0");

        Assert.NotNull(files);
        Assert.Equal(new IncomingFile("logo.png", null, null, null), Assert.Single(files.Files));
    }

    [Fact]
    public void A_rename_is_one_file_with_the_new_path_and_the_old_path()
    {
        var files = NumstatParser.Parse("0\t0\t\0a.txt\0b.txt\0");

        Assert.NotNull(files);
        Assert.Equal(1, files.Count);
        Assert.Equal(new IncomingFile("b.txt", "a.txt", 0, 0), Assert.Single(files.Files));
    }

    [Fact]
    public void A_rename_between_two_plain_records_keeps_both_in_order()
    {
        var files = NumstatParser.Parse("1\t0\tfirst.txt\0" + "2\t1\t\0old.txt\0new.txt\0" + "3\t0\tlast.txt\0");

        Assert.NotNull(files);
        Assert.Equal(3, files.Count);
        Assert.Equal("first.txt", files.Files[0].Path);
        Assert.Equal(new IncomingFile("new.txt", "old.txt", 2, 1), files.Files[1]);
        Assert.Equal("last.txt", files.Files[2].Path);
    }

    [Fact]
    public void A_path_with_a_tab_keeps_the_tab()
    {
        var files = NumstatParser.Parse("2\t0\tdocs/a\tb.md\0");

        Assert.NotNull(files);
        Assert.Equal(new IncomingFile("docs/a\tb.md", null, 2, 0), Assert.Single(files.Files));
    }

    [Fact]
    public void Empty_output_is_no_files()
    {
        var files = NumstatParser.Parse("");

        Assert.NotNull(files);
        Assert.Equal(0, files.Count);
        Assert.Empty(files.Files);
    }

    [Fact]
    public void A_count_that_is_not_a_number_is_malformed()
    {
        Assert.Null(NumstatParser.Parse("x\t1\tf\0"));
    }

    [Fact]
    public void A_record_with_too_few_fields_is_malformed()
    {
        Assert.Null(NumstatParser.Parse("1\t2\0"));
    }

    [Fact]
    public void A_rename_without_its_second_path_is_malformed()
    {
        Assert.Null(NumstatParser.Parse("0\t0\t\0a.txt\0"));
    }

    [Fact]
    public void An_empty_record_in_the_middle_is_malformed()
    {
        Assert.Null(NumstatParser.Parse("1\t0\ta.txt\0\0" + "1\t0\tb.txt\0"));
    }

    [Fact]
    public void More_than_the_kept_maximum_keeps_the_first_files_and_counts_them_all()
    {
        var text = string.Concat(Enumerable.Range(0, 250).Select(i => $"1\t0\tf{i}.txt\0"));

        var files = NumstatParser.Parse(text);

        Assert.NotNull(files);
        Assert.Equal(250, files.Count);
        Assert.Equal(IncomingFiles.FilesMax, files.Files.Count);
        Assert.Equal("f0.txt", files.Files[0].Path);
        Assert.Equal("f199.txt", files.Files[199].Path);
    }
}
