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

        // Keep git from opening an editor or pager and blocking the TUI.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_EDITOR"] = "true";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start git. Is it installed and on PATH?");

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
        if (!Directory.Exists(RepoPath))
            return false;

        var result = Run("rev-parse", "--is-inside-work-tree");
        return result.Success && result.StdOut.Trim() == "true";
    }

    /// <summary>Name of the checked-out branch (empty when HEAD is detached).</summary>
    public string GetCurrentBranch() => Run("branch", "--show-current").StdOut.Trim();

    /// <summary>All local branches with their short commit hash.</summary>
    public IReadOnlyList<Branch> GetBranches()
    {
        var current = GetCurrentBranch();
        var result = Run("for-each-ref", "--format=%(refname:short)|%(objectname:short)", "refs/heads");

        return result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Branch(parts[0], parts[1], parts[0] == current))
            .ToList();
    }

    /// <summary>True when there are no staged, unstaged or untracked changes.</summary>
    public bool IsWorkingTreeClean()
    {
        var result = Run("status", "--porcelain");
        return result.Success && string.IsNullOrWhiteSpace(result.StdOut);
    }
}
