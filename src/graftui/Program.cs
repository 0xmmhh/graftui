using graftui;
using graftui.Git;
using graftui.Model;
using graftui.Ui.App;
using graftui.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// The three early exits print and return at once, so awaiting their writes never moves the UI below onto another thread.
var parsed = CliArgs.Parse(args);
if (parsed.Options is not { } options)
{
    await Console.Error.WriteLineAsync(parsed.Error);
    await Console.Error.WriteLineAsync("Try 'graftui --help'.");
    return 2;
}

if (options.Help)
{
    await Console.Out.WriteAsync(CliArgs.HelpText);
    return 0;
}

if (options.Version)
{
    await Console.Out.WriteLineAsync(CliArgs.VersionLine());
    return 0;
}

// Colours are global and read by every view, so they are set before any view or Application exists.
// The theme was checked by CliArgs, so TryGet finds it; the default is used when no --theme was given.
Themes.TryGet(options.Theme ?? Themes.Default.Name, out var theme);
Palette.Apply(theme);

// Repo to operate on: the given path, the practice sandbox, or the current directory.
// During development, point this at the sandbox (graftui --sandbox), not at graftui itself.
var sandboxMode = options.Sandbox;
if (sandboxMode)
{
    try
    {
        Sandbox.Rebuild(Sandbox.DefaultPath, Console.Out);
    }
    catch (Exception ex) when (ex is SandboxRefusedException or InvalidOperationException or GitNotFoundException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"graftui: {ex.Message}");
        return 1;
    }
}

var git = new GitClient(sandboxMode ? Sandbox.DefaultPath : options.RepoPath ?? Directory.GetCurrentDirectory());

RepoSnapshot snapshot;
try
{
    if (!git.IsRepository())
    {
        Console.Error.WriteLine($"graftui: not a git repository: {git.RepoPath}");
        return 1;
    }

    // Read before the UI starts, so a broken repo fails with a plain message instead of an empty screen.
    snapshot = new RepoReader(git).LoadSnapshot(withCounts: false, CancellationToken.None);
}
catch (Exception ex) when (ex is GitNotFoundException or DirectoryNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine($"graftui: {(ex is InvalidOperationException ? "git error: " : "")}{ex.Message}");
    return 1;
}

var repoDisplay = ShortenHome(git.RepoPath);
AppController? gui = null;
IApplication? applicationHandle = null;
Window? windowHandle = null;
Exception? uiError = null;

try
{
    // Keep the handle before Init() so an Init that throws is still disposed.
    IApplication app = Application.Create();
    applicationHandle = app;
    app.Init();

    // Changes-tab paths are relative to the top-level folder, so that work runs there even when graftui was started in a subfolder.
    var root = git.GetTopLevel() ?? git.RepoPath;
    gui = new AppController(app, git, new GitClient(root), snapshot, repoDisplay);
    var window = gui.BuildWindow();
    windowHandle = window;
    app.Run(window);
}
catch (Exception ex)
{
    uiError = ex;
}
finally
{
    // Stop a merge that has not started yet, then restore the terminal before anything is printed or waited on.
    gui?.MarkQuitting();
    try { windowHandle?.Dispose(); } catch { /* terminal teardown is best effort */ }
    try { applicationHandle?.Dispose(); } catch { /* terminal teardown is best effort */ }
}

// Always runs, after the terminal is restored, so a running merge is waited for and reported.
var exitCode = FinishMerge();
var changeCode = WaitForChanges();

if (uiError is not null)
{
    Console.Error.WriteLine($"graftui: UI error: {uiError}");
    return 1;
}

return exitCode != 0 ? exitCode : changeCode;

int FinishMerge()
{
    var task = gui?.MergeTask;
    if (task is null)
        return 0;

    if (!task.IsCompleted)
    {
        // SIGINT goes to git as well; keep graftui alive so the merge fails and gets rolled back.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        Console.WriteLine("graftui: Ctrl+C interrupts git; graftui will roll back any partial merge");
        Console.WriteLine("graftui: waiting for git merge to finish...");
        try { task.Wait(); } catch (AggregateException) { /* reported below as no outcome */ }
    }

    if (!task.IsCompletedSuccessfully)
        return 1;

    // Null means graftui stopped before any merge started (Esc during the check): nothing to report.
    var o = task.Result;
    if (o is null || gui!.OutcomeShown)
        return 0;

    Console.WriteLine($"graftui: merge {o.Branch} into {o.Into}: {o.Kind} ({o.Message})");
    if (o.ConflictFiles.Count > 0)
        Console.WriteLine("Conflicting files: " + string.Join(", ", o.ConflictFiles));
    foreach (var r in o.Commands)
        Console.WriteLine($"$ {r.Command}  [exit {r.ExitCode}]");
    return o.Kind is MergeKind.Conflict or MergeKind.Failed ? 1 : 0;
}

// A stage, unstage, commit, amend or branch change that is still running is finished before exit. Only a stash-merge-pop that
// conflicted or failed sets an exit code (1, as a failed merge does); the others exit 0.
int WaitForChanges()
{
    var task = gui?.ChangesTask;
    if (task is null)
        return 0;

    if (!task.IsCompleted)
    {
        // As for a merge: Ctrl+C reaches git too, and graftui stays up so the change ends in a known state.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        Console.WriteLine("graftui: waiting for git to finish...");
        try { task.Wait(); } catch (AggregateException) { /* reported below as no line */ }
    }

    // The change draws its outcome only while the screen is up. If graftui quit first, the user has not seen it: say it here.
    if (task.IsCompletedSuccessfully && task.Result is { } line && !gui!.ChangeOutcomeShown)
        Console.WriteLine(line);
    return gui!.ChangeExitCode;
}

string ShortenHome(string path)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/');
    return home.Length > 0 && (path == home || path.StartsWith(home + "/", StringComparison.Ordinal))
        ? "~" + path[home.Length..]
        : path;
}
