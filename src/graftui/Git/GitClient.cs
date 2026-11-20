using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Thin wrapper around the git CLI. Every call runs inside <see cref="RepoPath"/>,
/// never in the process's current directory, so graftui only touches the repo you point it at.
/// </summary>
public sealed class GitClient(string repoPath, GitTimeouts? timeouts = null)
{
    // One commit per line: full hash, short hash, parents, committer date (Unix seconds), subject. Fields are separated by 0x1f
    // (unit separator). The subject is last, so a subject cannot shift the other fields.
    private const string CommitFormat = "--format=%H%x1f%h%x1f%P%x1f%ct%x1f%s";

    public string RepoPath { get; } = Path.GetFullPath(repoPath);

    /// <summary>How long each git process may run. Defaults to <see cref="GitTimeouts.Default"/>.</summary>
    public GitTimeouts Timeouts { get; } = timeouts ?? GitTimeouts.Default;

    /// <summary>Runs `git &lt;args&gt;` in RepoPath and captures everything. Stopped after <see cref="GitTimeouts.Write"/>.</summary>
    public GitResult Run(params string[] args) => RunCore(null, args, Timeouts.Write, CancellationToken.None, isWrite: true);

    /// <summary>Like <see cref="Run"/>, with extra environment variables (applied after graftui's own defaults).</summary>
    public GitResult RunWithEnv(IReadOnlyDictionary<string, string> env, params string[] args) =>
        RunCore(env, args, Timeouts.Write, CancellationToken.None, isWrite: true);

    /// <summary>
    /// A read-only call. It is stopped after <see cref="GitTimeouts.Read"/>, or as soon as <paramref name="ct"/> is cancelled: the process
    /// tree is killed and the result has Cancelled = true. A token that is already cancelled starts no process.
    /// </summary>
    public GitResult RunRead(CancellationToken ct, params string[] args) =>
        RunCore(null, args, Timeouts.Read, ct, isWrite: false);

    /// <summary>Like <see cref="RunRead"/>, with literal pathspecs (see <see cref="RunLiteral"/>).</summary>
    public GitResult RunReadLiteral(CancellationToken ct, params string[] args) =>
        RunCore(LiteralPathspecs, args, Timeouts.Read, ct, isWrite: false);

    /// <summary>
    /// Like <see cref="Run"/>, but pathspecs are literal: a file named "*.txt" or ":x" matches only that file,
    /// never a glob or magic pathspec.
    /// </summary>
    public GitResult RunLiteral(params string[] args) => RunWithEnv(LiteralPathspecs, args);

    /// <summary>
    /// A read for the query helpers. A read stopped by cancellation throws OperationCanceledException. A read that timed out is returned
    /// as a failed result, so each helper keeps its own meaning for a failure.
    /// </summary>
    private GitResult Query(CancellationToken ct, params string[] args)
    {
        var result = RunRead(ct, args);
        if (result.Cancelled)
            throw new OperationCanceledException(ct);
        return result;
    }

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

    // Parent environment entries a git child inherits. Everything else is dropped: GIT_DIR, GIT_WORK_TREE,
    // GIT_INDEX_FILE, GIT_ASKPASS, SSH_ASKPASS, EDITOR, VISUAL, GIT_CONFIG_COUNT/KEY_n/VALUE_n and
    // GIT_CONFIG_PARAMETERS must never reach git. GPG_TTY is left out on purpose: it would let a pinentry draw on the
    // TUI's own terminal (signing should use a GUI pinentry or fail, never take over the screen). GIT_CONFIG_GLOBAL and GIT_CONFIG_NOSYSTEM pass through, so the
    // tests' hermetic config (and the user's own global config choice) still applies. The locale (LANG and every LC_*)
    // passes too, so hooks and signing helpers keep the user's character set; UseEnglishMessages then changes only the
    // message language. The caller's env is applied after this, so RunWithIndex (GIT_INDEX_FILE) and
    // GIT_SEQUENCE_EDITOR keep working.
    private static readonly HashSet<string> AllowedEnvNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HOME", "PATH", "USER", "LOGNAME", "SHELL", "TMPDIR", "TERM",
        "SSH_AUTH_SOCK", "SSH_AGENT_PID", "GPG_AGENT_INFO", "GNUPGHOME",
        "DISPLAY", "WAYLAND_DISPLAY",
        "GIT_CONFIG_GLOBAL", "GIT_CONFIG_NOSYSTEM",
        "LANG",
        // Windows counterparts of the above: home, temp, identity, and what every child process needs to start.
        "SYSTEMROOT", "SYSTEMDRIVE", "TEMP", "TMP", "PATHEXT",
        "COMPUTERNAME", "USERNAME", "USERDOMAIN",
        "HOMEDRIVE", "HOMEPATH", "USERPROFILE",
        "APPDATA", "LOCALAPPDATA", "PROGRAMDATA",
    };

    /// <summary>
    /// True when a parent environment entry passes through to a git child: one of the allowed names, an XDG_* variable,
    /// or a locale category (LC_*).
    /// </summary>
    internal static bool IsEnvAllowed(string name)
    {
        if (name.StartsWith("XDG_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("LC_", StringComparison.OrdinalIgnoreCase))
            return true;
        return AllowedEnvNames.Contains(name);
    }

    /// <summary>Rebuilds the child's environment from the allowlisted entries of the parent. Null values are dropped.</summary>
    private static void ApplyEnvAllowlist(IDictionary<string, string?> child, IReadOnlyDictionary<string, string?> parent)
    {
        child.Clear();
        foreach (var pair in parent)
        {
            if (pair.Value is null)
                continue;
            if (IsEnvAllowed(pair.Key))
                child[pair.Key] = pair.Value;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> LiteralPathspecs =
        new Dictionary<string, string> { ["GIT_LITERAL_PATHSPECS"] = "1" };

    private GitResult RunCore(IReadOnlyDictionary<string, string>? env, string[] args, TimeSpan timeout, CancellationToken ct, bool isWrite)
    {
        var command = GitCommandLine.Render(args);

        // A token that is already cancelled starts no process at all.
        if (ct.IsCancellationRequested)
            return Stopped(command, args, Stop.Cancelled, CancelledNote, "", "");

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

        // git starts with a pristine environment: only the allowlisted parent entries pass through, so an inherited
        // GIT_DIR, GIT_WORK_TREE, GIT_INDEX_FILE, GIT_ASKPASS, SSH_ASKPASS, EDITOR, VISUAL, GIT_CONFIG_COUNT and
        // friends can never redirect git to another repo or stall the TUI waiting for input.
        var parent = new Dictionary<string, string?>(psi.Environment, StringComparer.Ordinal);
        ApplyEnvAllowlist(psi.Environment, parent);

        // Keep git from opening an editor or pager and blocking the TUI.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_EDITOR"] = "true";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";

        // Read-only status queries must not take index.lock while a merge is starting.
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        // git's messages in English whatever the user's language, so the text graftui reads (CONFLICT, error:, fatal:) is the same
        // on every machine. Only the message language changes: hooks and signing helpers keep the user's character set (UTF-8).
        UseEnglishMessages(psi.Environment);

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
        var clock = Stopwatch.StartNew();
        var stop = WaitOrStop(process, timeout, ct, clock);
        if (stop == Stop.None)
        {
            // git has exited on its own, so its output should end at once. If a child it started still holds the pipes open,
            // the call is reported as a timeout after only the short grace, not after the rest of the budget.
            if (!WaitForPipes(stdOut, stdErr, KillGrace))
                return Stopped(command, args, Stop.PipeHeld, PipeHeldNote(process.ExitCode), TextOrEmpty(stdOut), TextOrEmpty(stdErr));
            return new GitResult(command, process.ExitCode, TextOrEmpty(stdOut), TextOrEmpty(stdErr)) { Args = args };
        }

        // Stopped by graftui. The kill closes the pipes, so wait briefly for the text that arrived and keep it.
        WaitForPipes(stdOut, stdErr, KillGrace);
        var note = stop == Stop.TimedOut ? TimeoutNote(timeout, env, isWrite) : CancelledNote;
        return Stopped(command, args, stop, note, TextOrEmpty(stdOut), TextOrEmpty(stdErr));
    }

    /// <summary>
    /// Why a call ended early. None means git ran to its own end. PipeHeld means git exited, but a process it started kept the
    /// output pipes open; it is reported as a timeout.
    /// </summary>
    private enum Stop { None, TimedOut, Cancelled, PipeHeld }

    private const string CancelledNote = "graftui stopped git: its answer was no longer needed";

    // How often the wait looks at the cancellation token and the clock. WaitForExit returns at once when git exits, so this only
    // matters for calls that run long.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    // How long to wait for the pipes to close: after a kill, and after git exits on its own.
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Waits for git to exit. Stops it (killing its tree) when ct is cancelled or the clock passes the timeout. Cancellation is looked at
    /// first, so a read that is both cancelled and late says Cancelled.
    /// </summary>
    private static Stop WaitOrStop(Process process, TimeSpan timeout, CancellationToken ct, Stopwatch clock)
    {
        while (!process.WaitForExit(PollInterval))
        {
            if (ct.IsCancellationRequested)
            {
                KillTree(process);
                return Stop.Cancelled;
            }
            if (clock.Elapsed >= timeout)
            {
                KillTree(process);
                return Stop.TimedOut;
            }
        }
        return Stop.None;
    }

    /// <summary>
    /// Kills git and every process it started (sh, sleep, a hook's children), then waits up to <see cref="KillGrace"/> for git to exit.
    /// Kill(true) stops the tree before killing it, so a child cannot start another one in the meantime.
    /// </summary>
    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // git exited on its own just before the kill: nothing left to stop.
        }
        catch (AggregateException)
        {
            // Part of the tree could not be killed; the pipes may stay open, and the caller does not wait for them forever.
        }
        catch (Win32Exception)
        {
            // The process could not be signalled; the caller's bounded wait still returns.
        }
        process.WaitForExit(KillGrace);
    }

    /// <summary>
    /// The result for a call graftui stopped. A cancelled read has exit code 130; every other stop is a timeout (124, TimedOut).
    /// The note goes first in StdErr, where every operation's failure message finds it.
    /// </summary>
    private static GitResult Stopped(string command, string[] args, Stop stop, string note, string stdOut, string stdErr)
    {
        var cancelled = stop == Stop.Cancelled;
        var exitCode = cancelled ? GitResult.CancelledExitCode : GitResult.TimedOutExitCode;
        return new GitResult(command, exitCode, stdOut, note + "\n" + stdErr)
        {
            Args = args,
            TimedOut = !cancelled,
            Cancelled = cancelled,
        };
    }

    private static string PipeHeldNote(int exitCode) =>
        $"git exited {exitCode} but a process it started kept its output open; graftui stopped waiting for it";

    /// <summary>
    /// Waits up to <paramref name="within"/> for both output readers. False when one is still reading, because a process still holds
    /// the pipe. A reader that failed has stopped too, so it counts as done (WaitAll would throw for it).
    /// </summary>
    private static bool WaitForPipes(Task stdOut, Task stdErr, TimeSpan within)
    {
        try
        {
            return Task.WaitAll(new Task[] { stdOut, stdErr }, within);
        }
        catch (AggregateException)
        {
            // A reader failed after both had stopped; TextOrEmpty then leaves its text empty.
            return true;
        }
    }

    private static string TextOrEmpty(Task<string> read) => read.IsCompletedSuccessfully ? read.Result : "";

    /// <summary>
    /// The note for a timed-out call. A write may have left index.lock behind (it is killed while holding it): that file is named with
    /// the command to delete it, but graftui never deletes it. Reads never leave a lock (GIT_OPTIONAL_LOCKS=0), so they are not checked.
    /// </summary>
    private string TimeoutNote(TimeSpan timeout, IReadOnlyDictionary<string, string>? env, bool checkLock)
    {
        var note = $"git timed out after {GitTimeouts.Describe(timeout)} and graftui stopped it";
        if (!checkLock)
            return note;

        var lockPath = IndexLockPath(env);
        if (lockPath is null || !File.Exists(lockPath))
            return note;

        var remove = OperatingSystem.IsWindows() ? "del " : "rm ";
        return note + $"; it left {lockPath} behind: if no other git is running, delete it with {remove}"
            + GitCommandLine.Quote(lockPath, always: true);
    }

    /// <summary>
    /// Where git keeps the index lock for a call: beside GIT_INDEX_FILE when the call uses its own index, otherwise in the git dir.
    /// Null when the git dir cannot be read. The lookup is a read that is not part of any operation's log.
    /// </summary>
    internal string? IndexLockPath(IReadOnlyDictionary<string, string>? env)
    {
        if (env is not null && env.TryGetValue("GIT_INDEX_FILE", out var indexFile))
            return Path.GetFullPath(Path.Combine(RepoPath, indexFile)) + ".lock";

        var gitDir = RunCore(null, ["rev-parse", "--absolute-git-dir"], Timeouts.Read, CancellationToken.None, isWrite: false);
        if (!gitDir.Success)
            return null;
        return Path.Combine(gitDir.StdOut.Trim(), "index.lock");
    }

    /// <summary>
    /// Sets git's message language to English (LC_MESSAGES=C, LANGUAGE=C) and keeps every other part of the user's locale.
    /// LC_ALL would override LC_MESSAGES, so copy its effective value to LANG and the other categories before removing it.
    /// </summary>
    internal static void UseEnglishMessages(IDictionary<string, string?> env)
    {
        if (env.TryGetValue("LC_ALL", out var all) && !string.IsNullOrEmpty(all))
        {
            env["LANG"] = all;
            // Category variables outrank LANG once LC_ALL is removed, including GNU's extra categories.
            foreach (var category in new[]
            {
                "LC_CTYPE", "LC_COLLATE", "LC_MONETARY", "LC_NUMERIC", "LC_TIME",
                "LC_ADDRESS", "LC_IDENTIFICATION", "LC_MEASUREMENT", "LC_NAME", "LC_PAPER", "LC_TELEPHONE",
            })
                env[category] = all;
        }
        env.Remove("LC_ALL");
        env["LC_MESSAGES"] = "C";
        env["LANGUAGE"] = "C";
    }

    /// <summary>True if RepoPath is inside a git work tree.</summary>
    public bool IsRepository(CancellationToken ct = default)
    {
        var result = Query(ct, GitArgs.RevParse, "--is-inside-work-tree");
        return result.Success && result.StdOut.Trim() == "true";
    }

    /// <summary>Name of the checked-out branch (empty when HEAD is detached).</summary>
    public string GetCurrentBranch(CancellationToken ct = default)
    {
        // Full ref, not --short: a same-named tag would make --short print "heads/<name>".
        var result = Query(ct, "symbolic-ref", "-q", "HEAD");
        return result.Success ? StripHeads(result.StdOut.Trim()) : "";
    }

    /// <summary>Full hash of HEAD, or null when there are no commits yet.</summary>
    public string? GetHeadHash(CancellationToken ct = default)
    {
        var result = Query(ct, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        var hash = result.StdOut.Trim();
        return result.Success && hash.Length > 0 ? hash : null;
    }

    /// <summary>All local branches with their full and short commit hash.</summary>
    public IReadOnlyList<Branch> GetBranches(CancellationToken ct = default)
    {
        var current = GetCurrentBranch(ct);
        var result = Query(ct, "for-each-ref", "--format=%(refname)%00%(objectname)%00%(objectname:short)", "refs/heads");
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
    public string? GetMergeBase(string a, string b, CancellationToken ct = default)
    {
        var result = Query(ct, "merge-base", a, b);
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
    public (int Ahead, int Behind)? GetAheadBehind(string headHash, string tipHash, CancellationToken ct = default)
    {
        var result = Query(ct, "rev-list", "--left-right", "--count", headHash + "..." + tipHash);
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
    public IReadOnlyList<CommitInfo> GetFirstParentChain(string tip, string? stopAt, int max, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--no-show-signature", "--first-parent", "-n", max.ToString(), CommitFormat, tip };
        if (stopAt is not null)
            args.AddRange(["--not", stopAt]);
        args.Add("--");

        var result = Query(ct, [.. args]);
        if (!result.Success)
            throw new InvalidOperationException(Describe(result));

        return ParseCommits(result.StdOut);
    }

    /// <summary>
    /// Up to <paramref name="max"/> commits reachable from <paramref name="tip"/> but not from <paramref name="notTip"/>, newest first:
    /// the commits that rev-list counts. Unlike <see cref="GetFirstParentChain"/>, commits of merged side branches are included.
    /// </summary>
    public IReadOnlyList<CommitInfo> GetCommitsNotIn(string tip, string notTip, int max, CancellationToken ct = default)
    {
        var result = Query(ct, "log", "--no-show-signature", "-n", max.ToString(CultureInfo.InvariantCulture), CommitFormat, tip, "--not", notTip, "--");
        if (!result.Success)
            throw new InvalidOperationException(Describe(result));

        return ParseCommits(result.StdOut);
    }

    /// <summary>
    /// The commits for the lane graph, newest first, in topological order (a parent never comes before its child). With
    /// <paramref name="firstParent"/> only the first-parent chain is read, from the one tip given (the base's tip, with
    /// <paramref name="notTip"/> null). Without it, the history reachable from all the tips but not from
    /// <paramref name="notTip"/>: the branches' own commits.
    /// Throws InvalidOperationException when git fails.
    /// </summary>
    public IReadOnlyList<GraphCommit> GetGraphCommits(IReadOnlyList<string> tips, string? notTip, int max, bool firstParent,
        CancellationToken ct = default)
    {
        if (tips.Count == 0)
            return [];

        var args = new List<string>
        {
            "log", "--no-show-signature", firstParent ? "--first-parent" : "--topo-order",
            "-n", max.ToString(CultureInfo.InvariantCulture), CommitFormat,
        };
        args.AddRange(tips);
        if (notTip is not null)
            args.AddRange(["--not", notTip]);
        args.Add("--");

        var result = Query(ct, [.. args]);
        if (!result.Success)
            throw new InvalidOperationException(Describe(result));

        return ParseGraphCommits(result.StdOut);
    }

    /// <summary>One commit by hash, or null if git cannot read it.</summary>
    public CommitInfo? GetCommit(string hash, CancellationToken ct = default)
    {
        var result = Query(ct, "log", "--no-show-signature", "-n", "1", CommitFormat, hash, "--");
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

    // One commit per line for the lane graph. The parents are the space-separated %P field, so a root commit has none.
    private static List<GraphCommit> ParseGraphCommits(string stdout)
    {
        var commits = new List<GraphCommit>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var commit = ParseGraphCommit(line);
            if (commit is not null)
                commits.Add(commit);
        }

        return commits;
    }

    internal static GraphCommit? ParseGraphCommit(string line)
    {
        var fields = line.Split('\x1f', 5);
        if (fields.Length != 5)
            return null;
        var parents = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new GraphCommit(fields[0], fields[1], parents, fields[4]);
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
    public string? GetTopLevel(CancellationToken ct = default)
    {
        var result = Query(ct, GitArgs.RevParse, "--show-toplevel");
        var top = result.StdOut.Trim();
        return result.Success && top.Length > 0 ? top : null;
    }

    /// <summary>True when there are no staged, unstaged or untracked changes.</summary>
    public bool IsWorkingTreeClean(CancellationToken ct = default)
    {
        var result = Query(ct, "status", "--porcelain", "--untracked-files=normal");
        return result.Success && string.IsNullOrWhiteSpace(result.StdOut);
    }

    /// <summary>git's version as major.minor, or null when `git version` fails or its output is not understood.</summary>
    public Version? GetVersion(CancellationToken ct = default)
    {
        var result = Query(ct, "version");
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
    /// and any failure answers false (a cancelled token throws OperationCanceledException).
    /// </summary>
    public bool HasUndoSteps(CancellationToken ct = default)
    {
        var result = Query(ct, "for-each-ref", "--count=1", "--format=%(refname)", "refs/graftui/undo/");
        return result.Success && result.StdOut.Trim().Length > 0;
    }
}

/// <summary>git could not be launched (not installed or not on PATH).</summary>
public sealed class GitNotFoundException(Exception? inner = null)
    : Exception(inner is null ? "could not start git (is it on PATH?)" : $"could not start git (is it on PATH?): {inner.Message}", inner);
