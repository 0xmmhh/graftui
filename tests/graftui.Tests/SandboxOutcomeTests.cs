using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>The sandbox built by graftui --sandbox gives each merge outcome its own branch.</summary>
public class SandboxOutcomeTests
{
    private static readonly string[] ExpectedBranches =
        ["feature/conflict", "feature/fast-forward", "feature/ff-to-merge", "feature/merge-commit", "feature/picks", "main"];

    [Fact]
    public void The_progress_line_names_the_short_path_and_never_the_home_folder()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            var shown = new StringWriter();
            Sandbox.RebuildAt(dir, shown, "~/.graftui/sandbox");

            Assert.Contains("graftui: rebuilding practice repo at ~/.graftui/sandbox", shown.ToString());
            Assert.DoesNotContain(Path.GetFullPath(dir), shown.ToString());
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void The_sandbox_has_the_expected_branches_on_a_clean_main()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);
            var git = new GitClient(dir);

            var branches = git.Run("for-each-ref", "--format=%(refname:short)", "refs/heads").StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(ExpectedBranches, branches);
            Assert.Equal("main", git.Run("symbolic-ref", "--short", "HEAD").StdOut.Trim());
            Assert.True(git.IsWorkingTreeClean());
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Theory]
    [InlineData("feature/fast-forward", MergeKind.FastForward)]
    [InlineData("feature/ff-to-merge", MergeKind.FastForward)]
    [InlineData("feature/merge-commit", MergeKind.MergeCommit)]
    [InlineData("feature/conflict", MergeKind.Conflict)]
    [InlineData("feature/picks", MergeKind.MergeCommit)]
    public void Each_branch_merges_with_its_own_outcome(string branch, MergeKind expected)
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);
            var git = new GitClient(dir);
            var op = new MergeOperation(git);

            var check = op.Check(new MergeRequest(branch));
            Assert.True(check.CanMerge, check.Refusal);
            var outcome = op.Run(new MergeRequest(branch), check);

            Assert.Equal(expected, outcome.Kind);
            Assert.True(git.IsWorkingTreeClean());
            Assert.False(git.Run("rev-parse", "-q", "--verify", "MERGE_HEAD").Success);
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void A_conflict_names_app_txt_and_leaves_main_unchanged()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);
            var git = new GitClient(dir);
            var head = git.Run("rev-parse", "HEAD").StdOut.Trim();
            var op = new MergeOperation(git);

            var check = op.Check(new MergeRequest("feature/conflict"));
            var outcome = op.Run(new MergeRequest("feature/conflict"), check);

            Assert.Equal(new[] { "app.txt" }, outcome.ConflictFiles);
            Assert.Equal(head, git.Run("rev-parse", "HEAD").StdOut.Trim());
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void Fast_forward_onto_a_merge_commit_gives_main_a_merge_commit_as_its_tip()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);
            var git = new GitClient(dir);
            var op = new MergeOperation(git);

            var outcome = op.Run(new MergeRequest("feature/ff-to-merge"), op.Check(new MergeRequest("feature/ff-to-merge")));

            Assert.Equal(MergeKind.FastForward, outcome.Kind);
            var parents = git.Run("rev-list", "--parents", "-n", "1", "HEAD").StdOut.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, parents.Length); // the commit, then its two parents
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void Merging_the_merge_commit_branch_twice_is_up_to_date_the_second_time()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);
            var op = new MergeOperation(new GitClient(dir));
            var first = op.Run(new MergeRequest("feature/merge-commit"), op.Check(new MergeRequest("feature/merge-commit")));
            Assert.Equal(MergeKind.MergeCommit, first.Kind);

            var second = op.Run(new MergeRequest("feature/merge-commit"), op.Check(new MergeRequest("feature/merge-commit")));

            Assert.Equal(MergeKind.UpToDate, second.Kind);
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }
}
