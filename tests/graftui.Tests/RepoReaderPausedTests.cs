using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// The snapshot reads a paused rebase: the pause graftui started, its old tip, and the resolved flags, from the top level even when graftui
/// starts in a subfolder. Each case pauses a rebase in a throwaway repo.
/// </summary>
public class RepoReaderPausedTests
{
    private static SequenceOutcome Pause(TempRepo repo)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        return outcome;
    }

    private static RepoSnapshot Load(GitClient git) => new RepoReader(git).LoadSnapshot(withCounts: false, CancellationToken.None);

    [Fact]
    public void A_paused_rebase_is_read_as_a_conflict_graftui_started_with_its_old_tip()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        Pause(repo);

        var snap = Load(repo.Git);

        Assert.NotNull(snap.Paused);
        Assert.True(snap.Paused.Ours);
        Assert.Equal(SequenceKind.Rebase, snap.Paused.Kind);
        Assert.Equal(PauseKind.Conflict, snap.Paused.Pause);
        Assert.Equal("feature", snap.Paused.Branch);
        Assert.Equal(new[] { "app.txt" }, snap.Paused.Files.Select(f => f.Path));
        Assert.False(snap.Paused.Files[0].Resolved);
        Assert.Equal(before, snap.PausedOldTip);
    }

    [Fact]
    public void A_clean_repo_has_no_pause_and_no_old_tip()
    {
        using var repo = SequenceRepos.Feature4();

        var snap = Load(repo.Git);

        Assert.Null(snap.Paused);
        Assert.Null(snap.PausedOldTip);
    }

    [Fact]
    public void A_paused_rebase_layout_is_read_from_the_branch_old_tip_not_from_the_detached_head()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        Pause(repo);
        var snap = Load(repo.Git);

        var layout = new RepoReader(repo.Git).LoadRebaseLayout("feature", before, "main", snap, CancellationToken.None);

        Assert.Equal("", snap.CurrentBranch);
        Assert.Equal("feature", layout.Current);
        Assert.False(layout.Detached);
        Assert.Equal(before, layout.CurrentTip);
        Assert.Equal("main", layout.Selected);
        Assert.Equal(new[] { "Fix typo in form", "Add password check", "WIP debug logging" }, layout.CurrentOwn.Select(c => c.Subject));
        Assert.True(layout.CurrentOwnMore);
        Assert.Equal(new[] { "Change line 1 on main" }, layout.SelectedOwn.Select(c => c.Subject));
        Assert.Equal(new[] { "Add app" }, layout.Shared.Select(c => c.Subject));
        Assert.Equal(BranchLanes.LaneOf(snap.Branches, "feature"), layout.CurrentLane);
        Assert.Equal(BranchLanes.LaneOf(snap.Branches, "main"), layout.SelectedLane);
    }

    [Fact]
    public void A_reader_started_in_a_subfolder_finds_the_conflicted_file_and_its_resolved_flag()
    {
        using var repo = SequenceRepos.FeatureConflict();
        Pause(repo);
        var subfolder = Path.Combine(repo.Dir, "sub");
        Directory.CreateDirectory(subfolder);
        var reader = new RepoReader(new GitClient(subfolder));

        var open = reader.LoadSnapshot(withCounts: false, CancellationToken.None);
        repo.Write("app.txt", "line 1 (resolved)\n");
        var resolved = reader.LoadSnapshot(withCounts: false, CancellationToken.None);

        Assert.NotNull(open.Paused);
        Assert.False(open.Paused.Files[0].Resolved);
        Assert.True(resolved.Paused!.Files[0].Resolved);
    }
}
