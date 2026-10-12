using System.Collections.ObjectModel;
using Graft.Git;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// Repo to operate on: first argument, or the current directory.
// During development, point this at the sandbox (scripts/make-sandbox.sh), not at graft itself.
var git = new GitClient(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());

if (!git.IsRepository())
{
    Console.Error.WriteLine($"graft: not a git repository: {git.RepoPath}");
    return 1;
}

var branches = git.GetBranches();
var isClean = git.IsWorkingTreeClean();

using IApplication app = Application.Create().Init();

using Window window = new() { Title = $"graft — {git.RepoPath} (Esc to quit)" };

var status = new Label
{
    Text = isClean ? "Working tree clean" : "Uncommitted changes. Merging is disabled",
    X = 0,
    Y = 0,
};

var nameWidth = branches.Count == 0 ? 0 : branches.Max(b => b.Name.Length);
var rows = new ObservableCollection<string>(
    branches.Select(b => $"{(b.IsCurrent ? "*" : " ")} {b.Name.PadRight(nameWidth)}  {b.ShortHash}"));

var list = new ListView
{
    X = 0,
    Y = Pos.Bottom(status) + 1,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
};
list.SetSource(rows);

window.Add(status, list);
app.Run(window);
return 0;
