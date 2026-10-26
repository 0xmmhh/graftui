using graftui.Operations;

namespace graftui.Tests;

public class CommitMessageTests
{
    [Fact]
    public void A_subject_alone_ends_in_one_newline()
    {
        Assert.Equal("s\n", CommitMessage.Build("s", ""));
    }

    [Fact]
    public void A_subject_and_body_are_separated_by_a_blank_line()
    {
        Assert.Equal("s\n\nb1\nb2\n", CommitMessage.Build("s", "b1\nb2"));
    }

    [Fact]
    public void Windows_line_endings_are_normalised()
    {
        Assert.Equal("s\n\nb1\nb2\n", CommitMessage.Build("s\r\n", "b1\r\nb2\r\n"));
    }

    [Fact]
    public void A_blank_body_is_dropped_and_leading_blank_lines_are_skipped()
    {
        Assert.Equal("s\n", CommitMessage.Build("s", "\n  \n\t\n"));
        Assert.Equal("s\n\nb\n", CommitMessage.Build("s", "\n\nb"));
    }

    [Fact]
    public void A_newline_in_the_subject_becomes_a_space()
    {
        Assert.Equal("one two\n", CommitMessage.Build("one\ntwo", ""));
    }

    [Fact]
    public void Split_reads_back_what_build_wrote()
    {
        var message = CommitMessage.Build("Subj", "line one\n\nline three");

        var (subject, body) = CommitMessage.Split(message);

        Assert.Equal("Subj", subject);
        Assert.Equal("line one\n\nline three", body);
    }

    [Fact]
    public void Split_of_a_subject_only_message_has_an_empty_body()
    {
        var (subject, body) = CommitMessage.Split("only subject\n");

        Assert.Equal("only subject", subject);
        Assert.Equal("", body);
    }
}
