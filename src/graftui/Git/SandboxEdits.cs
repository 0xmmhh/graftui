using System.Text;

namespace graftui.Git;

/// <summary>Outcome of --make-edits. On success Message is the branch name; on failure it is the reason, shown after "graftui: ".</summary>
internal sealed record MakeEditsResult(bool Success, string Message);

/// <summary>
/// The hidden --make-edits flag: three unstaged changes in the practice repo, so the Changes tab has something to show.
/// It only changes ~/.graftui/sandbox, and only when that folder is clean and was made by graftui --sandbox.
/// </summary>
internal static class SandboxEdits
{
    public const string ModifiedFile = "README.md";
    public const string DeletedFile = "app.txt";
    public const string NewFile = "notes.txt";
    public const string AppendedLine = "Edited by graftui --make-edits.";
    public const string NewFileText = "Notes made by graftui --make-edits.\n";

    // Every line this flag prints starts with the program name; the refusals about the flag itself start with its name.
    private const string Prefix = "graftui: ";
    private const string FlagPrefix = "--make-edits: ";

    // Plain UTF-8 without a byte-order mark, as Sandbox writes its files.
    private static readonly Encoding NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Runs the flag on the real practice repo. Prints what it did, or why it refused, and returns the exit code.</summary>
    public static int RunFromCommandLine(string? path, TextWriter output, TextWriter error) =>
        Run(path, Sandbox.DefaultPath, output, error);

    /// <summary>
    /// The flag's logic, with the sandbox folder passed in so tests can use a temp repo. <paramref name="path"/> is the folder
    /// to change (default: the sandbox itself).
    /// </summary>
    internal static int Run(string? path, string sandboxRoot, TextWriter output, TextWriter error)
    {
        var root = sandboxRoot;
        var target = path ?? root;

        // Inside the try: CheckTarget runs git, and a missing git binary must end in the same one-line error as the rest.
        try
        {
            var refusal = CheckTarget(target, root);
            if (refusal != null)
            {
                error.WriteLine(Prefix + refusal);
                return 1;
            }

            var result = Make(new GitClient(root));
            if (!result.Success)
            {
                error.WriteLine(Prefix + result.Message);
                return 1;
            }

            output.WriteLine(Prefix + $"made 3 unstaged changes in ~/.graftui/sandbox (on {result.Message})");
            output.WriteLine($"  modified   {ModifiedFile}");
            output.WriteLine($"  deleted    {DeletedFile}");
            output.WriteLine($"  untracked  {NewFile}");
            output.WriteLine("Open it with: graftui ~/.graftui/sandbox");
            return 0;
        }
        catch (Exception ex) when (ex is GitNotFoundException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(Prefix + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Null when <paramref name="target"/> may be changed, otherwise the reason it may not. The checks run in order and the
    /// first failure wins. Repo identity comes from git (top-level folder, real paths), so a symlink to the sandbox is
    /// accepted and a look-alike folder is not.
    /// </summary>
    public static string? CheckTarget(string target, string sandboxRoot)
    {
        if (!Path.IsPathRooted(sandboxRoot))
            return "HOME is not set; cannot find ~/.graftui";

        if (!Directory.Exists(sandboxRoot))
            return FlagPrefix + "there is no practice repo at ~/.graftui/sandbox yet; build it first with 'graftui --sandbox'";

        // The sandbox must be a repo of its own, not a folder inside another repo, and must carry the marker that Sandbox writes.
        var sandbox = new GitClient(sandboxRoot);
        var top = sandbox.GetTopLevel();
        if (top == null || !IsOwnRepoRoot(sandbox) || !File.Exists(Path.Combine(sandboxRoot, ".git", Sandbox.MarkerFile)))
            return FlagPrefix + "~/.graftui/sandbox was not created by 'graftui --sandbox'; it was left alone";

        if (!Directory.Exists(target))
            return FlagPrefix + "no such folder: " + target;

        var targetTop = new GitClient(target).GetTopLevel();
        if (targetTop == null || !string.Equals(targetTop, top, StringComparison.Ordinal))
            return "--make-edits only works on the practice repo at ~/.graftui/sandbox, not " + target;

        return null;
    }

    /// <summary>
    /// Makes the three changes in the repo that <paramref name="git"/> points at. Refuses, and changes nothing, when the
    /// work tree has changes or the two files are not tracked.
    /// </summary>
    public static MakeEditsResult Make(GitClient git)
    {
        if (!git.IsWorkingTreeClean())
            return new MakeEditsResult(false, FlagPrefix + "the sandbox already has changes; commit, stash or discard them in graftui, or rebuild it with 'graftui --sandbox'");

        // Literal pathspecs, so the names are matched exactly and not as globs.
        var tracked = git.RunLiteral("ls-files", "--error-unmatch", "--", ModifiedFile, DeletedFile);
        if (!tracked.Success)
            return new MakeEditsResult(false, FlagPrefix + "README.md and app.txt must be tracked on the checked-out branch; rebuild the sandbox with 'graftui --sandbox'");

        var root = git.RepoPath;
        var newPath = Path.Combine(root, NewFile);
        if (File.Exists(newPath) || Directory.Exists(newPath))
            return new MakeEditsResult(false, FlagPrefix + "notes.txt already exists");

        File.AppendAllText(Path.Combine(root, ModifiedFile), AppendedLine + "\n", NoBom);
        File.Delete(Path.Combine(root, DeletedFile));
        File.WriteAllText(newPath, NewFileText, NoBom);

        var branch = git.GetCurrentBranch();
        return new MakeEditsResult(true, branch.Length > 0 ? branch : "detached HEAD");
    }

    /// <summary>True when the top-level folder of the repo is the folder itself, not a folder inside another repo.</summary>
    private static bool IsOwnRepoRoot(GitClient git)
    {
        // --show-prefix is empty at the top of the work tree and names the sub-folder anywhere else.
        var prefix = git.Run("rev-parse", "--show-prefix");
        return prefix.Success && prefix.StdOut.Trim().Length == 0;
    }
}
