using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Thin wrapper around the git CLI. Every call runs inside <see cref="RepoPath"/>,
/// never in the process's current directory, so graftui only touches the repo you point it at.
/// </summary>
public sealed class GitClient(string repoPath)
{
    // One commit per line: full hash, short hash, parents, committer date (Unix seconds), subject. Fields are separated by 0x1f
    // (unit separator). The subject is last, so a subject cannot shift the other fields.
    private const string CommitFormat = "--format=%H%x1f%h%x1f%P%x1f%ct%x1f%s";

    public string RepoPath { get; } = Path.GetFullPath(repoPath);

    /// <summary>Runs `git &lt;args&gt;` in RepoPath and captures everything.</summary>
    public GitResult Run(params string[] args) => RunCore(null, args);

    /// <summary>Like <see cref="Run"/>, with extra environment variables (applied after graftui's own defaults).</summary>
    public GitResult RunWithEnv(IReadOnlyDictionary<string, string> env, params string[] args) => RunCore(env, args);

    /// <summary>
    /// Like <see cref="Run"/>, but pathspecs are literal: a file named "*.txt" or ":x" matches only that file,
    /// never a glob or magic pathspec.
    /// </summary>
    public GitResult RunLiteral(params string[] args) => RunWithEnv(LiteralPathspecs, args);

    /// <summary>
    /// Runs git against a separate index file (GIT_INDEX_FILE) with literal pathspecs. The logged command is prefixed
    /// with GIT_INDEX_FILE=&lt;path relative to RepoPath&gt;, so the command log shows which index was used.
    /// </summary>
    public GitResult RunWithIndex(string indexFile, params string[] args)
    {
        var env = new Dictionary<string, string>
        {
            ["GIT_INDEX_FILE"] = indexFile,
            ["GIT_LITERAL_PATHSPECS"] = "1",
        };
        var result = RunWithEnv(env, args);
        var rel = Path.GetRelativePath(RepoPath, indexFile);
        return result with { Command = $"GIT_INDEX_FILE={rel} " + result.Command };
    }

    private static readonly IReadOnlyDictionary<string, string> LiteralPathspecs =
        new Dictionary<string, string> { ["GIT_LITERAL_PATHSPECS"] = "1" };

    private GitResult RunCore(IReadOnlyDictionary<string, string>? env, string[] args)
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

        // Read-only status queries must not take index.lock while a merge is starting.
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        if (env is not null)
            foreach (var (key, value) in env)
                psi.Environment[key] = value;

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

        return new GitResult(GitCommandLine.Render(args), process.ExitCode, stdOut.Result, stdErr.Result) { Args = args };
    }

    /// <summary>True if RepoPath is inside a git work tree.</summary>
    public bool IsRepository()
    {
        var result = Run(GitArgs.RevParse, "--is-inside-work-tree");
        return result.Success && result.StdOut.Trim() == "true";
    }

    /// <summary>Name of the checked-out branch (empty when HEAD is detached).</summary>
    public string GetCurrentBranch()
    {
        // Full ref, not --short: a same-named tag would make --short print "heads/<name>".
        var result = Run("symbolic-ref", "-q", "HEAD");
        return result.Success ? StripHeads(result.StdOut.Trim()) : "";
    }

    /// <summary>Full hash of HEAD, or null when there are no commits yet.</summary>
    public string? GetHeadHash()
    {
        var result = Run(GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        var hash = result.StdOut.Trim();
        return result.Success && hash.Length > 0 ? hash : null;
    }

    /// <summary>All local branches with their full and short commit hash.</summary>
    public IReadOnlyList<Branch> GetBranches()
    {
        var current = GetCurrentBranch();
        var result = Run("for-each-ref", "--format=%(refname)%00%(objectname)%00%(objectname:short)", "refs/heads");
        if (!result.Success)
            throw new InvalidOperationException(Describe(result));

        // Branch names may contain '|', so fields are NUL-separated.
        return result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\0'))
            .Where(parts => parts.Length == 3)
            .Select(parts =>
            {
                var name = StripHeads(parts[0]);
                return new Branch(name, parts[1], parts[2], name == current);
            })
            .ToList();
    }

    /// <summary>
    /// Best common ancestor of two commits. Null when they share no history (git exits 1).
    /// Any other failure throws.
    /// </summary>
    public string? GetMergeBase(string a, string b)
    {
        var result = Run("merge-base", a, b);
        if (result.Success)
            return result.StdOut.Trim();
        if (result.ExitCode == 1)
            return null;
        throw new InvalidOperationException(Describe(result));
    }

    /// <summary>
    /// Commits on <paramref name="tipHash"/> that are not on <paramref name="headHash"/> (Ahead) and the reverse (Behind).
    /// Null when git fails, so the caller can show the count as unknown.
    /// </summary>
    public (int Ahead, int Behind)? GetAheadBehind(string headHash, string tipHash)
    {
        var result = Run("rev-list", "--left-right", "--count", headHash + "..." + tipHash);
        if (!result.Success)
            return null;

        var parts = result.StdOut.Trim().Split('\t', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var behind) || !int.TryParse(parts[1], out var ahead))
            return null;
        return (ahead, behind);
    }

    /// <summary>
    /// Up to <paramref name="max"/> first-parent commits from <paramref name="tip"/>, newest first.
    /// With <paramref name="stopAt"/>, history reachable from it is excluded.
    /// </summary>
    public IReadOnlyList<CommitInfo> GetFirstParentChain(string tip, string? stopAt, int max)
    {
        var args = new List<string> { "log", "--no-show-signature", "--first-parent", "-n", max.ToString(), CommitFormat, tip };
        if (stopAt is not null)
            args.AddRange(["--not", stopAt]);
        args.Add("--");

        var result = Run([.. args]);
        if (!result.Success)
            throw new InvalidOperationException(Describe(result));

        return ParseCommits(result.StdOut);
    }

    /// <summary>
    /// Up to <paramref name="max"/> commits reachable from <paramref name="tip"/> but not from <paramref name="notTip"/>, newest first:
    /// the commits that rev-list counts. Unlike <see cref="GetFirstParentChain"/>, commits of merged side branches are included.
    /// </summary>
    public IReadOnlyList<CommitInfo> GetCommitsNotIn(string tip, string notTip, int max)
    {
        var result = Run("log", "--no-show-signature", "-n", max.ToString(CultureInfo.InvariantCulture), CommitFormat, tip, "--not", notTip, "--");
        if (!result.Success)
            throw new InvalidOperationException(Describe(result));

        return ParseCommits(result.StdOut);
    }

    /// <summary>One commit by hash, or null if git cannot read it.</summary>
    public CommitInfo? GetCommit(string hash)
    {
        var result = Run("log", "--no-show-signature", "-n", "1", CommitFormat, hash, "--");
        return result.Success ? ParseCommit(result.StdOut.Trim()) : null;
    }

    // One commit per line of git's --format output. Lines that do not have every field are skipped.
    private static List<CommitInfo> ParseCommits(string stdout) =>
        stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseCommit)
            .OfType<CommitInfo>()
            .ToList();

    private static CommitInfo? ParseCommit(string line)
    {
        var fields = line.Split('\x1f', 5);
        if (fields.Length != 5)
            return null;
        var parents = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return new CommitInfo(fields[0], fields[1], parents, fields[4], ParseDate(fields[3]));
    }

    // The committer date from %ct (Unix seconds). Null when the field is not a number.
    internal static DateTimeOffset? ParseDate(string unixSeconds)
    {
        if (!long.TryParse(unixSeconds, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    internal static string StripHeads(string refName) =>
        refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..] : refName;

    /// <summary>Error text for a failed git call: its stderr, or the exit code when stderr is empty.</summary>
    internal static string Describe(GitResult result)
    {
        var detail = result.StdErr.Trim();
        return detail.Length > 0 ? detail : $"{result.Command} exited with {result.ExitCode}";
    }

    /// <summary>Absolute path of the top-level folder of the work tree, or null when there is none.</summary>
    public string? GetTopLevel()
    {
        var result = Run(GitArgs.RevParse, "--show-toplevel");
        var top = result.StdOut.Trim();
        return result.Success && top.Length > 0 ? top : null;
    }

    /// <summary>True when there are no staged, unstaged or untracked changes.</summary>
    public bool IsWorkingTreeClean()
    {
        var result = Run("status", "--porcelain");
        return result.Success && string.IsNullOrWhiteSpace(result.StdOut);
    }

    /// <summary>git's version as major.minor, or null when `git version` fails or its output is not understood.</summary>
    public Version? GetVersion()
    {
        var result = Run("version");
        return result.Success ? ParseVersion(result.StdOut) : null;
    }

    /// <summary>
    /// The major and minor numbers of `git version X.Y.Z...`, or null for anything else. Only the leading digits of the first two parts
    /// count: "2.39.5 (Apple Git-154)" and "2.45.1.windows.1" both give 2.39 and 2.45.
    /// </summary>
    internal static Version? ParseVersion(string output)
    {
        const string prefix = "git version ";
        var text = output.Trim();
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var words = text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        var parts = words[0].Split('.');
        if (parts.Length < 2)
            return null;

        var major = LeadingNumber(parts[0]);
        var minor = LeadingNumber(parts[1]);
        if (major is null || minor is null)
            return null;
        return new Version(major.Value, minor.Value);
    }

    // The number at the start of a text ("39" in "39rc1"), or null when the text does not start with a digit.
    private static int? LeadingNumber(string text)
    {
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            digits++;
        if (digits == 0)
            return null;
        return int.TryParse(text[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>
    /// True when at least one undo step is saved (a ref under refs/graftui/undo/). A read for the key bar: it is not logged,
    /// and any failure answers false.
    /// </summary>
    public bool HasUndoSteps()
    {
        var result = Run("for-each-ref", "--count=1", "--format=%(refname)", "refs/graftui/undo/");
        return result.Success && result.StdOut.Trim().Length > 0;
    }
}

/// <summary>git could not be launched (not installed or not on PATH).</summary>
public sealed class GitNotFoundException(Exception? inner = null)
    : Exception(inner is null ? "could not start git (is it on PATH?)" : $"could not start git (is it on PATH?): {inner.Message}", inner);
