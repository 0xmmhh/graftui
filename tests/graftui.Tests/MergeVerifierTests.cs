using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// The read-back after a merge. Every test runs in its own temp repo (TempRepo), never the sandbox or the graftui repo.
/// </summary>
public sealed class MergeVerifierTests
{
    private static readonly string[] SymbolicHeadArgs = ["symbolic-ref", "-q", "HEAD"];
    private static readonly string[] RevHeadArgs = ["rev-parse", "-q", "--verify", "HEAD"];
    private static readonly string[] RevMergeHeadArgs = ["rev-parse", "-q", "--verify", "MERGE_HEAD"];
    private static readonly string[] StatusArgs = ["status", "--porcelain", "--untracked-files=normal"];

    // The outcome of a merge of feature into main, as MergeOperation makes it.
    private static MergeOutcome Merge(TempRepo repo, string branch)
    {
        var op = new MergeOperation(repo.Git);
        var request = new MergeRequest(branch);
        var check = op.Check(request);
        return op.Run(request, check);
    }

    private static MergeOutcome Outcome(MergeKind kind, string? newHead) =>
        new(kind, "feature", "main", newHead, [], "message", []);

    [Theory]
    [InlineData(MergeKind.MergeCommit, "A", "M", "M")]
    [InlineData(MergeKind.MergeCommit, "A", "A", null)]
    [InlineData(MergeKind.MergeCommit, "A", null, null)]
    [InlineData(MergeKind.MergeCommit, null, "M", null)]
    [InlineData(MergeKind.FastForward, "A", "F", "F")]
    [InlineData(MergeKind.FastForward, "A", null, null)]
    [InlineData(MergeKind.Conflict, "A", null, "A")]
    [InlineData(MergeKind.UpToDate, "A", "A", "A")]
    [InlineData(MergeKind.Failed, "A", null, "A")]
    [InlineData(MergeKind.Failed, "A", "X", null)]
    [InlineData(MergeKind.Conflict, null, null, null)]
    public void Expected_head_follows_the_outcome(MergeKind kind, string? before, string? newHead, string? expected)
    {
        Assert.Equal(expected, MergeVerifier.ExpectedHead(Outcome(kind, newHead), before));
    }

    [Fact]
    public void A_merge_commit_is_verified_with_its_new_head()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var outcome = Merge(repo, "feature");

        Assert.Equal(MergeKind.MergeCommit, outcome.Kind);
        var verdict = MergeVerifier.Check(repo.Git, outcome, before);

        Assert.True(verdict.Verified);
        Assert.True(verdict.OnBranch);
        Assert.Equal(outcome.NewHead, verdict.Head);
        Assert.False(verdict.Merging);
        Assert.True(verdict.Clean);
    }

    [Fact]
    public void The_four_reads_are_the_commands_in_order()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var verdict = MergeVerifier.Check(repo.Git, Merge(repo, "feature"), before);

        Assert.Equal(4, verdict.Commands.Count);
        Assert.Equal(SymbolicHeadArgs, verdict.Commands[0].Args.ToArray());
        Assert.Equal(RevHeadArgs, verdict.Commands[1].Args.ToArray());
        Assert.Equal(RevMergeHeadArgs, verdict.Commands[2].Args.ToArray());
        Assert.Equal(StatusArgs, verdict.Commands[3].Args.ToArray());
    }

    [Fact]
    public void A_fast_forward_is_verified()
    {
        using var repo = TempRepo.FastForwardable();
        var before = repo.Head;
        var outcome = Merge(repo, "feature");

        Assert.Equal(MergeKind.FastForward, outcome.Kind);
        Assert.True(MergeVerifier.Check(repo.Git, outcome, before).Verified);
    }

    [Fact]
    public void A_conflict_is_verified_with_head_where_it_was()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var before = repo.Head;
        var outcome = Merge(repo, "feature");

        Assert.Equal(MergeKind.Conflict, outcome.Kind);
        var verdict = MergeVerifier.Check(repo.Git, outcome, before);

        Assert.True(verdict.Verified);
        Assert.Equal(before, verdict.Head);
    }

    [Fact]
    public void An_up_to_date_merge_is_verified()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Run("branch", "feature");
        var before = repo.Head;
        var outcome = Merge(repo, "feature");

        Assert.Equal(MergeKind.UpToDate, outcome.Kind);
        Assert.True(MergeVerifier.Check(repo.Git, outcome, before).Verified);
    }

    [Fact]
    public void An_untracked_file_left_behind_is_not_verified()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var outcome = Merge(repo, "feature");
        repo.Write("new.txt", "left behind\n");

        var verdict = MergeVerifier.Check(repo.Git, outcome, before);

        Assert.False(verdict.Verified);
        Assert.False(verdict.Clean);
    }

    [Fact]
    public void A_merge_still_in_progress_is_not_verified()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        // A branch with a commit of its own, so that merging it stops at --no-commit and leaves MERGE_HEAD behind.
        repo.Run("switch", "-q", "-c", "late", "feature");
        repo.Commit("d.txt", "d\n", "Add d on late");
        repo.Run("switch", "-q", "main");
        var before = repo.Head;
        var outcome = Merge(repo, "feature");

        repo.Run("merge", "--no-commit", "--no-ff", "late");
        var verdict = MergeVerifier.Check(repo.Git, outcome, before);
        repo.Run("merge", "--abort");

        Assert.True(verdict.Merging);
        Assert.False(verdict.Verified);
    }

    [Fact]
    public void A_detached_head_is_not_verified()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var outcome = Merge(repo, "feature");
        repo.Run("switch", "-q", "--detach");

        var verdict = MergeVerifier.Check(repo.Git, outcome, before);

        Assert.False(verdict.OnBranch);
        Assert.False(verdict.Verified);
    }

    [Fact]
    public void A_commit_made_after_the_merge_is_not_verified()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var outcome = Merge(repo, "feature");
        repo.Commit("e.txt", "e\n", "Add e after the merge");

        var verdict = MergeVerifier.Check(repo.Git, outcome, before);

        Assert.NotEqual(outcome.NewHead, verdict.Head);
        Assert.False(verdict.Verified);
    }

    [Fact]
    public void After_the_checks_the_repo_is_clean_and_not_mid_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var outcome = Merge(repo, "feature");
        MergeVerifier.Check(repo.Git, outcome, before);

        Assert.True(repo.IsClean);
        Assert.False(repo.Merging);
    }
}
