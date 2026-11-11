using graftui.Model;

namespace graftui.Git;

// Not modelled: the pre-merge-commit hook and merge.verifySignatures. A real merge can run them, and this forecast does not.
// The forecast never runs user commands. A configured merge driver makes it Unavailable, and so does a filter (clean, smudge or
// process) while merge.renormalize is true: merge-tree could run them. A filter alone does not block, since merge-tree runs filters
// only during renormalize.
/// <summary>
/// Read-only forecast of `git merge --no-edit &lt;selected&gt;` into the current branch. It never throws for git's own errors (it answers
/// Unavailable instead), never changes HEAD, refs, the index or the work tree, and is not logged in the Commands pane.
/// The only write is the loose objects that merge-tree leaves behind, which nothing references.
/// </summary>
public sealed class MergeForecaster(GitClient git, Version? knownVersion = null)
{
    /// <summary>The oldest git with `merge-tree --write-tree`.</summary>
    public static readonly Version Minimum = new(2, 38);

    /// <summary>The first git that makes `recursive` an alias of ort. Older git runs the old recursive code, which merge-tree cannot stand for.</summary>
    public static readonly Version RecursiveIsOrt = new(2, 50);

    // What merge.ff asks for: merge commits allowed (the default), never a fast-forward, or fast-forward only.
    private enum FfMode { Default, Never, Only }

    // Every merge driver, as `git config --get-regexp` matches it: merge.<name>.driver. A driver is a shell command.
    private const string MergeDriverKeys = "^merge\\..*\\.driver$";

    // Every filter command, as `git config --get-regexp` matches it: filter.<name>.clean, .smudge or .process. Each is a shell command.
    private const string FilterKeys = "^filter\\..*\\.(clean|smudge|process)$";

    private Version? _version = knownVersion;
    private bool _versionRead = knownVersion is not null;

    /// <summary>
    /// True when git is 2.38 or newer. `git version` is read once, on first use (a version given to the constructor is used as it is).
    /// Called from the worker thread only.
    /// </summary>
    public bool IsSupported => GitVersion is not null && GitVersion >= Minimum;

    /// <summary>The git version, read once (see <see cref="IsSupported"/>). Null when `git version` fails or is not understood.</summary>
    private Version? GitVersion
    {
        get
        {
            if (!_versionRead)
            {
                _version = git.GetVersion();
                _versionRead = true;
            }
            return _version;
        }
    }

    /// <summary>The forecast of merging the selected branch into the current one. Never throws: every failure answers Unavailable.</summary>
    public MergeForecast Forecast(GraphLayout layout)
    {
        var log = new List<GitResult>();
        try
        {
            return Decide(layout, log);
        }
        catch (Exception)
        {
            return MergeForecast.Unavailable(log);
        }
    }

    // The decision, in order. A layout that git cannot be sure about gets Unavailable: the preview then says nothing.
    private MergeForecast Decide(GraphLayout layout, List<GitResult> log)
    {
        // A detached HEAD refuses the merge, and the preview already says so. A Same layout has its own caption. Unrelated histories are
        // refused by git before any merge. A rebase in progress also leaves HEAD detached. None of these asks git anything.
        if (layout.Detached || layout.Kind == GraphKind.Same || layout.Kind == GraphKind.Unrelated)
            return MergeForecast.Unavailable(log);

        // A merge that is already in progress: MergeOperation.Check refuses to start another, so m says nothing here either.
        // Exit 1 means there is no MERGE_HEAD; any other failure leaves the state unknown.
        var merging = git.Run(GitArgs.RevParse, "-q", GitArgs.Verify, GitArgs.MergeHead);
        log.Add(merging);
        if (merging.Success || merging.ExitCode != 1)
            return MergeForecast.Unavailable(log);

        if (!IsSupported)
            return MergeForecast.Unavailable(log);

        // m runs `git merge --no-edit <name>`, and git resolves that name itself: a tag of the same name can win over the branch. The forecast
        // is of the selected tip, so a name that resolves to another commit (or to nothing) is Unavailable.
        if (!NameResolvesToSelectedTip(layout, log))
            return MergeForecast.Unavailable(log);

        var config = ReadConfig(layout.Current, log);
        if (config is null)
            return MergeForecast.Unavailable(log);

        if (layout.Kind == GraphKind.UpToDate)
            return new MergeForecast(ForecastKind.UpToDate, [], null, log);

        if (layout.Kind == GraphKind.FastForward)
            return FastForwardForecast(config, log);

        // Diverged: a merge is needed. With merge.ff=only, git refuses it before it looks at the files.
        if (config.Ff == FfMode.Only)
            return new MergeForecast(ForecastKind.Refused, [], MergeForecast.FfOnlyNote, log);

        return MergeTreeForecast(layout, log);
    }

    // The selected branch's name, resolved the way `git merge` resolves it, must give the selected tip. A name that starts with '-' is never
    // a branch name, and --end-of-options keeps git from reading it as an option anyway. Any failure, or another commit, answers false.
    private bool NameResolvesToSelectedTip(GraphLayout layout, List<GitResult> log)
    {
        if (layout.Selected.StartsWith('-'))
            return false;

        var resolved = git.Run("rev-parse", "--verify", "-q", "--end-of-options", layout.Selected + "^{commit}");
        log.Add(resolved);
        return resolved.Success && resolved.StdOut.Trim() == layout.SelectedTip;
    }

    // A fast-forward is what `git merge` does by default. With merge.ff=false it makes a merge commit instead, which cannot conflict.
    private static MergeForecast FastForwardForecast(MergeConfig config, List<GitResult> log)
    {
        if (config.Ff == FfMode.Never)
            return new MergeForecast(ForecastKind.Clean, [], MergeForecast.FfOffNote, log);
        return new MergeForecast(ForecastKind.FastForward, [], null, log);
    }

    // The diverged case: merge-tree merges the two tips in memory. Exit 0 is a clean merge. Exit 1 with a parsable list is a conflict.
    // Any other answer (unknown ref, other exit code, unreadable output) is Unavailable.
    private MergeForecast MergeTreeForecast(GraphLayout layout, List<GitResult> log)
    {
        // The forecast never runs user commands: merge-tree must not be asked while one could run.
        if (!MergeTreeRunsNoCommands(log))
            return MergeForecast.Unavailable(log);

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
    /// True when merge-tree cannot run a user command. A configured merge driver always can, so any driver key answers false. A filter
    /// (clean, smudge or process) can run only while merge.renormalize is on: merge-tree never checks files out, so smudge does not
    /// run otherwise, and merge.renormalize runs the clean filter. So a filter key answers false only when renormalize is true. A
    /// config read that fails, or a renormalize value git does not know, answers false too.
    /// </summary>
    private bool MergeTreeRunsNoCommands(List<GitResult> log)
    {
        if (!NothingConfigured(MergeDriverKeys, log))
            return false;

        var renormalize = RenormalizeSetting(log);
        if (renormalize is null)
            return false;
        if (!renormalize.Value)
            return true;

        return NothingConfigured(FilterKeys, log);
    }

    // `git config --get-regexp` exits 1 when no key matches the pattern.
    private bool NothingConfigured(string pattern, List<GitResult> log)
    {
        var result = git.Run("config", "--get-regexp", pattern);
        log.Add(result);
        return result.ExitCode == 1;
    }

    // merge.renormalize: exit 1 means "not set" (false). Another failure, or a value that is not a boolean git knows, is unknown (null).
    private bool? RenormalizeSetting(List<GitResult> log)
    {
        var result = git.Run("config", "--get", "merge.renormalize");
        log.Add(result);
        if (result.ExitCode == 1)
            return false;
        if (!result.Success)
            return null;
        return BoolOf(result.StdOut.Trim());
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

        var mode = FfMode.Default;
        if (ff.Success)
        {
            var known = FfModeOf(ff.StdOut.Trim());
            if (known is null)
                return null;
            mode = known.Value;
        }

        // pull.twohead is the strategy `git merge` picks for a diverged merge when none is given. merge-tree always uses ort, so any other
        // strategy makes the forecast unknown. It is read here, before any fast-forward is classified or merge-tree runs.
        var twohead = git.Run("config", "--get", "pull.twohead");
        log.Add(twohead);
        if (!twohead.Success && twohead.ExitCode != 1)
            return null;
        if (twohead.Success && !IsOrtStrategy(twohead.StdOut.Trim(), GitVersion))
            return null;

        // Exit 1 means "not set". A set, non-empty value means git merges with extra flags, so the forecast says nothing.
        var options = git.Run("config", "--get", "branch." + currentBranch + ".mergeOptions");
        log.Add(options);
        if (options.ExitCode == 1)
            return new MergeConfig(mode);
        if (!options.Success || options.StdOut.Trim().Length > 0)
            return null;
        return new MergeConfig(mode);
    }

    /// <summary>
    /// True when the strategy is one merge-tree's ort stands for. ort always is. recursive is an alias of ort only from git 2.50 on:
    /// with an older git, or with no known version, it is not, and the forecast says nothing.
    /// </summary>
    internal static bool IsOrtStrategy(string name, Version? gitVersion)
    {
        if (name == "ort")
            return true;
        if (name == "recursive")
            return gitVersion is not null && gitVersion >= RecursiveIsOrt;
        return false;
    }

    // git's boolean spellings. Anything else is not known (null).
    private static bool? BoolOf(string value)
    {
        var v = value.ToLowerInvariant();
        if (v == "false" || v == "no" || v == "off" || v == "0")
            return false;
        if (v == "true" || v == "yes" || v == "on" || v == "1")
            return true;
        return null;
    }

    // git's boolean spellings for merge.ff, and "only". Anything else is not known.
    private static FfMode? FfModeOf(string value)
    {
        if (value.ToLowerInvariant() == "only")
            return FfMode.Only;
        var on = BoolOf(value);
        if (on is null)
            return null;
        return on.Value ? FfMode.Default : FfMode.Never;
    }

    private sealed record MergeConfig(FfMode Ff);
}
