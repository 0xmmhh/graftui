using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>SequenceReader against throwaway repos: the paused state, the not-ours case, already-applied marks and conflict markers.</summary>
public class SequenceReaderTests
{
    [Fact]
    public void ReadPaused_is_null_on_a_clean_repo()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");

        Assert.Null(new SequenceReader(repo.Git).ReadPaused());
    }

    [Fact]
    public void ReadPaused_reports_a_rebase_graftui_did_not_start_as_not_ours()
    {
        using var repo = SequenceRepos.Feature4();
        var todo = Path.Combine(Path.GetDirectoryName(repo.Dir)!, Guid.NewGuid().ToString("N") + ".todo");
        File.WriteAllText(todo, "edit " + repo.Run("rev-parse", "feature~3") + "\npick " + repo.Run("rev-parse", "feature~2") + "\n");
        try
        {
            var env = new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = "cp '" + todo + "'" };
            var started = repo.Git.RunWithEnv(env, "rebase", "-i", "main");
            Assert.True(started.Success, started.StdErr);

            var stop = new SequenceReader(repo.Git).ReadPaused();

            Assert.NotNull(stop);
            Assert.False(stop.Ours);
            Assert.Equal(SequenceKind.Rebase, stop.Kind);
            Assert.Equal("", stop.Branch);
        }
        finally
        {
            File.Delete(todo);
            repo.Git.Run("rebase", "--abort");
        }
    }

    [Fact]
    public void AlreadyApplied_marks_a_selected_commit_whose_change_is_on_the_current_branch()
    {
        using var repo = SequenceRepos.Feature4();
        var feature1 = repo.Run("rev-parse", "feature~1");
        var feature = repo.Run("rev-parse", "feature");
        repo.Run("switch", "-q", "main");
        repo.Run("cherry-pick", feature1);

        var applied = new SequenceReader(repo.Git).AlreadyApplied(repo.Head, feature);

        Assert.Contains(feature1, applied);
        Assert.DoesNotContain(feature, applied);
    }

    [Fact]
    public void ConflictState_reads_markers_from_the_work_tree()
    {
        using var repo = TempRepo.Create();
        File.WriteAllText(Path.Combine(repo.Dir, "app.txt"), "<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> feature\n");
        File.WriteAllText(Path.Combine(repo.Dir, "clean.txt"), "fine\n");

        var files = SequenceReader.ConflictState(repo.Dir, ["app.txt", "clean.txt"]);

        Assert.Equal(new[] { new ConflictFile("app.txt", false), new ConflictFile("clean.txt", true) }, files);
    }

    [Fact]
    public void ReadSequence_reads_a_graftui_pause_with_its_old_tip_in_one_pass()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);

        var read = new SequenceReader(repo.Git).ReadSequence();

        Assert.NotNull(read.Stop);
        Assert.True(read.Stop.Ours);
        Assert.Equal(before, read.OldTip);
        Assert.Null(read.Unreadable);
    }

    [Fact]
    public void ReadSequence_reports_a_graftui_rebase_whose_stop_cannot_be_read_as_unreadable()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);
        // Without the done list git's stop cannot be read, so the sequence is still graftui's but has no stop to show.
        File.Delete(Path.Combine(repo.Dir, ".git", "rebase-merge", "done"));

        var read = new SequenceReader(repo.Git).ReadSequence();

        Assert.Null(read.Stop);
        Assert.Null(read.OldTip);
        Assert.Equal("rebase", read.Unreadable);
    }

    [Fact]
    public void ReadSequence_of_a_clean_repo_holds_nothing()
    {
        using var repo = SequenceRepos.Feature4();

        var read = new SequenceReader(repo.Git).ReadSequence();

        Assert.Null(read.Stop);
        Assert.Null(read.OldTip);
        Assert.Null(read.Unreadable);
    }
}
