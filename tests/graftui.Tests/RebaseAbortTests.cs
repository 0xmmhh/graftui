using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// RebaseAbort, the fallback abort of a conflicting rebase, against a real conflicting rebase in a temp repo: git's abort runs, and the
/// branch must be back on its old tip with no rebase in progress before the fallback reports success.
/// </summary>
public class RebaseAbortTests
{
    private static string GitDir(TempRepo repo) => Path.Combine(repo.Dir, ".git");

    /// <summary>Starts the conflicting rebase and asserts that it stopped at the conflict.</summary>
    private static void StartConflict(TempRepo repo)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);
    }

    [Fact]
    public void Force_aborts_a_conflicting_rebase_and_restores_the_branch()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        StartConflict(repo);
        var log = new List<GitResult>();

        var restored = RebaseAbort.Force(repo.Git, GitDir(repo), "feature", before, log);

        Assert.True(restored);
        Assert.Equal(before, repo.Head);
        Assert.Equal("refs/heads/feature", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.True(repo.IsClean);
        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.False(File.Exists(Path.Combine(GitDir(repo), SequenceStateFile.FileName)));
        // The abort is a logged git call, so the Commands pane shows it.
        Assert.Contains(log, r => r.Args.SequenceEqual(["rebase", "--abort"]));
    }

    [Fact]
    public void Force_reports_failure_when_the_branch_is_not_back_on_the_expected_tip()
    {
        using var repo = SequenceRepos.FeatureConflict();
        StartConflict(repo);
        var log = new List<GitResult>();

        var restored = RebaseAbort.Force(repo.Git, GitDir(repo), "feature", "0000000000000000000000000000000000000000", log);

        Assert.False(restored);
    }

    [Fact]
    public void Force_reports_failure_when_git_still_holds_the_rebase_after_its_abort()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        StartConflict(repo);
        // git cannot abort a rebase whose onto is not a commit, so the rebase is still there after its abort.
        File.WriteAllText(Path.Combine(GitDir(repo), "rebase-merge", "onto"), "not-a-commit\n");
        var log = new List<GitResult>();

        var restored = RebaseAbort.Force(repo.Git, GitDir(repo), "feature", before, log);

        Assert.False(restored);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.Contains(log, r => r.Args.SequenceEqual(["rebase", "--abort"]));
    }

    [Fact]
    public void Force_on_a_repo_with_no_rebase_only_checks_the_branch()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        var log = new List<GitResult>();

        var restored = RebaseAbort.Force(repo.Git, GitDir(repo), "feature", before, log);

        Assert.True(restored);
        Assert.DoesNotContain(log, r => r.Args.Count > 0 && r.Args[0] == "rebase");
    }
}
