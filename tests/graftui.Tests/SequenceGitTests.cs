using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>The helpers the rebase and the cherry-pick share: quoting, conflict markers, move mapping and the state file.</summary>
public class SequenceGitTests
{
    [Fact]
    public void ShellQuote_escapes_single_quotes()
    {
        Assert.Equal("'/a/it'\\''s'", SequenceGit.ShellQuote("/a/it's"));
    }

    [Theory]
    [InlineData("<<<<<<< HEAD")]
    [InlineData("<<<<<<<")]
    [InlineData(">>>>>>> feature")]
    [InlineData(">>>>>>>\r")]
    public void HasMarkers_detects_each_marker_line(string markerLine)
    {
        using var repo = TempRepo.Create();
        var path = Path.Combine(repo.Dir, "app.txt");
        File.WriteAllText(path, "one\n" + markerLine + "\ntwo\n");

        Assert.True(SequenceGit.HasMarkers(path));
    }

    [Theory]
    [InlineData("=======")]
    [InlineData("<<<<<<<<")]
    [InlineData(">>>>>>>x")]
    public void HasMarkers_ignores_a_lone_separator_and_runs_that_are_not_exactly_seven(string line)
    {
        using var repo = TempRepo.Create();
        var path = Path.Combine(repo.Dir, "app.txt");
        File.WriteAllText(path, "one\n" + line + "\ntwo\n");

        Assert.False(SequenceGit.HasMarkers(path));
    }

    [Fact]
    public void HasMarkers_ignores_a_missing_file_and_a_clean_one()
    {
        using var repo = TempRepo.Create();
        var clean = Path.Combine(repo.Dir, "clean.txt");
        File.WriteAllText(clean, "=======x\n<<<<<<<<\nno marker here\n");

        Assert.False(SequenceGit.HasMarkers(Path.Combine(repo.Dir, "missing.txt")));
        Assert.False(SequenceGit.HasMarkers(clean));
    }

    [Fact]
    public void HasMarkers_does_not_follow_a_link_to_a_file_with_markers()
    {
        using var repo = TempRepo.Create();
        File.WriteAllText(Path.Combine(repo.Dir, "target.txt"), "<<<<<<< HEAD\n");
        File.CreateSymbolicLink(Path.Combine(repo.Dir, "link.txt"), "target.txt");

        Assert.False(SequenceGit.HasMarkers(Path.Combine(repo.Dir, "link.txt")));
    }

    [Fact]
    public void ReadConflicts_takes_a_link_only_from_the_sides_so_a_link_in_the_base_alone_leaves_the_text_conflict_text()
    {
        using var repo = SequenceRepos.LinkReplacedByText();
        Assert.Equal(1, repo.Git.Run("rebase", "main").ExitCode);
        try
        {
            var files = SequenceGit.ReadConflicts(repo.Git, new List<GitResult>());

            var ln = Assert.Single(files);
            Assert.Equal("ln", ln.Path);
            Assert.Equal(ConflictKind.Text, ln.Kind);
            Assert.False(ln.Resolved);
        }
        finally
        {
            repo.Run("rebase", "--abort");
        }
    }

    [Fact]
    public void MapMoves_zips_equal_lists_by_position()
    {
        var moves = SequenceGit.MapMoves(
            [("a1", "Add login form"), ("c3", "Add password check")],
            [("n1", "Edited login form"), ("n2", "Add password check")]);

        Assert.Equal(new[] { new CommitMove("a1", "n1"), new CommitMove("c3", "n2") }, moves);
    }

    [Fact]
    public void MapMoves_matches_by_subject_when_the_counts_differ()
    {
        // b2 was folded into a1, so only a1, c3 and d4 have new copies.
        var moves = SequenceGit.MapMoves(
            [("a1", "Add login form"), ("b2", "Fix typo in form"), ("c3", "Add password check")],
            [("n1", "Add login form"), ("n3", "Add password check")]);

        Assert.Equal(new[] { new CommitMove("a1", "n1"), new CommitMove("b2", null), new CommitMove("c3", "n3") }, moves);
    }

    [Fact]
    public void State_round_trips_through_the_file()
    {
        using var repo = TempRepo.Create();
        var gitDir = Path.Combine(repo.Dir, ".git");
        var state = new SequenceState(1, SequenceKind.Rebase, "feature", "main", "abc1234def",
            [new RebaseRow("a1", "a1", "Add login form", RebaseAction.Reword), new RebaseRow("b2", "b2", "Fix typo", RebaseAction.Fixup)],
            [], ["b2"], [], ["a1"], [], []);

        Assert.Null(SequenceStateFile.Save(gitDir, state));
        var loaded = SequenceStateFile.Load(gitDir);

        Assert.NotNull(loaded);
        Assert.Equal(SequenceKind.Rebase, loaded.Kind);
        Assert.Equal("feature", loaded.Branch);
        Assert.Equal("abc1234def", loaded.OldTip);
        Assert.Equal(new[] { RebaseAction.Reword, RebaseAction.Fixup }, loaded.Rows.Select(r => r.Action));
        Assert.Equal(new[] { "b2" }, loaded.Skipped);
        Assert.Equal(new[] { "a1" }, loaded.Reworded);
        Assert.Empty(loaded.Amended);
        SequenceStateFile.Delete(gitDir);
        Assert.Null(SequenceStateFile.Load(gitDir));
    }

    [Fact]
    public void A_corrupt_state_file_loads_as_null()
    {
        using var repo = TempRepo.Create();
        var gitDir = Path.Combine(repo.Dir, ".git");
        File.WriteAllText(Path.Combine(gitDir, SequenceStateFile.FileName), "{ not json");

        Assert.Null(SequenceStateFile.Load(gitDir));
    }
}
