using graftui.Git;

namespace graftui.Tests;

/// <summary>--make-edits only changes a clean sandbox that graftui made. Every test uses a temp repo, never the real sandbox.</summary>
public class SandboxEditsSafetyTests
{
    /// <summary>A temp repo shaped like the sandbox: README.md and app.txt committed, and the marker that graftui writes.</summary>
    private static TempRepo FakeSandbox()
    {
        var repo = TempRepo.Create();
        repo.Commit("README.md", "# sandbox\n", "Initial commit");
        repo.Commit("app.txt", "line 1\n", "Add app");
        File.WriteAllText(Path.Combine(repo.Dir, ".git", Sandbox.MarkerFile), "test\n");
        return repo;
    }

    private static string Read(TempRepo repo, string file) => File.ReadAllText(Path.Combine(repo.Dir, file));

    [Fact]
    public void Make_on_a_clean_repo_leaves_exactly_three_unstaged_changes()
    {
        using var repo = FakeSandbox();
        var head = repo.Head;

        var result = SandboxEdits.Make(repo.Git);

        Assert.True(result.Success);
        Assert.Equal("main", result.Message);
        Assert.Equal(" M README.md\n D app.txt\n?? notes.txt", repo.Status());
        Assert.Equal(head, repo.Head);
        Assert.True(repo.Git.Run("diff", "--cached", "--quiet").Success);
        Assert.Equal(SandboxEdits.NewFileText, Read(repo, "notes.txt"));
        Assert.EndsWith(SandboxEdits.AppendedLine + "\n", Read(repo, "README.md"));
    }

    [Fact]
    public void Make_on_a_dirty_repo_refuses_and_changes_nothing()
    {
        using var repo = FakeSandbox();
        repo.Write("x.txt", "mine\n");
        var status = repo.Status();

        var result = SandboxEdits.Make(repo.Git);

        Assert.False(result.Success);
        Assert.Contains("already has changes", result.Message);
        Assert.Equal(status, repo.Status());
        Assert.True(File.Exists(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal("# sandbox\n", Read(repo, "README.md"));
        Assert.Equal("mine\n", Read(repo, "x.txt"));
    }

    [Fact]
    public void Make_without_the_expected_files_refuses_and_leaves_the_repo_clean()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");

        var result = SandboxEdits.Make(repo.Git);

        Assert.False(result.Success);
        Assert.Contains("must be tracked", result.Message);
        Assert.True(repo.IsClean);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "notes.txt")));
    }

    [Fact]
    public void Make_on_a_detached_head_reports_it()
    {
        using var repo = FakeSandbox();
        repo.Run("checkout", "-q", "--detach");

        var result = SandboxEdits.Make(repo.Git);

        Assert.True(result.Success);
        Assert.Equal("detached HEAD", result.Message);
    }

    [Fact]
    public void CheckTarget_accepts_the_sandbox_and_a_subfolder()
    {
        using var repo = FakeSandbox();
        var sub = Path.Combine(repo.Dir, "sub");
        Directory.CreateDirectory(sub);

        Assert.Null(SandboxEdits.CheckTarget(repo.Dir, repo.Dir));
        Assert.Null(SandboxEdits.CheckTarget(sub, repo.Dir));
    }

    [Fact]
    public void CheckTarget_accepts_a_symlink_to_the_sandbox()
    {
        // Directory symlinks need extra rights on Windows, so the test is skipped there.
        if (OperatingSystem.IsWindows())
            return;

        using var repo = FakeSandbox();
        var holder = Path.Combine(Path.GetTempPath(), "graftui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(holder);
        var link = Path.Combine(holder, "link");
        try
        {
            Directory.CreateSymbolicLink(link, repo.Dir);

            Assert.Null(SandboxEdits.CheckTarget(link, repo.Dir));
        }
        finally
        {
            // Remove the link itself first, so the recursive delete never follows it into the sandbox.
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(holder, recursive: true);
        }
    }

    [Fact]
    public void CheckTarget_refuses_another_repo_even_with_a_marker()
    {
        using var repo = FakeSandbox();
        using var other = FakeSandbox();

        var refusal = SandboxEdits.CheckTarget(other.Dir, repo.Dir);

        Assert.NotNull(refusal);
        Assert.Contains("only works on", refusal);
    }

    [Fact]
    public void CheckTarget_refuses_a_missing_sandbox()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            var refusal = SandboxEdits.CheckTarget(dir, dir);

            Assert.NotNull(refusal);
            Assert.Contains("graftui --sandbox", refusal);
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void CheckTarget_refuses_a_sandbox_without_the_marker()
    {
        using var repo = TempRepo.Create();

        var refusal = SandboxEdits.CheckTarget(repo.Dir, repo.Dir);

        Assert.NotNull(refusal);
        Assert.Contains("was not created", refusal);
    }

    [Fact]
    public void CheckTarget_refuses_a_sandbox_folder_that_is_inside_another_repo()
    {
        using var repo = FakeSandbox();
        // A folder inside the repo, with a marker-looking .git folder of its own: git still sees the outer repo as the top.
        var sub = Path.Combine(repo.Dir, "sub");
        Directory.CreateDirectory(Path.Combine(sub, ".git"));
        File.WriteAllText(Path.Combine(sub, ".git", Sandbox.MarkerFile), "test\n");

        var refusal = SandboxEdits.CheckTarget(sub, sub);

        Assert.NotNull(refusal);
        Assert.Contains("was not created", refusal);
    }

    [Fact]
    public void CheckTarget_refuses_a_relative_sandbox_root()
    {
        var refusal = SandboxEdits.CheckTarget(".", ".graftui/sandbox");

        Assert.Equal("HOME is not set; cannot find ~/.graftui", refusal);
    }

    [Fact]
    public void CheckTarget_refuses_a_missing_target()
    {
        using var repo = FakeSandbox();
        var missing = Path.Combine(repo.Dir, "nope");

        var refusal = SandboxEdits.CheckTarget(missing, repo.Dir);

        Assert.Equal("--make-edits: no such folder: " + missing, refusal);
    }
}
