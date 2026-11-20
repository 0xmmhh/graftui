using System.Diagnostics;
using System.Globalization;
using System.Text;
using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// The timeouts and cancellation of GitClient. A hanging script stands in for a hook or alias that never returns. It starts a 30 s sleep and
/// writes the sleep's pid to a file, so a test can check that the whole process tree was killed. Every test uses short timeouts and asserts
/// the call returned quickly, so a regression fails instead of hanging. The hook tests need a Unix-like OS, as the other hook tests do.
/// </summary>
public class GitTimeoutTests
{
    // Every call must return within this, however it is stopped. The timeouts under test are a few seconds at most.
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(10);

    private static GitTimeouts Budgets(double readSeconds, double writeSeconds) =>
        new(TimeSpan.FromSeconds(readSeconds), TimeSpan.FromSeconds(writeSeconds));

    /// <summary>The result of a call started on another thread. Fails, rather than hangs, when the call does not return in time.</summary>
    private static async Task<GitResult> ResultWithin(Task<GitResult> call, string whenLate)
    {
        var finished = await Task.WhenAny(call, Task.Delay(Quick));
        Assert.True(finished == call, whenLate);
        return await call;
    }

    private static void AssertReturnedQuickly(Stopwatch clock) =>
        Assert.True(clock.Elapsed < Quick, $"the call took {clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");

    [Fact]
    public void Normal_calls_are_unchanged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        var clock = Stopwatch.StartNew();

        var status = repo.Git.Run("status", "--porcelain");
        var read = repo.Git.RunRead(CancellationToken.None, "rev-parse", "HEAD");
        var written = repo.Git.Run("rev-parse", "HEAD");

        AssertReturnedQuickly(clock);
        Assert.True(status.Success, status.StdErr);
        Assert.False(status.TimedOut);
        Assert.False(status.Cancelled);
        Assert.True(read.Success, read.StdErr);
        Assert.Equal(written.StdOut, read.StdOut);
        Assert.False(read.TimedOut);
        Assert.False(read.Cancelled);
    }

    [Fact]
    public void A_call_that_takes_longer_than_one_poll_still_succeeds()
    {
        using var repo = TempRepo.Create();
        var git = repo.GitWith(Budgets(60, 5));
        var clock = Stopwatch.StartNew();

        var result = git.Run("-c", "alias.slow=!sleep 0.3; echo done", "slow");

        AssertReturnedQuickly(clock);
        Assert.True(result.Success, result.StdErr);
        Assert.Equal("done", result.StdOut.Trim());
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Large_output_does_not_deadlock()
    {
        using var repo = TempRepo.Create();
        var git = repo.GitWith(Budgets(60, 5));
        var clock = Stopwatch.StartNew();

        // Far more than a pipe buffer: the output must be read while git is still writing it.
        var result = git.Run("-c", "alias.big=!head -c 300000 /dev/zero | tr '\\0' x", "big");

        AssertReturnedQuickly(clock);
        Assert.True(result.Success, result.StdErr);
        Assert.Equal(300000, result.StdOut.Length);
    }

    [Fact]
    public async Task A_cancelled_read_kills_git_and_its_children()
    {
        using var repo = TempRepo.Create();
        using var hang = new HangingScript(repo, "hang");
        var git = repo.GitWith(Budgets(60, 60));
        var clock = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        var call = Task.Run(() => git.RunRead(cts.Token, hang.Args));

        var sleepPid = hang.WaitForChildPid(TimeSpan.FromSeconds(5));
        cts.Cancel();

        var result = await ResultWithin(call, "the cancelled read did not return");
        AssertReturnedQuickly(clock);
        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.Equal(GitResult.CancelledExitCode, result.ExitCode);
        Assert.StartsWith("graftui stopped git", result.StdErr, StringComparison.Ordinal);
        Assert.True(ProcessGone(sleepPid, TimeSpan.FromSeconds(5)), "the sleep outlived the cancelled read");
    }

    [Fact]
    public async Task A_read_cancelled_and_disposed_like_the_ui_does_still_stops()
    {
        using var repo = TempRepo.Create();
        using var hang = new HangingScript(repo, "hang");
        var git = repo.GitWith(Budgets(60, 60));
        var clock = Stopwatch.StartNew();
        var cts = new CancellationTokenSource();
        var call = Task.Run(() => git.RunRead(cts.Token, hang.Args));

        var sleepPid = hang.WaitForChildPid(TimeSpan.FromSeconds(5));
        // The UI cancels and disposes its source straight away, while the read is still polling its token.
        cts.Cancel();
        cts.Dispose();

        var result = await ResultWithin(call, "the read did not return after its source was disposed");
        AssertReturnedQuickly(clock);
        Assert.True(result.Cancelled);
        Assert.Equal(GitResult.CancelledExitCode, result.ExitCode);
        Assert.True(ProcessGone(sleepPid, TimeSpan.FromSeconds(5)), "the sleep outlived the disposed read");
    }

    [Fact]
    public void An_already_cancelled_token_starts_no_process()
    {
        using var repo = TempRepo.Create();
        var marker = Path.Combine(repo.Dir, "started.marker");
        var clock = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = repo.Git.RunRead(cts.Token, "-c", $"alias.mark=!touch '{marker}'", "mark");

        AssertReturnedQuickly(clock);
        Assert.True(result.Cancelled);
        Assert.Equal(GitResult.CancelledExitCode, result.ExitCode);
        Assert.False(File.Exists(marker), "git ran although the token was already cancelled");
    }

    [Fact]
    public async Task A_read_times_out()
    {
        using var repo = TempRepo.Create();
        using var hang = new HangingScript(repo, "hang");
        var git = repo.GitWith(Budgets(1, 10));
        var clock = Stopwatch.StartNew();
        var call = Task.Run(() => git.RunRead(CancellationToken.None, hang.Args));

        var sleepPid = hang.TryWaitForChildPid(call, TimeSpan.FromSeconds(5));
        var result = await call;

        AssertReturnedQuickly(clock);
        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Equal(GitResult.TimedOutExitCode, result.ExitCode);
        Assert.StartsWith("git timed out after 1 s", result.StdErr, StringComparison.Ordinal);
        Assert.True(sleepPid.HasValue, "the script did not record its sleep pid before graftui stopped it");
        Assert.True(ProcessGone(sleepPid.Value, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out read");
    }

    [Fact]
    public async Task A_write_times_out_and_its_tree_is_killed()
    {
        using var repo = TempRepo.Create();
        using var hang = new HangingScript(repo, "hang");
        var git = repo.GitWith(Budgets(10, 1));
        var clock = Stopwatch.StartNew();
        var call = Task.Run(() => git.Run(hang.Args));

        var sleepPid = hang.TryWaitForChildPid(call, TimeSpan.FromSeconds(5));
        var result = await call;

        AssertReturnedQuickly(clock);
        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Equal(GitResult.TimedOutExitCode, result.ExitCode);
        Assert.StartsWith("git timed out after 1 s", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("index.lock", result.StdErr, StringComparison.Ordinal);
        Assert.True(sleepPid.HasValue, "the script did not record its sleep pid before graftui stopped it");
        Assert.True(ProcessGone(sleepPid.Value, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out write");
    }

    [Fact]
    public async Task A_stale_index_lock_is_named_and_kept()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Write("a.txt", "a2\n");
        var oldHead = repo.Head;
        using var hang = new HangingScript(repo, "pre-commit-hang");
        repo.WriteHook("pre-commit", hang.Body);
        var git = repo.GitWith(Budgets(10, 1));
        var lockPath = Path.Combine(repo.Dir, ".git", "index.lock");
        var clock = Stopwatch.StartNew();

        // commit -a updates the index, and holds index.lock, before its pre-commit hook runs.
        var call = Task.Run(() => git.Run("commit", "-a", "-m", "x"));
        var sleepPid = hang.TryWaitForChildPid(call, TimeSpan.FromSeconds(5));
        var result = await call;

        AssertReturnedQuickly(clock);
        Assert.True(result.TimedOut);
        Assert.Equal(GitResult.TimedOutExitCode, result.ExitCode);
        var firstLine = result.StdErr.Split('\n')[0];
        Assert.StartsWith("git timed out after 1 s", firstLine, StringComparison.Ordinal);
        Assert.Contains("index.lock", firstLine, StringComparison.Ordinal);
        Assert.Contains("rm \"", firstLine, StringComparison.Ordinal);
        Assert.Equal(oldHead, repo.Head);
        Assert.True(File.Exists(lockPath), "graftui deleted index.lock");
        Assert.True(sleepPid.HasValue, "the script did not record its sleep pid before graftui stopped it");
        Assert.True(ProcessGone(sleepPid.Value, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out commit");
    }

    [Fact]
    public void RunWithIndex_names_its_own_lock()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        var altIndex = Path.Combine(repo.Dir, ".git", "alt-index");
        var git = repo.GitWith(Budgets(10, 1));
        Assert.True(git.RunWithIndex(altIndex, "read-tree", "HEAD").Success);
        File.WriteAllText(altIndex + ".lock", "");
        using var hang = new HangingScript(repo, "alt-hang");

        var result = git.RunWithIndex(altIndex, hang.Args);

        Assert.True(result.TimedOut);
        var firstLine = result.StdErr.Split('\n')[0];
        Assert.Contains("alt-index.lock", firstLine, StringComparison.Ordinal);
        Assert.DoesNotContain("/index.lock", firstLine, StringComparison.Ordinal);
        Assert.True(File.Exists(altIndex + ".lock"), "graftui deleted the index lock");
        var sleepPid = hang.WaitForChildPid(TimeSpan.FromSeconds(5));
        Assert.True(ProcessGone(sleepPid, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out call");
    }

    [Fact]
    public void A_child_that_keeps_the_output_open_is_a_timeout_after_a_short_grace()
    {
        using var repo = TempRepo.Create();

        // The alias exits 0 at once, but the sleep it started inherits the output pipe and keeps it open.
        using var orphan = new HangingScript(repo, "orphan", waitForSleep: false);
        var git = repo.GitWith(Budgets(60, 30));
        var clock = Stopwatch.StartNew();

        var result = git.Run(orphan.Args);

        // The budget is 30 s. Returning in well under that shows the wait after git's exit is the short grace.
        AssertReturnedQuickly(clock);
        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Equal(GitResult.TimedOutExitCode, result.ExitCode);
        Assert.StartsWith("git exited 0 but a process it started kept its output open", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timed_out_commit_fails_and_changes_nothing()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var oldHead = repo.Head;
        repo.Write("app.txt", "line 1 (edited)\n");
        repo.Run("add", "-A");
        using var hang = new HangingScript(repo, "commit-hang");
        repo.WriteHook("pre-commit", hang.Body);
        var cached = repo.Run("diff", "--cached", "--name-only");
        var worktree = File.ReadAllBytes(Path.Combine(repo.Dir, "app.txt"));
        var op = new CommitOperation(repo.GitWith(Budgets(1, 1)));
        var request = new CommitRequest(false, "Will time out", "", null, false);
        var clock = Stopwatch.StartNew();

        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var call = Task.Run(() => op.Run(request, check));
        var sleepPid = hang.TryWaitForChildPid(call, TimeSpan.FromSeconds(5));
        var outcome = await call;

        AssertReturnedQuickly(clock);
        Assert.Equal(CommitKind.Failed, outcome.Kind);
        Assert.StartsWith("git timed out after 1 s", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was committed", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(oldHead, repo.Head);
        Assert.Equal(cached, repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal(worktree, File.ReadAllBytes(Path.Combine(repo.Dir, "app.txt")));
        Assert.Empty(repo.TempFiles(GitDirTemp.Message));
        Assert.True(sleepPid.HasValue, "the script did not record its sleep pid before graftui stopped it");
        Assert.True(ProcessGone(sleepPid.Value, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out commit");
    }

    [Fact]
    public async Task A_timed_out_merge_is_rolled_back()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var oldHead = repo.Head;
        using var hang = new HangingScript(repo, "merge-hang");
        repo.WriteHook("pre-merge-commit", hang.Body);
        var op = new MergeOperation(repo.GitWith(Budgets(1, 1)));
        var request = new MergeRequest("feature");
        var clock = Stopwatch.StartNew();

        var check = op.Check(request);
        Assert.True(check.CanMerge, check.Refusal);
        var call = Task.Run(() => op.Run(request, check));
        var sleepPid = hang.TryWaitForChildPid(call, TimeSpan.FromSeconds(5));
        var outcome = await call;

        AssertReturnedQuickly(clock);
        Assert.Equal(MergeKind.Failed, outcome.Kind);
        Assert.Contains(outcome.Commands, c => c.Command == "git merge --no-edit feature" && c.TimedOut && c.ExitCode == 124);
        Assert.Contains(outcome.Commands, c => c.Command == "git reset --merge");
        Assert.StartsWith("git timed out", outcome.Message, StringComparison.Ordinal);
        Assert.EndsWith("nothing was merged", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(oldHead, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(repo.Merging);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "b.txt")));
        Assert.True(sleepPid.HasValue, "the script did not record its sleep pid before graftui stopped it");
        Assert.True(ProcessGone(sleepPid.Value, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out merge");
    }

    [Fact]
    public async Task A_merge_killed_while_holding_the_lock_names_the_lock_and_the_steps()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var oldHead = repo.Head;

        // A merge driver for app.txt that hangs. The merge is in its tree update while it waits: it holds index.lock and has no MERGE_HEAD yet.
        using var hang = new HangingScript(repo, "driver-hang");
        File.WriteAllText(Path.Combine(repo.Dir, ".git", "info", "attributes"), "app.txt merge=hang\n", new UTF8Encoding(false));
        repo.Run("config", "merge.hang.driver", "sh '" + hang.ScriptPath + "'");
        var lockPath = Path.Combine(repo.Dir, ".git", "index.lock");
        var op = new MergeOperation(repo.GitWith(Budgets(1, 1)));
        var request = new MergeRequest("feature");
        var clock = Stopwatch.StartNew();

        var check = op.Check(request);
        Assert.True(check.CanMerge, check.Refusal);
        var call = Task.Run(() => op.Run(request, check));
        var sleepPid = hang.TryWaitForChildPid(call, TimeSpan.FromSeconds(5));
        var outcome = await call;

        AssertReturnedQuickly(clock);
        Assert.Equal(MergeKind.Failed, outcome.Kind);
        Assert.StartsWith("git timed out after 1 s", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("index.lock", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("rm \"", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("working tree left dirty by an interrupted merge", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("then git reset --merge", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("check git status for leftover files", outcome.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(lockPath), "graftui deleted index.lock");
        Assert.Equal(oldHead, repo.Head);
        Assert.True(sleepPid.HasValue, "the script did not record its sleep pid before graftui stopped it");
        Assert.True(ProcessGone(sleepPid.Value, TimeSpan.FromSeconds(5)), "the sleep outlived the timed-out merge");
    }

    [Theory]
    [InlineData(600_000, "10 min")]
    [InlineData(60_000, "1 min")]
    [InlineData(90_000, "90 s")]
    [InlineData(1_000, "1 s")]
    [InlineData(300, "300 ms")]
    public void Timeouts_are_described_in_whole_units(int milliseconds, string expected)
    {
        Assert.Equal(expected, GitTimeouts.Describe(TimeSpan.FromMilliseconds(milliseconds)));
    }

    /// <summary>
    /// A script that starts a 30 s sleep in the background, as a hook or alias that hangs does. Its pid is written to a file in the git dir.
    /// With <paramref name="waitForSleep"/> the script waits for the sleep, so it hangs; without it the script exits at once and leaves the sleep
    /// behind. Dispose kills the sleep if the test has not, so nothing outlives the test run.
    /// </summary>
    internal sealed class HangingScript : IDisposable
    {
        private readonly string _pidFile;

        public HangingScript(TempRepo repo, string name, bool waitForSleep = true)
        {
            _pidFile = Path.Combine(repo.Dir, ".git", name + ".pid");
            ScriptPath = Path.Combine(repo.Dir, ".git", name + ".sh");
            var ending = waitForSleep ? "wait\n" : "exit 0\n";
            Body = "#!/bin/sh\nsleep 30 &\necho $! > '" + _pidFile + "'\n" + ending;
            File.WriteAllText(ScriptPath, Body, new UTF8Encoding(false));

            // Git runs an alias that starts with ! in a shell, as it runs a hook.
            Args = new[] { "-c", "alias.hang=!sh '" + ScriptPath + "'", "hang" };
        }

        /// <summary>The script text, for use as a hook.</summary>
        public string Body { get; }

        /// <summary>Where the script was written, for a command that runs it (a merge driver, for one).</summary>
        public string ScriptPath { get; }

        /// <summary>The arguments that run the script through an alias named hang.</summary>
        public string[] Args { get; }

        /// <summary>Waits until the script has started its sleep, and returns the sleep's pid. Throws when that does not happen in time.</summary>
        public int WaitForChildPid(TimeSpan within)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < within)
            {
                var pid = ReadPid();
                if (pid.HasValue)
                    return pid.Value;
                Thread.Sleep(20);
            }
            throw new TimeoutException("the script did not start its sleep in time");
        }

        /// <summary>
        /// Waits for the sleep's pid while a call the test started runs. Null when the call ended, or the time ran out, before the script
        /// wrote the pid. The script starts its sleep before it writes the pid, so tests treat null as a failure: they cannot then prove the sleep was killed.
        /// </summary>
        public int? TryWaitForChildPid(Task call, TimeSpan within)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < within)
            {
                var pid = ReadPid();
                if (pid.HasValue)
                    return pid;
                if (call.IsCompleted)
                    return ReadPid();
                Thread.Sleep(20);
            }
            return null;
        }

        public void Dispose()
        {
            var pid = ReadPid();
            if (pid.HasValue && IsSleep(pid.Value))
                KillSleep(pid.Value);
        }

        private int? ReadPid()
        {
            if (!File.Exists(_pidFile))
                return null;
            var text = File.ReadAllText(_pidFile).Trim();
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                return pid;
            return null;
        }
    }

    /// <summary>True when the pid is a sleep that is still running (a zombie counts until it is reaped). The name guards against a reused pid.</summary>
    private static bool IsSleep(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.ProcessName == "sleep";
        }
        catch (ArgumentException)
        {
            // No such process.
            return false;
        }
    }

    private static void KillSleep(int pid)
    {
        try
        {
            Process.GetProcessById(pid).Kill();
        }
        catch (ArgumentException)
        {
            // Gone already.
        }
        catch (InvalidOperationException)
        {
            // Gone already.
        }
    }

    /// <summary>Polls until the sleep with this pid has stopped, or the time runs out.</summary>
    internal static bool ProcessGone(int pid, TimeSpan within)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < within)
        {
            if (!IsSleep(pid))
                return true;
            Thread.Sleep(50);
        }
        return !IsSleep(pid);
    }
}
