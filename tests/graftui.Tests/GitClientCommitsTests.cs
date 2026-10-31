using System.IO;

namespace graftui.Tests;

public class GitClientCommitsTests
{
    [Fact]
    public void Commits_not_in_the_other_tip_come_newest_first_and_stop_at_the_limit()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("b.txt", "b\n", "One");
        repo.Commit("c.txt", "c\n", "Two");
        repo.Commit("d.txt", "d\n", "Three");

        var commits = repo.Git.GetCommitsNotIn("feature", "main", 2);

        Assert.Equal(new[] { "Three", "Two" }, commits.Select(c => c.Subject));
    }

    [Fact]
    public void Each_commit_carries_its_committer_date_from_git()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");
        File.WriteAllText(Path.Combine(repo.Dir, "d.txt"), "d\n");
        repo.Run("add", "d.txt");
        var env = new Dictionary<string, string> { ["GIT_COMMITTER_DATE"] = "2024-03-05T10:00:00+00:00" };
        repo.Git.RunWithEnv(env, "commit", "-q", "-m", "Dated");

        var commit = repo.Git.GetCommit(repo.Head);

        Assert.Equal(new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.Zero), commit!.Date);
        Assert.Equal("Dated", commit.Subject);
    }
}
