using System.Collections.ObjectModel;
using System.Text;
using Graft.Git;
using Graft.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// Repo to operate on: first argument, or the current directory.
// During development, point this at the sandbox (scripts/make-sandbox.sh), not at graft itself.
var git = new GitClient(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());

var merger = new Merger(git);
IReadOnlyList<Branch> branches;
try
{
    if (!git.IsRepository())
    {
        Console.Error.WriteLine($"graft: not a git repository: {git.RepoPath}");
        return 1;
    }

    branches = git.GetBranches();
}
catch (GitNotFoundException ex)
{
    Console.Error.WriteLine($"graft: {ex.Message}");
    return 1;
}

IApplication app = Application.Create().Init();

// Branch names, file names and git output may contain '_' (the title shows the repo path);
// don't treat it as a hotkey marker.
var noHotKey = (Rune)'\xffff';

Window window = new() { Title = $"graft — {git.RepoPath} (Esc to quit)", HotKeySpecifier = noHotKey };

var status = new Label { X = 0, Y = 0, Width = Dim.Fill(), HotKeySpecifier = noHotKey };
var hint = new Label
{
    Text = "↑/↓ select · m merge selected branch into current · Esc quit",
    X = 0,
    Y = 1,
    Width = Dim.Fill(),
    HotKeySpecifier = noHotKey,
};

var rows = new ObservableCollection<string>();
var list = new ListView
{
    X = 0,
    Y = 2,
    Width = Dim.Fill(),
    Height = 5,
    HotKeySpecifier = noHotKey,
};
list.SetSource(rows);

var art = new View
{
    X = 0,
    Y = 7,
    Width = Dim.Fill(),
    Height = 3,
    Text = "",
    HotKeySpecifier = noHotKey,
};

// TextView is obsolete in 2.5.0, so the command log is a non-focusable ListView that follows its last row.
var logLines = new ObservableCollection<string>();
var logView = new ListView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    CanFocus = false,
    HotKeySpecifier = noHotKey,
};
logView.SetSource(logLines);
var cmdFrame = new FrameView
{
    Title = "Commands",
    X = 0,
    Y = 10,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    HotKeySpecifier = noHotKey,
};
cmdFrame.Add(logView);

void Refresh()
{
    branches = git.GetBranches();
    var current = git.GetCurrentBranch();
    var clean = git.IsWorkingTreeClean();

    var previous = list.SelectedItem ?? 0;
    var nameWidth = branches.Count == 0 ? 0 : branches.Max(b => b.Name.Length);
    rows.Clear();
    foreach (var b in branches)
        rows.Add($"{(b.IsCurrent ? "*" : " ")} {b.Name.PadRight(nameWidth)}  {b.ShortHash}");
    if (rows.Count > 0)
        list.SelectedItem = Math.Clamp(previous, 0, rows.Count - 1);

    status.Text = current.Length == 0
        ? "Detached HEAD. Merging is disabled"
        : clean
            ? $"On {current} · Working tree clean"
            : $"On {current} · Uncommitted changes. Merging is disabled";
    status.SetNeedsDraw();
    list.SetNeedsDraw();
}

void Log(string line)
{
    logLines.Add(line);
    logView.SelectedItem = logLines.Count - 1;
    logView.SetNeedsDraw();
}

// Logs each command and returns the log index of the first `git merge` line (or -1).
int LogCommands(IEnumerable<GitResult> results)
{
    var mergeIndex = -1;
    foreach (var r in results)
    {
        if (mergeIndex < 0 && r.Command.StartsWith("git merge", StringComparison.Ordinal))
            mergeIndex = logLines.Count;
        Log($"$ {r.Command}  [exit {r.ExitCode}]");
        if (!r.Command.StartsWith("git merge", StringComparison.Ordinal))
            continue;
        foreach (var line in (r.StdOut + "\n" + r.StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Log("    " + line);
    }
    return mergeIndex;
}

void SetArt(string text)
{
    art.Text = text;
    art.SetNeedsDraw();
}

var busy = false;
MergeOutcome? pending = null;
Task<MergeOutcome>? mergeTask = null;
var outcomeShown = false; // true once the outcome was drawn in the UI

void StartMerge()
{
    if (busy || list.SelectedItem is not int i || i < 0 || i >= branches.Count)
        return;

    var b = branches[i];
    var check = merger.Check(b.Name);
    LogCommands(check.Commands);
    if (!check.CanMerge)
    {
        SetArt(MergeArt.Refused(check.Refusal!));
        Log("Refused: " + check.Refusal);
        return;
    }

    busy = true;
    pending = null;
    var step = 0;
    var into = check.CurrentBranch;
    var name = b.Name;
    SetArt(MergeArt.Frame(0, into, name));

    outcomeShown = false;
    var started = DateTime.UtcNow;
    var warned = false;

    mergeTask = Task.Run(() =>
    {
        var o = merger.Merge(name, into);
        try { app.Invoke(() => pending = o); }
        catch (Exception) { /* UI already gone (Esc mid-merge); the outcome is returned instead */ }
        return o;
    });

    app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
    {
        if (step < MergeArt.Steps)
        {
            step++;
            SetArt(MergeArt.Frame(step, into, name));
            return true;
        }

        if (pending is null)
        {
            // Hold the pre-join frame while git runs; flag a merge that seems stuck.
            if (!warned && DateTime.UtcNow - started > TimeSpan.FromSeconds(5))
            {
                warned = true;
                SetArt(MergeArt.Frame(MergeArt.Steps, into, name,
                    "git is still running (waiting for a password prompt or hook?)"));
            }
            return true;
        }

        SetArt(MergeArt.Final(pending));
        var mergeIndex = LogCommands(pending.Commands);
        if (pending.ConflictFiles.Count > 0)
            Log("Conflicting files: " + string.Join(", ", pending.ConflictFiles));
        // The log pane is short: scroll so the `git merge` line is the top row when it all fits.
        if (mergeIndex >= 0)
        {
            var viewport = logView.Viewport;
            var maxTop = Math.Max(0, logLines.Count - viewport.Height);
            logView.Viewport = viewport with { Y = Math.Min(mergeIndex, maxTop) };
        }
        Refresh();
        outcomeShown = true;
        busy = false;
        pending = null;
        return false;
    });
}

// ListView swallows letter keys for type-ahead search, so listen at application level.
app.Keyboard.KeyDown += (_, e) =>
{
    if (e == Key.M)
    {
        e.Handled = true;
        StartMerge();
    }
};

Refresh();
if (rows.Count > 0)
    list.SelectedItem = 0;

window.Add(status, hint, list, art, cmdFrame);
list.SetFocus();
try
{
    app.Run(window);
}
finally
{
    // Restore the terminal before anything is printed or waited on.
    window.Dispose();
    app.Dispose();
}

if (mergeTask is null)
    return 0;

if (!mergeTask.IsCompleted)
{
    Console.WriteLine("graft: waiting for git merge to finish...");
    mergeTask.Wait();
}

if (!outcomeShown && mergeTask.IsCompletedSuccessfully)
{
    var o = mergeTask.Result;
    Console.WriteLine($"graft: merge {o.Branch} into {o.Into}: {o.Kind} ({o.Message})");
    if (o.ConflictFiles.Count > 0)
        Console.WriteLine("Conflicting files: " + string.Join(", ", o.ConflictFiles));
    foreach (var r in o.Commands)
        Console.WriteLine($"$ {r.Command}  [exit {r.ExitCode}]");
}

return 0;
