using graftui.Git;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>BranchNames.Problem against throwaway repos: the names git accepts and refuses, and the names that are taken.</summary>
public class BranchNameSafetyTests
{
    private static string? Problem(TempRepo repo, string name, string? allowExisting = null) =>
        BranchNames.Problem(repo.Git, name, allowExisting, []);

    [Theory]
    [InlineData("feature/x")]
    [InlineData("foo")]
    [InlineData("Ünï")]
    [InlineData("topic-2.0_a")]
    [InlineData("refs/heads/x")]
    public void Names_git_accepts_and_that_are_free_pass(string name)
    {
        using var repo = TempRepo.FastForwardable();

        Assert.Null(Problem(repo, name));
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("-x")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("x.lock")]
    [InlineData("")]
    [InlineData("foo/")]
    [InlineData("x/.y")]
    [InlineData("a@{b")]
    [InlineData("@")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    public void Names_git_refuses_are_invalid(string name)
    {
        using var repo = TempRepo.FastForwardable();

        Assert.Equal(BranchRefusals.InvalidName, Problem(repo, name));
    }

    [Fact]
    public void At_minus_one_is_invalid_even_though_git_exits_zero_for_it()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("switch", "-q", "-c", "other");
        repo.Run("switch", "-q", "main");

        // check-ref-format --branch "@{-1}" exits 0 and prints "other": the name is not the one typed.
        var log = new List<GitResult>();
        Assert.Equal(BranchRefusals.InvalidName, BranchNames.Problem(repo.Git, "@{-1}", null, log));
        Assert.Equal("other", Assert.Single(log, r => r.Args.Count > 0 && r.Args[0] == "check-ref-format").StdOut.Trim());
    }

    [Fact]
    public void An_existing_branch_is_refused_as_exists()
    {
        using var repo = TempRepo.FastForwardable();

        Assert.Equal(BranchRefusals.Exists("main"), Problem(repo, "main"));
        Assert.Equal(BranchRefusals.Exists("feature"), Problem(repo, "feature"));
    }

    [Fact]
    public void allowExisting_lets_the_named_branch_pass_and_no_other()
    {
        using var repo = TempRepo.FastForwardable();

        Assert.Null(Problem(repo, "main", allowExisting: "main"));
        Assert.Equal(BranchRefusals.Exists("feature"), Problem(repo, "feature", allowExisting: "main"));
    }

    [Fact]
    public void Refusals_before_git_run_no_command()
    {
        using var repo = TempRepo.FastForwardable();
        var log = new List<GitResult>();

        Assert.Equal(BranchRefusals.InvalidName, BranchNames.Problem(repo.Git, "-x", null, log));
        Assert.Equal(BranchRefusals.InvalidName, BranchNames.Problem(repo.Git, "@", null, log));
        Assert.Equal(BranchRefusals.InvalidName, BranchNames.Problem(repo.Git, " lead", null, log));
        Assert.Empty(log);
    }

    [Fact]
    public void A_valid_free_name_is_checked_with_check_ref_format_then_rev_parse()
    {
        using var repo = TempRepo.FastForwardable();
        var log = new List<GitResult>();

        Assert.Null(BranchNames.Problem(repo.Git, "topic", null, log));
        Assert.Collection(log,
            r => Assert.Equal(new[] { "check-ref-format", "--branch", "topic" }, r.Args),
            r => Assert.Equal(new[] { "rev-parse", "-q", "--verify", "refs/heads/topic" }, r.Args));
    }

    [Fact]
    public void Problem_changes_no_refs()
    {
        using var repo = TempRepo.FastForwardable();
        var before = repo.Run("for-each-ref");

        Problem(repo, "topic");
        Problem(repo, "main");

        Assert.Equal(before, repo.Run("for-each-ref"));
    }
}
