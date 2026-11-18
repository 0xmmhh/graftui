using System.Diagnostics;
using System.Text;
using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>
/// The reads the UI cancels when a selection or a snapshot goes stale: a diff, the Changes-tab status and a merge forecast. A hanging
/// script (see <see cref="GitTimeoutTests.HangingScript"/>) stands in for a textconv or fsmonitor that never returns. Each test checks the
/// call returned well inside the budget, so a regression fails instead of hanging.
/// </summary>
public class ReadCancellationTests
{
    // Every call must return within this, however it is stopped.
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(10);

    // How long the hanging git runs before the UI cancels it.
    private static readonly TimeSpan CancelAfter = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_cancelled_diff_stops_a_hanging_textconv_and_its_children()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "one\n", "Add app");
        repo.Write("app.txt", "two\n");

        // A textconv runs for a file whose attributes name the driver. git diff runs it, even with --no-ext-diff.
        File.WriteAllText(Path.Combine(repo.Dir, ".git", "info", "attributes"), "app.txt diff=slow\n", new UTF8Encoding(false));
        using var hang = new GitTimeoutTests.HangingScript(repo, "textconv-hang");
        repo.Run("config", "diff.slow.textconv", "sh '" + hang.ScriptPath + "'");
        var reader = new RepoReader(repo.Git);
        var row = new FileChange("app.txt", null, ChangeSide.Unstaged, ChangeKind.Modified);
        using var cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var call = Task.Run(() => reader.LoadDiff(row, cts.Token));

        var sleepPid = hang.WaitForChildPid(TimeSpan.FromSeconds(5));
        await Task.Delay(CancelAfter);
        cts.Cancel();

        var error = await CancelledWithin(call, "the cancelled diff did not return");
        Assert.True(clock.Elapsed < Quick, "the cancelled diff took too long");
        Assert.NotNull(error);
        Assert.True(GitTimeoutTests.ProcessGone(sleepPid, TimeSpan.FromSeconds(5)), "the textconv's sleep outlived the cancelled diff");
    }

    [Fact]
    public async Task A_cancelled_status_stops_a_hanging_fsmonitor_and_its_children()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");

        // git status asks core.fsmonitor for the changed files. The hook is set after the commit, which also refreshes the index.
        using var hang = new GitTimeoutTests.HangingScript(repo, "fsmonitor-hang");
        repo.Run("config", "core.fsmonitor", "sh '" + hang.ScriptPath + "'");
        var reader = new RepoReader(repo.Git);
        using var cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var call = Task.Run(() => reader.LoadChanges(cts.Token));

        var sleepPid = hang.WaitForChildPid(TimeSpan.FromSeconds(5));
        await Task.Delay(CancelAfter);
        cts.Cancel();

        var error = await CancelledWithin(call, "the cancelled status did not return");
        Assert.True(clock.Elapsed < Quick, "the cancelled status took too long");
        Assert.NotNull(error);
        Assert.True(GitTimeoutTests.ProcessGone(sleepPid, TimeSpan.FromSeconds(5)), "the fsmonitor's sleep outlived the cancelled status");
    }

    [Fact]
    public void A_cancelled_forecast_is_unavailable_and_a_later_forecast_still_answers()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);
        var layout = reader.LoadLayout(snap, snap.Branches.Single(b => b.Name == "feature"), CancellationToken.None);
        var forecaster = new MergeForecaster(repo.Git);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var clock = Stopwatch.StartNew();

        var first = forecaster.Forecast(layout, cancelled.Token);

        Assert.True(clock.Elapsed < Quick, "the cancelled forecast took too long");
        Assert.Equal(ForecastKind.Unavailable, first.Kind);

        // A fresh token: the forecast reads git again and answers (the cancelled call stopped at its first read, before the version read).
        using var fresh = new CancellationTokenSource();
        var second = forecaster.Forecast(layout, fresh.Token);

        Assert.Equal(ForecastKind.Clean, second.Kind);
        Assert.True(forecaster.IsSupported);
    }

    /// <summary>The exception a cancelled call ended with, or null when it returned a value. Fails when the call does not return in time.</summary>
    private static async Task<OperationCanceledException?> CancelledWithin<T>(Task<T> call, string whenLate)
    {
        var finished = await Task.WhenAny(call, Task.Delay(Quick));
        Assert.True(finished == call, whenLate);
        try
        {
            await call;
            return null;
        }
        catch (OperationCanceledException ex)
        {
            return ex;
        }
    }
}
