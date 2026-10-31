using System.Text;

namespace graftui.Git;

/// <summary>
/// Builds the practice repo at ~/.graftui/sandbox: one branch per merge outcome plus feature/picks.
/// It only ever deletes a directory that graftui itself created (marked by .git/graftui-sandbox).
/// </summary>
public static class Sandbox
{
    public const string MarkerFile = "graftui-sandbox";

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Plain UTF-8 without a byte-order mark, so the files match what git and the shell scripts write.
    private static readonly Encoding NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string DefaultPath => Path.Combine(Home, ".graftui", "sandbox");

    /// <summary>
    /// Deletes and recreates <paramref name="path"/>, which must be <see cref="DefaultPath"/>.
    /// Throws <see cref="SandboxRefusedException"/> when the path exists and was not made by graftui,
    /// and <see cref="InvalidOperationException"/> when a git step fails.
    /// </summary>
    public static void Rebuild(string path, TextWriter progress)
    {
        if (string.IsNullOrEmpty(Home))
            throw new SandboxRefusedException("HOME is not set; cannot find ~/.graftui");

        if (Path.GetFullPath(path) != Path.GetFullPath(DefaultPath))
            throw new SandboxRefusedException("the sandbox can only be rebuilt at ~/.graftui/sandbox");

        RebuildAt(path, progress);
    }

    /// <summary>Test-only: builds at any path, still behind the marker-file guard. Never called from Program.</summary>
    internal static void RebuildAt(string path, TextWriter progress)
    {
        var full = Path.GetFullPath(path);
        CheckRefusals(full);

        if (Directory.Exists(full))
            DeleteOwnTree(full);

        Directory.CreateDirectory(full);
        progress.WriteLine($"graftui: rebuilding practice repo at {full}");
        try
        {
            Build(full);
        }
        catch
        {
            // Before the marker is written the folder is not recognisable as ours, so it would block every later run.
            // It was created by this run, so it is removed. A marked folder is kept: the next launch rebuilds it.
            if (!File.Exists(Path.Combine(full, ".git", MarkerFile)))
                TryDeleteTree(full);
            throw;
        }
    }

    /// <summary>
    /// Deletes a sandbox graftui made. The marker goes last of all: top-level entries, then everything in .git except
    /// the marker, then the marker, then the folder. If any step throws, the marker is still there, so the next launch
    /// recognises the folder and retries instead of refusing it.
    /// </summary>
    private static void DeleteOwnTree(string full)
    {
        foreach (var entry in Directory.GetFileSystemEntries(full).Where(IsNotDotGit))
            DeleteEntry(entry);

        var gitDir = Path.Combine(full, ".git");
        if (Directory.Exists(gitDir))
        {
            foreach (var entry in Directory.GetFileSystemEntries(gitDir).Where(IsNotMarker))
                DeleteEntry(entry);

            File.Delete(Path.Combine(gitDir, MarkerFile));
        }

        Directory.Delete(full, recursive: true);
    }

    /// <summary>True for every top-level entry except the .git folder, which goes last.</summary>
    private static bool IsNotDotGit(string path)
    {
        return Path.GetFileName(path) != ".git";
    }

    /// <summary>True for every entry in .git except the marker, which is deleted after the rest.</summary>
    private static bool IsNotMarker(string path)
    {
        return Path.GetFileName(path) != MarkerFile;
    }

    /// <summary>Deletes a file, a link, or a folder. A link is removed itself and never followed.</summary>
    private static void DeleteEntry(string p)
    {
        if (Directory.Exists(p) && !IsLink(p))
            Directory.Delete(p, recursive: true);
        else
            File.Delete(p);
    }

    private static void CheckRefusals(string sandbox)
    {
        var graftuiDir = Path.GetDirectoryName(sandbox)!;
        if (Exists(graftuiDir) && (IsLink(graftuiDir) || File.Exists(graftuiDir)))
            throw new SandboxRefusedException($"{Display(graftuiDir)} exists but is not a plain folder; move it away or delete it yourself");

        if (!Exists(sandbox))
            return;

        // An empty folder is what a failed build leaves behind when git never wrote anything. It holds nothing to lose.
        if (!IsLink(sandbox) && Directory.Exists(sandbox) && !Directory.EnumerateFileSystemEntries(sandbox).Any())
            return;

        var marker = Path.Combine(sandbox, ".git", MarkerFile);
        var refused = IsLink(sandbox) || File.Exists(sandbox)
            || IsLink(Path.Combine(sandbox, ".git"))
            || !IsRegularFile(marker);
        if (refused)
            throw new SandboxRefusedException($"{Display(sandbox)} exists but was not created by graftui; move it away or delete it yourself");
    }

    private static void Build(string path)
    {
        var env = new Dictionary<string, string>();
        var commitNo = 0;

        GitResult Git(params string[] args)
        {
            var r = new GitClient(path).RunWithEnv(env, args);
            if (!r.Success)
                throw new InvalidOperationException($"sandbox: {r.Command} failed: {r.StdErr.Trim()}");
            return r;
        }

        // Writes the marker before anything else, so a half-built sandbox is recognised and rebuilt on the next launch.
        Git("init", "-q", "-b", "main");
        var gitDir = Path.Combine(path, ".git");
        File.WriteAllText(Path.Combine(gitDir, MarkerFile), "created by graftui --sandbox; safe to delete\n", NoBom);
        Directory.CreateDirectory(Path.Combine(gitDir, "graftui-no-hooks"));

        Git("config", "user.name", "graftui sandbox");
        Git("config", "user.email", "sandbox@example.invalid");
        Git("config", "commit.gpgsign", "false");
        Git("config", "tag.gpgsign", "false");
        Git("config", "core.hooksPath", ".git/graftui-no-hooks");
        Git("config", "core.autocrlf", "false");
        Git("config", "merge.ff", "true");

        // Fixed identity and dates, so the commits are the same on every rebuild.
        void Commit(string message, bool all = false)
        {
            commitNo++;
            var date = $"2025-01-01T12:{commitNo:00}:00+00:00";
            env["GIT_AUTHOR_NAME"] = "graftui sandbox";
            env["GIT_COMMITTER_NAME"] = "graftui sandbox";
            env["GIT_AUTHOR_EMAIL"] = "sandbox@example.invalid";
            env["GIT_COMMITTER_EMAIL"] = "sandbox@example.invalid";
            env["GIT_AUTHOR_DATE"] = date;
            env["GIT_COMMITTER_DATE"] = date;
            if (all)
                Git("commit", "-q", "-a", "-m", message);
            else
                Git("commit", "-q", "-m", message);
        }

        void Append(string file, string text, string message)
        {
            File.AppendAllText(Path.Combine(path, file), text + "\n", NoBom);
            Git("add", file);
            Commit(message);
        }

        void Overwrite(string file, string text, string message)
        {
            File.WriteAllText(Path.Combine(path, file), text, NoBom);
            Commit(message, all: true);
        }

        Append("README.md", "# sandbox", "Initial commit");
        Append("app.txt", "line 1", "Add app");

        // conflict: edits the same line of app.txt as main will
        Git("switch", "-q", "-c", "feature/conflict");
        Overwrite("app.txt", "line 1 (from conflict branch)\n", "Change line 1 on branch");
        Git("switch", "-q", "main");

        // main moves on, so the next two can't fast-forward
        Overwrite("app.txt", "line 1 (from main)\n", "Change line 1 on main");

        // merge commit: diverged, but touches a different file
        Git("switch", "-q", "-c", "feature/merge-commit", "HEAD~1");
        Append("other.txt", "other", "Add other file");
        Git("switch", "-q", "main");

        // fast-forward: branches off main's final tip, so main is its ancestor
        Git("switch", "-q", "-c", "feature/fast-forward");
        Append("ff.txt", "fast", "Add ff file");
        Git("switch", "-q", "main");

        // fast-forward onto a merge commit: main is an ancestor of the tip, and the tip is itself a merge
        Git("switch", "-q", "-c", "feature/ff-to-merge");
        Append("ffm.txt", "ffm", "Add ffm file");
        Git("switch", "-q", "-c", "side-for-ffm");
        Append("side.txt", "side", "Add side file");
        Git("switch", "-q", "feature/ff-to-merge");
        Git("merge", "-q", "--no-ff", "--no-edit", "side-for-ffm");
        Git("branch", "-q", "-d", "side-for-ffm");
        Git("switch", "-q", "main");

        // picks: three independent commits off main~1 (for cherry-pick later; merges as a merge commit)
        Git("switch", "-q", "-c", "feature/picks", "main~1");
        Overwrite("README.md", "# Sandbox\n", "Fix typo in README");
        Append("log.txt", "log", "Add logging");
        Append("VERSION", "0.3", "Bump version to 0.3");
        Git("switch", "-q", "main");

        var status = Git("status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status.StdOut))
            throw new InvalidOperationException("sandbox: work tree is not clean after building");
    }

    private static void TryDeleteTree(string p)
    {
        try
        {
            Directory.Delete(p, recursive: true);
        }
        catch (IOException)
        {
            // Left in place; the next run refuses it with a clear message.
        }
        catch (UnauthorizedAccessException)
        {
            // Left in place; the next run refuses it with a clear message.
        }
    }

    private static bool Exists(string p) => File.Exists(p) || Directory.Exists(p) || IsLink(p);

    private static bool IsLink(string p) =>
        new DirectoryInfo(p).LinkTarget is not null || new FileInfo(p).LinkTarget is not null;

    private static bool IsRegularFile(string p) => File.Exists(p) && !IsLink(p) && !Directory.Exists(p);

    private static string Display(string p)
    {
        var home = Home.TrimEnd(Path.DirectorySeparatorChar);
        return p.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "~" + p[home.Length..]
            : p;
    }
}

/// <summary>The sandbox folder exists but was not created by graftui, so it was left alone.</summary>
public sealed class SandboxRefusedException(string message) : Exception(message);
