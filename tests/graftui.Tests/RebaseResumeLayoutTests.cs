using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using Terminal.Gui.App;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The layout a resume of a paused rebase animates from. A rebase's branch is read from its old tip, so the layout has the branch's own
/// commits; a stop with no old tip, a cherry-pick, or an onto that is not listed keeps the bare pair the snapshot gives. They share a headless
/// Terminal.Gui session, so they run one at a time.
/// </summary>
[Collection("TerminalGui")]
public sealed class RebaseResumeLayoutTests : IDisposable
{
    private readonly IApplication _ui;

    public RebaseResumeLayoutTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    /// <summary>A rebase of feature onto main that conflicts and pauses, with the app open on the pause.</summary>
    private (AppController App, RepoSnapshot Snap, SequenceStop Stop, string OldTip) Paused(TempRepo repo)
    {
        var oldTip = repo.Head;
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);

        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow();
        return (app, snapshot, snapshot.Paused!, oldTip);
    }

    /// <summary>The bare pair the resume used before it read the rebase's layout: HEAD is detached, so the snapshot's current name is empty.</summary>
    private static GraphLayout Bare(RepoSnapshot snap) =>
        GraphLayout.Minimal(snap.CurrentBranch, snap.CurrentBranch.Length == 0, "main", snap.HeadHash ?? "", "mainhash");

    [Fact]
    public void A_resume_of_a_paused_rebase_animates_from_the_branch_as_the_rebase_started_it()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var (app, snap, stop, oldTip) = Paused(repo);

        var layout = app.BranchesTab.ResumeLayout(stop, oldTip, snap, Bare(snap));

        Assert.Equal("feature", layout.Current);
        Assert.False(layout.Detached);
        Assert.Equal(oldTip, layout.CurrentTip);
        Assert.Equal(new[] { "Change line 1 on main" }, layout.SelectedOwn.Select(c => c.Subject));
    }

    [Fact]
    public void Without_an_old_tip_the_resume_keeps_the_bare_pair()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var (app, snap, stop, _) = Paused(repo);
        var bare = Bare(snap);

        var layout = app.BranchesTab.ResumeLayout(stop, null, snap, bare);

        Assert.Same(bare, layout);
    }

    [Fact]
    public void A_cherry_pick_stop_keeps_the_bare_pair_even_with_an_old_tip()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var (app, snap, stop, oldTip) = Paused(repo);
        var bare = Bare(snap);

        var layout = app.BranchesTab.ResumeLayout(stop with { Kind = SequenceKind.CherryPick }, oldTip, snap, bare);

        Assert.Same(bare, layout);
    }

    [Fact]
    public void A_failed_read_keeps_the_bare_pair()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var (app, snap, stop, oldTip) = Paused(repo);
        var bare = Bare(snap);

        var layout = app.BranchesTab.ResumeLayout(stop with { Onto = "not-listed" }, oldTip, snap, bare);

        Assert.Same(bare, layout);
    }
}
