using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>The one-line parser of the lane graph's git log. Pure: no git runs.</summary>
public sealed class GraphCommitParseTests
{
    // Written as an interpolated value, not "\x1f", because C# reads "\x1fb" as one hex escape.
    private const char Sep = '\x1f';

    [Fact]
    public void A_subject_with_the_separator_keeps_its_text()
    {
        var commit = GitClient.ParseGraphCommit($"h{Sep}s{Sep}p{Sep}1{Sep}a{Sep}b");

        Assert.Equal("a" + Sep + "b", commit!.Subject);
    }

    [Fact]
    public void An_empty_subject_is_an_empty_string()
    {
        var commit = GitClient.ParseGraphCommit($"h{Sep}s{Sep}p{Sep}1{Sep}");

        Assert.Equal("", commit!.Subject);
    }

    [Fact]
    public void A_three_parent_commit_keeps_its_parents_in_order()
    {
        var commit = GitClient.ParseGraphCommit($"h{Sep}s{Sep}p1 p2 p3{Sep}1{Sep}octopus");

        Assert.Equal(new[] { "p1", "p2", "p3" }, commit!.Parents.ToArray());
    }

    [Fact]
    public void A_root_commit_has_no_parents()
    {
        var commit = GitClient.ParseGraphCommit($"h{Sep}s{Sep}{Sep}1{Sep}first");

        Assert.Empty(commit!.Parents);
    }

    [Fact]
    public void A_line_with_fewer_than_five_fields_is_skipped()
    {
        Assert.Null(GitClient.ParseGraphCommit($"h{Sep}s{Sep}p"));
    }
}

/// <summary>The cap on branch tips in the lane graph read. Pure: the tips come from a list, not from git.</summary>
public sealed class LaneGraphTipsTests
{
    [Fact]
    public void Tips_skip_the_base_and_repeats_and_stop_at_the_cap()
    {
        var branches = new[] { Br("main", "m"), Br("a", "x1"), Br("b", "x1"), Br("c", "x2"), Br("d", "x3"), Br("e", "m") };

        var capped = RepoReader.TipsOf(branches, "main", "m", 2, out var cut);

        Assert.Equal(new[] { "x1", "x2" }, capped.ToArray());
        Assert.True(cut);
    }

    [Fact]
    public void Under_the_cap_every_distinct_tip_is_kept_and_nothing_is_cut()
    {
        var branches = new[] { Br("main", "m"), Br("a", "x1"), Br("b", "x1"), Br("c", "x2"), Br("d", "x3"), Br("e", "m") };

        var all = RepoReader.TipsOf(branches, "main", "m", 3, out var cut);

        Assert.Equal(new[] { "x1", "x2", "x3" }, all.ToArray());
        Assert.False(cut);
    }

    private static Branch Br(string name, string hash) => new(name, hash, hash, false);
}
