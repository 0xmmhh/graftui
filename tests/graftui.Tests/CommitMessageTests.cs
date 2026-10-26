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

        var (subject, body, blank) = CommitMessage.Split(message);

        Assert.Equal("Subj", subject);
        Assert.Equal("line one\n\nline three", body);
        Assert.True(blank);
    }

    [Fact]
    public void Split_of_a_subject_only_message_has_an_empty_body()
    {
        var (subject, body, _) = CommitMessage.Split("only subject\n");

        Assert.Equal("only subject", subject);
        Assert.Equal("", body);
    }

    [Theory]
    [InlineData("s\n")]
    [InlineData("s\n\nb1\nb2\n")]
    [InlineData("s\n\nb1\n\nb2\n")]
    [InlineData("line1\nline2\n\nbody\n")]
    [InlineData("line1\nline2\nline3\n\nbody\n")]
    [InlineData("line1\n\nbody\n")]
    public void Splitting_then_building_gives_back_the_same_message(string message)
    {
        var (subject, body, blank) = CommitMessage.Split(message);

        Assert.Equal(message, CommitMessage.Build(subject, body, blank));
    }

    [Fact]
    public void A_subject_that_wraps_onto_the_next_line_gets_no_blank_line_when_amended()
    {
        // The second line is part of the subject's paragraph. Saving must not insert a blank line after the first line.
        var (subject, body, blank) = CommitMessage.Split("Fix the parser for long\nsubjects\n\nDetails here\n");

        Assert.Equal("Fix the parser for long", subject);
        Assert.Equal("subjects\n\nDetails here", body);
        Assert.False(blank);
        Assert.Equal("Fix the parser for long\nsubjects\n\nDetails here\n", CommitMessage.Build(subject, body, blank));
    }

    [Fact]
    public void Exactly_one_blank_line_after_the_subject_is_removed_from_the_body()
    {
        var (_, body, blank) = CommitMessage.Split("s\n\n\nbody");

        Assert.True(blank);
        Assert.Equal("\nbody", body);
    }

    [Fact]
    public void A_typed_body_with_leading_blank_lines_is_built_without_them()
    {
        Assert.Equal("s\n\nb\n", CommitMessage.Build("s", "\n\nb"));
    }
}
