using System.IO;
using System.Text;
using graftui.Git;

namespace graftui.Tests;

/// <summary>
/// A throwaway repository under the system temp folder. Every test that needs git builds one of these,
/// so nothing touches the graftui repo or ~/.graftui/sandbox.
/// </summary>
internal sealed class TempRepo : IDisposable
{
    private TempRepo(string dir)
    {
        Dir = dir;
        Git = new GitClient(dir);
    }

    public GitClient Git { get; }

    public string Dir { get; }

    public string Head => Run("rev-parse", "HEAD");

    public bool IsClean => Run("status", "--porcelain").Length == 0;

    public bool Merging => Git.Run("rev-parse", "-q", "--verify", "MERGE_HEAD").Success;

    public static TempRepo Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "graftui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var repo = new TempRepo(dir);
        repo.Run("init", "-q", "-b", "main");
        repo.Run("config", "user.name", "t");
        repo.Run("config", "user.email", "t@example.invalid");
        repo.Run("config", "commit.gpgsign", "false");
        repo.Run("config", "core.hooksPath", ".git/hooks");
        repo.Run("config", "core.autocrlf", "false");
        repo.Run("config", "merge.ff", "true");
        return repo;
    }

    /// <summary>
    /// main and feature both move on from "Add app". With <paramref name="conflict"/> both change the same line of app.txt;
    /// otherwise feature adds b.txt and main adds c.txt.
    /// </summary>
    public static TempRepo Diverged(bool conflict)
    {
        var repo = Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        if (conflict)
            repo.Commit("app.txt", "feature\n", "Change line 1 on feature");
        else
            repo.Commit("b.txt", "b\n", "Add b on feature");
        repo.Run("switch", "-q", "main");
        if (conflict)
            repo.Commit("app.txt", "main\n", "Change line 1 on main");
        else
            repo.Commit("c.txt", "c\n", "Add c on main");
        return repo;
    }

    /// <summary>feature is one commit ahead of main, so merging it fast-forwards main.</summary>
    public static TempRepo FastForwardable()
    {
        var repo = Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Run("switch", "-q", "main");
        return repo;
    }

    /// <summary>A sandbox path under a fresh temp folder. Only the parent is created; the sandbox itself is left to the caller.</summary>
    public static string NewSandboxDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "graftui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "sandbox");
    }

    /// <summary>Removes the temp folder that <see cref="NewSandboxDir"/> returned a path in. Best effort.</summary>
    public static void DeleteSandboxRoot(string sandboxDir)
    {
        var root = Path.GetDirectoryName(sandboxDir);
        if (root is null || !Path.GetFileName(root).StartsWith("graftui-tests-", StringComparison.Ordinal))
            return;
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Left behind; the system cleans the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
            // Left behind; the system cleans the temp folder.
        }
    }

    /// <summary>Writes a file, stages it and commits it.</summary>
    public void Commit(string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(Dir, file), content, new UTF8Encoding(false));
        Run("add", file);
        Run("commit", "-q", "-m", message);
    }

    /// <summary>Installs an executable hook. core.hooksPath points at .git/hooks, so git runs it.</summary>
    public void WriteHook(string name, string script)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("hook tests need a Unix-like OS");
        var hooks = Path.Combine(Dir, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        var path = Path.Combine(hooks, name);
        File.WriteAllText(path, script, new UTF8Encoding(false));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Runs git and returns trimmed stdout. Throws when git fails, so a broken test setup is loud.</summary>
    public string Run(params string[] args)
    {
        var r = Git.Run(args);
        if (!r.Success)
            throw new InvalidOperationException($"{r.Command} failed ({r.ExitCode}): {r.StdErr.Trim()}");
        return r.StdOut.Trim();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
            // Left behind; the system cleans the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
            // Left behind; the system cleans the temp folder.
        }
    }
}
