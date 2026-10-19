using Graft.Git;
using Graft.Model;
using Graft.Ui.App;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

const string HelpText = """
graft: an animated git TUI

Usage:
  graft [path-to-repo]   open a repository (default: the current directory)
  graft --sandbox        rebuild the practice repo at ~/.graft/sandbox and open it
  graft --help           show this help

Keys: ↑/↓ select · m merge the selected branch into the current one · Esc quit

""";

if (args.Length > 1)
{
    Console.Error.WriteLine("graft: too many arguments");
    Console.Error.WriteLine("Try 'graft --help'.");
    return 2;
}

var argument = args.Length == 1 ? args[0] : null;
if (argument is "--help" or "-h")
{
    Console.Out.Write(HelpText);
    return 0;
}

if (argument is not null && argument.StartsWith('-') && argument != "--sandbox")
{
    Console.Error.WriteLine($"graft: unknown option '{argument}'");
    Console.Error.WriteLine("Try 'graft --help'.");
    return 2;
}

// Repo to operate on: the given path, the practice sandbox, or the current directory.
// During development, point this at the sandbox (scripts/make-sandbox.sh), not at graft itself.
var sandboxMode = argument == "--sandbox";
if (sandboxMode)
{
    try
    {
        Sandbox.Rebuild(Sandbox.DefaultPath, Console.Out);
    }
    catch (Exception ex) when (ex is SandboxRefusedException or InvalidOperationException or GitNotFoundException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"graft: {ex.Message}");
        return 1;
    }
}

var git = new GitClient(sandboxMode ? Sandbox.DefaultPath : argument ?? Directory.GetCurrentDirectory());

RepoSnapshot snapshot;
try
{
    if (!git.IsRepository())
    {
        Console.Error.WriteLine($"graft: not a git repository: {git.RepoPath}");
        return 1;
    }

    // Read before the UI starts, so a broken repo fails with a plain message instead of an empty screen.
    snapshot = new RepoReader(git).LoadSnapshot(withCounts: false, CancellationToken.None);
}
catch (Exception ex) when (ex is GitNotFoundException or DirectoryNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine($"graft: {(ex is InvalidOperationException ? "git error: " : "")}{ex.Message}");
    return 1;
}

var repoDisplay = ShortenHome(git.RepoPath);
GraftApp? gui = null;
IApplication? applicationHandle = null;
Window? windowHandle = null;
Exception? uiError = null;

try
{
    // Keep the handle before Init() so an Init that throws is still disposed.
    IApplication app = Application.Create();
    applicationHandle = app;
    app.Init();

    gui = new GraftApp(app, git, snapshot, repoDisplay, sandboxMode);
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

if (uiError is not null)
{
    Console.Error.WriteLine($"graft: UI error: {uiError}");
    return 1;
}

return exitCode;

int FinishMerge()
{
    var task = gui?.MergeTask;
    if (task is null)
        return 0;

    if (!task.IsCompleted)
    {
        // SIGINT goes to git as well; keep graft alive so the merge fails and gets rolled back.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        Console.WriteLine("graft: Ctrl+C interrupts git; graft will roll back any partial merge");
        Console.WriteLine("graft: waiting for git merge to finish...");
        try { task.Wait(); } catch (AggregateException) { /* reported below as no outcome */ }
    }

    if (!task.IsCompletedSuccessfully)
        return 1;

    // Null means graft stopped before any merge started (Esc during the check): nothing to report.
    var o = task.Result;
    if (o is null || gui!.OutcomeShown)
        return 0;

    Console.WriteLine($"graft: merge {o.Branch} into {o.Into}: {o.Kind} ({o.Message})");
    if (o.ConflictFiles.Count > 0)
        Console.WriteLine("Conflicting files: " + string.Join(", ", o.ConflictFiles));
    foreach (var r in o.Commands)
        Console.WriteLine($"$ {r.Command}  [exit {r.ExitCode}]");
    return o.Kind is MergeKind.Conflict or MergeKind.Failed ? 1 : 0;
}

string ShortenHome(string path)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/');
    return home.Length > 0 && (path == home || path.StartsWith(home + "/", StringComparison.Ordinal))
        ? "~" + path[home.Length..]
        : path;
}
