using graftui.Git;

namespace graftui.Tests;

/// <summary>--sandbox only ever deletes a folder that graftui made.</summary>
public class SandboxSafetyTests
{
    [Fact]
    public void A_folder_graft_did_not_create_is_refused_and_its_files_survive()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "keep.txt"), "mine");

            Assert.Throws<SandboxRefusedException>(() => Sandbox.RebuildAt(dir, TextWriter.Null));

            Assert.Equal("mine", File.ReadAllText(Path.Combine(dir, "keep.txt")));
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void A_git_repo_without_the_marker_is_refused_and_its_git_folder_survives()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Directory.CreateDirectory(dir);
            new GitClient(dir).Run("init", "-q", "-b", "main");

            Assert.Throws<SandboxRefusedException>(() => Sandbox.RebuildAt(dir, TextWriter.Null));

            Assert.True(Directory.Exists(Path.Combine(dir, ".git")));
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void The_public_rebuild_only_accepts_the_default_path()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            var ex = Assert.Throws<SandboxRefusedException>(() => Sandbox.Rebuild(dir, TextWriter.Null));

            Assert.Contains("can only be rebuilt at ~/.graftui/sandbox", ex.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(dir));
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void A_missing_folder_is_built_and_marked()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);

            Assert.True(File.Exists(Path.Combine(dir, ".git", Sandbox.MarkerFile)));
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void An_empty_folder_is_built_and_marked()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Directory.CreateDirectory(dir);

            Sandbox.RebuildAt(dir, TextWriter.Null);

            Assert.True(File.Exists(Path.Combine(dir, ".git", Sandbox.MarkerFile)));
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void Rebuilding_over_its_own_output_succeeds_and_leaves_a_clean_repo()
    {
        var dir = TempRepo.NewSandboxDir();
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);

            Sandbox.RebuildAt(dir, TextWriter.Null);

            Assert.True(File.Exists(Path.Combine(dir, ".git", Sandbox.MarkerFile)));
            Assert.True(new GitClient(dir).IsWorkingTreeClean());
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(dir);
        }
    }

    [Fact]
    public void A_folder_that_cannot_be_emptied_keeps_its_marker_so_the_next_launch_retries()
    {
        // Root ignores directory permissions, so the failure cannot be produced there. Unix modes do not exist on Windows.
        if (Environment.UserName == "root" || OperatingSystem.IsWindows())
            return;

        var dir = TempRepo.NewSandboxDir();
        var locked = Path.Combine(dir, "locked");
        try
        {
            Sandbox.RebuildAt(dir, TextWriter.Null);
            Directory.CreateDirectory(locked);
            File.WriteAllText(Path.Combine(locked, "f"), "x");
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var ex = Record.Exception(() => Sandbox.RebuildAt(dir, TextWriter.Null));
            Assert.True(ex is IOException or UnauthorizedAccessException, $"expected a file-system error, got {ex}");
            Assert.True(File.Exists(Path.Combine(dir, ".git", Sandbox.MarkerFile)));

            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Sandbox.RebuildAt(dir, TextWriter.Null);

            Assert.True(File.Exists(Path.Combine(dir, ".git", Sandbox.MarkerFile)));
            Assert.False(Directory.Exists(locked));
        }
        finally
        {
            if (Directory.Exists(locked))
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            TempRepo.DeleteSandboxRoot(dir);
        }
    }
}
