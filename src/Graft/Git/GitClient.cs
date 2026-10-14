using System.ComponentModel;
using System.Diagnostics;

namespace Graft.Git;

/// <summary>
/// Thin wrapper around the git CLI. Every call runs inside <see cref="RepoPath"/>,
/// never in the process's current directory, so graft only touches the repo you point it at.
/// </summary>
public sealed class GitClient(string repoPath)
{
    public string RepoPath { get; } = Path.GetFullPath(repoPath);

    /// <summary>Runs `git &lt;args&gt;` in RepoPath and captures everything.</summary>
    public GitResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Don't let an inherited environment redirect git to a different repo.
        psi.Environment.Remove("GIT_DIR");
        psi.Environment.Remove("GIT_WORK_TREE");
        psi.Environment.Remove("GIT_INDEX_FILE");

        // Keep git from opening an editor or pager and blocking the TUI.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_EDITOR"] = "true";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";

        if (!Directory.Exists(RepoPath))
            throw new DirectoryNotFoundException($"repository folder not found: {RepoPath}");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new GitNotFoundException();
        }
        catch (Win32Exception ex)
        {
            throw new GitNotFoundException(ex);
        }

        using var _ = process;

        // Git (or hooks, signing helpers) must never read the TUI's raw stdin.
        process.StandardInput.Close();

        // Read both streams concurrently so a full pipe can't deadlock.
        var stdOut = process.StandardOutput.ReadToEndAsync();
        var stdErr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return new GitResult("git " + string.Join(' ', args), process.ExitCode, stdOut.Result, stdErr.Result);
    }

    /// <summary>True if RepoPath is inside a git work tree.</summary>
    public bool IsRepository()
    {
        var result = Run("rev-parse", "--is-inside-work-tree");
        return result.Success && result.StdOut.Trim() == "true";
    }

    /// <summary>Name of the checked-out branch (empty when HEAD is detached).</summary>
    public string GetCurrentBranch()
    {
        // Full ref, not --short: a same-named tag would make --short print "heads/<name>".
        var result = Run("symbolic-ref", "-q", "HEAD");
        return result.Success ? StripHeads(result.StdOut.Trim()) : "";
    }

    /// <summary>All local branches with their short commit hash.</summary>
    public IReadOnlyList<Branch> GetBranches()
    {
        var current = GetCurrentBranch();
        var result = Run("for-each-ref", "--format=%(refname)%00%(objectname:short)", "refs/heads");
        if (!result.Success)
        {
            var detail = result.StdErr.Trim();
            throw new InvalidOperationException(detail.Length > 0 ? detail : $"git for-each-ref exited with {result.ExitCode}");
        }

        // Branch names may contain '|', so fields are NUL-separated.
        return result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\0'))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Branch(StripHeads(parts[0]), parts[1], StripHeads(parts[0]) == current))
            .ToList();
    }

    internal static string StripHeads(string refName) =>
        refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..] : refName;

    /// <summary>True when there are no staged, unstaged or untracked changes.</summary>
    public bool IsWorkingTreeClean()
    {
        var result = Run("status", "--porcelain");
        return result.Success && string.IsNullOrWhiteSpace(result.StdOut);
    }
}

/// <summary>git could not be launched (not installed or not on PATH).</summary>
public sealed class GitNotFoundException(Exception? inner = null)
    : Exception(inner is null ? "could not start git (is it on PATH?)" : $"could not start git (is it on PATH?): {inner.Message}", inner);
