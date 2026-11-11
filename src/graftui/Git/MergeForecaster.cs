using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Read-only forecast of `git merge --no-edit &lt;selected&gt;` into the current branch. It never throws for git's own errors (it answers
/// Unavailable instead), never changes HEAD, refs, the index or the work tree, and is not logged in the Commands pane.
/// The only write is the loose objects that merge-tree leaves behind, which nothing references.
/// </summary>
public sealed class MergeForecaster(GitClient git, Version? knownVersion = null)
{
    /// <summary>The oldest git with `merge-tree --write-tree`.</summary>
    public static readonly Version Minimum = new(2, 38);

    // What merge.ff asks for: merge commits allowed (the default), never a fast-forward, or fast-forward only.
    private const string FfDefault = "default";
    private const string FfNever = "never";
    private const string FfOnly = "only";

    private Version? _version = knownVersion;
    private bool _versionRead = knownVersion is not null;

    /// <summary>
    /// True when git is 2.38 or newer. `git version` is read once, on first use (a version given to the constructor is used as it is).
    /// Called from the worker thread only.
    /// </summary>
    public bool IsSupported
    {
        get
        {
            if (!_versionRead)
            {
                _version = git.GetVersion();
                _versionRead = true;
            }
            return _version is not null && _version >= Minimum;
        }
    }

    /// <summary>The forecast of merging the selected branch into the current one. Cancelling it throws; every other failure answers Unavailable.</summary>
    public MergeForecast Forecast(GraphLayout layout, CancellationToken ct)
    {
        var log = new List<GitResult>();
        try
        {
            return Decide(layout, ct, log);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return MergeForecast.Unavailable(log);
        }
    }

    // The decision, in order. A layout that git cannot be sure about gets Unavailable: the preview then says nothing.
    private MergeForecast Decide(GraphLayout layout, CancellationToken ct, List<GitResult> log)
    {
        // A detached HEAD refuses the merge, and the preview already says so. A Same layout has its own caption.
        if (layout.Detached || layout.Kind == GraphKind.Same)
            return MergeForecast.Unavailable(log);

        // git refuses unrelated histories without merging. merge-tree says the same (exit 128), so no git call is needed here.
        if (layout.Kind == GraphKind.Unrelated)
            return new MergeForecast(ForecastKind.Refused, [], MergeForecast.UnrelatedNote, log);

        if (!IsSupported)
            return MergeForecast.Unavailable(log);

        var config = ReadConfig(layout.Current, log);
        if (config is null)
            return MergeForecast.Unavailable(log);

        if (layout.Kind == GraphKind.UpToDate)
            return new MergeForecast(ForecastKind.UpToDate, [], null, log);

        if (layout.Kind == GraphKind.FastForward)
            return FastForwardForecast(config, log);

        // Diverged: a merge is needed. With merge.ff=only, git refuses it before it looks at the files.
        if (config.Ff == FfOnly)
            return new MergeForecast(ForecastKind.Refused, [], MergeForecast.FfOnlyNote, log);

        ct.ThrowIfCancellationRequested();
        return MergeTreeForecast(layout, log);
    }

    // A fast-forward is what `git merge` does by default. With merge.ff=false it makes a merge commit instead, which cannot conflict.
    private static MergeForecast FastForwardForecast(MergeConfig config, List<GitResult> log)
    {
        if (config.Ff == FfNever)
            return new MergeForecast(ForecastKind.Clean, [], MergeForecast.FfOffNote, log);
        return new MergeForecast(ForecastKind.FastForward, [], null, log);
    }

    // The diverged case: merge-tree merges the two tips in memory. Exit 0 is a clean merge. Exit 1 with a parsable list is a conflict.
    // Any other answer (unknown ref, other exit code, unreadable output) is Unavailable.
    private MergeForecast MergeTreeForecast(GraphLayout layout, List<GitResult> log)
    {
        // The tips are passed as full hashes, so no branch name can be read as a tag or as an option.
        var result = git.Run("merge-tree", "--write-tree", "-z", layout.CurrentTip, layout.SelectedTip);
        log.Add(result);

        if (result.ExitCode == 0 && MergeTreeParser.HasTree(result.StdOut))
            return new MergeForecast(ForecastKind.Clean, [], null, log);

        if (result.ExitCode == 1)
        {
            var files = MergeTreeParser.ParseConflicts(result.StdOut);
            if (files is not null)
                return new MergeForecast(ForecastKind.Conflict, files, null, log);
        }
        return MergeForecast.Unavailable(log);
    }

    /// <summary>
    /// The two config values that change what `git merge --no-edit` does: merge.ff, and branch.&lt;current&gt;.mergeOptions (extra merge
    /// flags, which the forecast cannot know). Null when either cannot be read, has a value the forecast does not know, or sets options.
    /// </summary>
    private MergeConfig? ReadConfig(string currentBranch, List<GitResult> log)
    {
        // Exit 1 means "not set", which is git's default. Any other failure, or a value not known here, makes the forecast unknown.
        var ff = git.Run("config", "--get", "merge.ff");
        log.Add(ff);
        if (!ff.Success && ff.ExitCode != 1)
            return null;

        var mode = FfDefault;
        if (ff.Success)
        {
            var known = FfModeOf(ff.StdOut.Trim());
            if (known is null)
                return null;
            mode = known;
        }

        // Exit 1 means "not set". A set, non-empty value means git merges with extra flags, so the forecast says nothing.
        var options = git.Run("config", "--get", "branch." + currentBranch + ".mergeOptions");
        log.Add(options);
        if (options.ExitCode == 1)
            return new MergeConfig(mode);
        if (!options.Success || options.StdOut.Trim().Length > 0)
            return null;
        return new MergeConfig(mode);
    }

    // git's boolean spellings for merge.ff, and "only". Anything else is not known.
    private static string? FfModeOf(string value)
    {
        var v = value.ToLowerInvariant();
        if (v == "false" || v == "no" || v == "off" || v == "0")
            return FfNever;
        if (v == "only")
            return FfOnly;
        if (v == "true" || v == "yes" || v == "on" || v == "1")
            return FfDefault;
        return null;
    }

    private sealed record MergeConfig(string Ff);
}
