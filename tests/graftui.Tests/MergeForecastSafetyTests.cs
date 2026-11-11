using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// The merge forecast against real temp repos. Every test also checks that the forecast left the repo as it found it: HEAD, the refs,
/// the index file, the status, and no merge in progress.
/// </summary>
public class MergeForecastSafetyTests
{
    private const string MergeTree = "merge-tree";

    [Fact]
    public void A_clean_diverged_merge_is_forecast_clean_with_no_files()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Clean, f.Kind);
        Assert.Null(f.Note);
        Assert.Empty(f.Files);
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_content_conflict_is_forecast_with_the_file_as_content()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("app.txt", ForecastConflict.Content));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_file_added_on_both_sides_is_forecast_as_added_on_both()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("both.txt", "feature\n", "Add both on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("both.txt", "main\n", "Add both on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("both.txt", ForecastConflict.BothAdded));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_file_deleted_on_the_current_branch_and_changed_on_the_selected_is_deleted_current()
    {
        using var repo = TempRepo.Create();
        repo.Commit("del.txt", "one\n", "Add del");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("del.txt", "changed on feature\n", "Change del on feature");
        repo.Run("switch", "-q", "main");
        repo.Run("rm", "-q", "del.txt");
        repo.Run("commit", "-q", "-m", "Delete del on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("del.txt", ForecastConflict.DeletedCurrent));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_file_deleted_on_the_selected_branch_and_changed_on_the_current_is_deleted_selected()
    {
        using var repo = TempRepo.Create();
        repo.Commit("del.txt", "one\n", "Add del");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("rm", "-q", "del.txt");
        repo.Run("commit", "-q", "-m", "Delete del on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("del.txt", "changed on main\n", "Change del on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("del.txt", ForecastConflict.DeletedSelected));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_binary_file_changed_on_both_sides_is_forecast_as_binary()
    {
        using var repo = TempRepo.Create();
        CommitBytes(repo, "bin.dat", [0, 1, 2, 3], "Add bin");
        repo.Run("switch", "-q", "-c", "feature");
        CommitBytes(repo, "bin.dat", [0, 9, 9], "Change bin on feature");
        repo.Run("switch", "-q", "main");
        CommitBytes(repo, "bin.dat", [0, 7, 7], "Change bin on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("bin.dat", ForecastConflict.Binary));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_rename_on_one_side_and_an_edit_on_the_other_merges_cleanly()
    {
        using var repo = TempRepo.Create();
        repo.Commit("ren.txt", Lines(20), "Add ren");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("mv", "ren.txt", "moved.txt");
        repo.Run("commit", "-q", "-m", "Rename ren on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("ren.txt", Lines(20) + "extra\n", "Edit ren on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Clean, f.Kind);
        Assert.Empty(f.Files);
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_rename_on_both_sides_to_different_names_is_forecast_as_renamed()
    {
        using var repo = TempRepo.Create();
        repo.Commit("ren.txt", Lines(20), "Add ren");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("mv", "ren.txt", "other-name.txt");
        repo.Run("commit", "-q", "-m", "Rename on feature");
        repo.Run("switch", "-q", "main");
        repo.Run("mv", "ren.txt", "main-name.txt");
        repo.Run("commit", "-q", "-m", "Rename on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        Assert.NotEmpty(f.Files);
        Assert.All(f.Files, file => Assert.Equal(ForecastConflict.Rename, file.Kind));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_branch_already_in_the_current_one_is_up_to_date_and_merge_tree_is_not_asked()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Run("branch", "feature");
        repo.Commit("b.txt", "b\n", "Add b on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.UpToDate, f.Kind);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_fast_forward_is_forecast_as_a_fast_forward()
    {
        using var repo = TempRepo.FastForwardable();
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.FastForward, f.Kind);
        AssertUntouched(repo, before);
    }

    [Fact]
    public void With_merge_ff_false_a_fast_forward_is_forecast_as_a_merge_commit()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("config", "merge.ff", "false");

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Clean, f.Kind);
        Assert.Equal(MergeForecast.FfOffNote, f.Note);
    }

    [Fact]
    public void With_merge_ff_only_a_diverged_merge_is_refused_without_merge_tree()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("config", "merge.ff", "only");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Refused, f.Kind);
        Assert.Equal(MergeForecast.FfOnlyNote, f.Note);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void Branches_with_no_common_history_get_no_forecast_and_no_git_call()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Run("switch", "-q", "--orphan", "other");
        repo.Commit("b.txt", "b\n", "Add b on an orphan");
        repo.Run("switch", "-q", "main");

        var f = ForecastOf(repo, "other");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Commands);
    }

    [Fact]
    public void Extra_merge_options_in_the_branch_config_make_the_forecast_unavailable()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "branch.main.mergeOptions", "--no-ff");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Files);
        AssertUntouched(repo, before);
    }

    [Fact]
    public void Git_older_than_2_38_gives_no_forecast_and_never_runs_merge_tree()
    {
        using var repo = TempRepo.Diverged(conflict: true);

        var f = ForecastOf(repo, "feature", new Version(2, 37));

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
    }

    [Fact]
    public void Git_2_38_is_new_enough_to_forecast_a_conflict()
    {
        using var repo = TempRepo.Diverged(conflict: true);

        var f = ForecastOf(repo, "feature", new Version(2, 38));

        Assert.Equal(ForecastKind.Conflict, f.Kind);
    }

    [Fact]
    public void A_dirty_work_tree_is_forecast_as_usual_and_left_as_it_was()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Write("app.txt", "edited, not committed\n");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("app.txt", ForecastConflict.Content));
        Assert.Equal(before.Status, repo.Status());
        Assert.Equal(before.Head, repo.Head);
        Assert.Equal(before.Index, File.ReadAllBytes(IndexPath(repo)));
    }

    [Fact]
    public void A_merge_already_in_progress_gets_no_forecast_and_is_left_as_it_was()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var started = repo.Git.Run("merge", "--no-edit", "feature");
        Assert.False(started.Success);
        Assert.True(repo.Merging);
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Files);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        Assert.Equal(before.Head, repo.Head);
        Assert.Equal(before.Status, repo.Status());
        Assert.Equal(before.Index, File.ReadAllBytes(IndexPath(repo)));
        Assert.True(repo.Merging);
    }

    [Fact]
    public void A_rename_rename_conflict_lists_the_files_git_lists_as_unmerged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("ren.txt", Lines(20), "Add ren");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("mv", "ren.txt", "other-name.txt");
        repo.Run("commit", "-q", "-m", "Rename on feature");
        repo.Run("switch", "-q", "main");
        repo.Run("mv", "ren.txt", "main-name.txt");
        repo.Run("commit", "-q", "-m", "Rename on main");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");
        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertUntouched(repo, before);

        var merge = repo.Git.Run("merge", "--no-edit", "feature");
        Assert.False(merge.Success);
        var unmerged = repo.Git.Run("diff", "--name-only", "--diff-filter=U").StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        repo.Git.Run("merge", "--abort");

        Assert.Equal(unmerged.ToList(), f.Files.Select(file => file.Path).ToList());
    }

    [Fact]
    public void The_clean_forecast_matches_the_merge_that_follows()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var f = ForecastOf(repo, "feature");

        var outcome = MergeNow(repo, "feature");

        Assert.Equal(ForecastKind.Clean, f.Kind);
        Assert.Equal(MergeKind.MergeCommit, outcome.Kind);
    }

    [Fact]
    public void The_conflict_forecast_matches_the_merge_that_follows()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var f = ForecastOf(repo, "feature");
        var before = repo.Head;

        var outcome = MergeNow(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        Assert.Equal(MergeKind.Conflict, outcome.Kind);
        Assert.Equal(f.Files.Select(file => file.Path).ToList(), outcome.ConflictFiles.ToList());
        Assert.Equal(before, repo.Head);
    }

    [Fact]
    public void The_fast_forward_forecast_matches_the_merge_that_follows()
    {
        using var repo = TempRepo.FastForwardable();
        var f = ForecastOf(repo, "feature");

        var outcome = MergeNow(repo, "feature");

        Assert.Equal(ForecastKind.FastForward, f.Kind);
        Assert.Equal(MergeKind.FastForward, outcome.Kind);
    }

    // A merge driver is a shell command. merge-tree runs one for a file that .gitattributes assigns to it, so the forecast must not ask.
    [Fact]
    public void A_configured_merge_driver_is_never_run_and_the_forecast_is_unavailable()
    {
        using var repo = TempRepo.Create();
        repo.Commit(".gitattributes", "app.txt merge=touchy\n", "Assign app.txt to the driver");
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("app.txt", "feature\n", "Change line 1 on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("app.txt", "main\n", "Change line 1 on main");
        var marker = Path.Combine(repo.Dir, "driver-ran.marker");
        repo.Run("config", "merge.touchy.driver", "touch '" + marker + "'");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Files);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        Assert.False(File.Exists(marker), "the merge driver ran");
        AssertUntouched(repo, before);
    }

    [Theory]
    [InlineData("resolve")]
    [InlineData("octopus")]
    public void A_pull_twohead_other_than_ort_makes_a_diverged_forecast_unavailable(string strategy)
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "pull.twohead", strategy);
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Files);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_pull_twohead_other_than_ort_is_unavailable_before_a_fast_forward_is_classified()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("config", "pull.twohead", "resolve");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        AssertUntouched(repo, before);
    }

    [Fact]
    public void A_pull_twohead_of_ort_keeps_the_conflict_forecast()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "pull.twohead", "ort");

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("app.txt", ForecastConflict.Content));
    }

    // recursive is an alias of ort only from git 2.50. The version is injected, so both sides are tested whatever git is installed.
    [Fact]
    public void A_pull_twohead_of_recursive_keeps_the_conflict_forecast_on_git_2_50()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "pull.twohead", "recursive");

        var f = ForecastOf(repo, "feature", new Version(2, 50));

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("app.txt", ForecastConflict.Content));
    }

    [Fact]
    public void A_pull_twohead_of_recursive_is_unavailable_before_git_2_50_and_merge_tree_is_not_asked()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "pull.twohead", "recursive");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature", new Version(2, 49));

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Files);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        AssertUntouched(repo, before);
    }

    // A clean filter selected by .gitattributes does not run without merge.renormalize: merge-tree only runs it during renormalize.
    // So the forecast goes ahead, and the marker stays absent.
    [Fact]
    public void A_clean_filter_without_renormalize_forecasts_normally_and_never_runs()
    {
        using var repo = FilteredDiverged();
        var marker = Path.Combine(repo.Dir, "filter-ran.marker");
        repo.Run("config", "filter.touchy.clean", "touch '" + marker + "' && cat");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("app.txt", ForecastConflict.Content));
        Assert.False(File.Exists(marker), "the clean filter ran");
        AssertUntouched(repo, before);
    }

    // git-lfs installs filter.lfs.* globally. With no attribute selecting it and renormalize off, nothing can run it: no block.
    [Fact]
    public void A_git_lfs_style_filter_alone_does_not_block_the_forecast()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "filter.lfs.clean", "git-lfs clean -- %f");
        repo.Run("config", "filter.lfs.smudge", "git-lfs smudge -- %f");
        repo.Run("config", "filter.lfs.process", "git-lfs filter-process");

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
        AssertFiles(f, ("app.txt", ForecastConflict.Content));
    }

    // With renormalize on, merge-tree runs any clean filter, so any filter key makes the forecast unknown.
    [Theory]
    [InlineData("clean")]
    [InlineData("smudge")]
    [InlineData("process")]
    public void With_renormalize_on_any_filter_key_makes_the_forecast_unavailable(string key)
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "filter.touchy." + key, "true");
        repo.Run("config", "merge.renormalize", "true");

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
    }

    // renormalize with no filter has no command to run, so the forecast goes ahead.
    [Fact]
    public void Merge_renormalize_true_without_a_filter_keeps_the_conflict_forecast()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "merge.renormalize", "true");

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
    }

    // merge.renormalize runs the clean filter on every file. On this pair merge-tree does run the filter when renormalize is on (the
    // marker shows it), so this is the case that proves the forecast does not ask merge-tree.
    [Fact]
    public void Merge_renormalize_true_makes_the_forecast_unavailable_and_the_clean_filter_is_not_run()
    {
        using var repo = FilteredDiverged();
        var marker = Path.Combine(repo.Dir, "filter-ran.marker");
        repo.Run("config", "filter.touchy.clean", "touch '" + marker + "' && cat");
        repo.Run("config", "merge.renormalize", "true");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        Assert.False(File.Exists(marker), "the clean filter ran");
        AssertUntouched(repo, before);
    }

    [Fact]
    public void Merge_renormalize_false_keeps_the_conflict_forecast()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("config", "merge.renormalize", "false");

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Conflict, f.Kind);
    }

    // m merges by name, and git resolves a tag of the same name before the branch: `git merge feature` merged the tag's commit. The
    // forecast is of the branch's tip, so it must say nothing here.
    [Fact]
    public void A_tag_named_like_the_selected_branch_makes_the_forecast_unavailable()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Run("tag", "feature", "main~1");
        var before = Capture(repo);

        var f = ForecastOf(repo, "feature");

        Assert.Equal(ForecastKind.Unavailable, f.Kind);
        Assert.Empty(f.Files);
        Assert.DoesNotContain(f.Commands, c => c.Args.Contains(MergeTree));
        AssertUntouched(repo, before);
    }

    // ---------- helpers ----------

    private static GraphLayout LayoutOf(TempRepo repo, string selected)
    {
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);
        var branch = snap.Branches.Single(b => b.Name == selected);
        return reader.LoadLayout(snap, branch, CancellationToken.None);
    }

    private static MergeForecast ForecastOf(TempRepo repo, string selected, Version? version = null) =>
        new MergeForecaster(repo.Git, version).Forecast(LayoutOf(repo, selected));

    private static MergeOutcome MergeNow(TempRepo repo, string branch)
    {
        var op = new MergeOperation(repo.Git);
        var check = op.Check(new MergeRequest(branch));
        Assert.True(check.CanMerge, check.Refusal);
        return op.Run(new MergeRequest(branch), check);
    }

    private static void AssertFiles(MergeForecast f, params (string Path, ForecastConflict Kind)[] expected)
    {
        var actual = f.Files.Select(file => (file.Path, file.Kind)).ToList();
        Assert.Equal(expected.ToList(), actual);
    }

    private static string Lines(int count) => string.Concat(Enumerable.Range(1, count).Select(i => $"line {i}\n"));

    private static void CommitBytes(TempRepo repo, string file, byte[] bytes, string message)
    {
        File.WriteAllBytes(Path.Combine(repo.Dir, file), bytes);
        repo.Run("add", file);
        repo.Run("commit", "-q", "-m", message);
    }

    // main and feature both change app.txt, which .gitattributes gives the filter `touchy`. The filter's command is set by the caller.
    private static TempRepo FilteredDiverged()
    {
        var repo = TempRepo.Create();
        repo.Commit(".gitattributes", "app.txt filter=touchy\n", "Assign app.txt to the filter");
        CommitBackdated(repo, "app.txt", "line 1\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        CommitBackdated(repo, "app.txt", "feature\n", "Change line 1 on feature");
        repo.Run("switch", "-q", "main");
        CommitBackdated(repo, "app.txt", "main\n", "Change line 1 on main");
        return repo;
    }

    // Writes and commits a file with a modification time two minutes back. A file written in the same timestamp tick as the index is
    // "racy": git status then re-reads it, and with a clean filter selected that runs the filter. The filter test must not depend on that.
    private static void CommitBackdated(TempRepo repo, string file, string content, string message)
    {
        var path = Path.Combine(repo.Dir, file);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        repo.Run("add", file);
        repo.Run("commit", "-q", "-m", message);
    }

    private static string IndexPath(TempRepo repo) => Path.Combine(repo.Dir, ".git", "index");

    /// <summary>What the forecast must not change: HEAD, the refs, the status, the index file's bytes.</summary>
    private sealed record RepoFacts(string Head, string Status, string Refs, byte[] Index);

    private static RepoFacts Capture(TempRepo repo) =>
        new(repo.Head, repo.Status(), repo.Run("for-each-ref"), File.ReadAllBytes(IndexPath(repo)));

    private static void AssertUntouched(TempRepo repo, RepoFacts before)
    {
        Assert.Equal(before.Head, repo.Head);
        Assert.Equal(before.Refs, repo.Run("for-each-ref"));
        Assert.Equal(before.Status, repo.Status());
        Assert.Equal(before.Index, File.ReadAllBytes(IndexPath(repo)));
        Assert.False(repo.Merging);
    }
}
