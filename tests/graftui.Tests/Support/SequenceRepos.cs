using System.Text;
using graftui.Git;

namespace graftui.Tests;

/// <summary>
/// Throwaway repos for the rebase and cherry-pick tests. Each builder leaves HEAD where the test expects it. Commits use
/// TempRepo.Commit, so every repo gets the same identity and no signing.
/// </summary>
internal static class SequenceRepos
{
    /// <summary>
    /// main and feature both start at "Add app". feature has four commits (f1..f4); main gains m.txt.
    /// HEAD is on feature.
    /// </summary>
    public static TempRepo Feature4()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("f1.txt", "1\n", "Add login form");
        repo.Commit("f2.txt", "2\n", "Fix typo in form");
        repo.Commit("f3.txt", "3\n", "Add password check");
        repo.Commit("f4.txt", "4\n", "WIP debug logging");
        repo.Run("switch", "-q", "main");
        repo.Commit("m.txt", "m\n", "Add m on main");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>Feature4, except the third feature commit and main both change line 1 of app.txt. HEAD is on feature.</summary>
    public static TempRepo FeatureConflict()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("f1.txt", "1\n", "Add login form");
        repo.Commit("f2.txt", "2\n", "Fix typo in form");
        repo.Commit("app.txt", "feature line 1\n", "Add password check");
        repo.Commit("f4.txt", "4\n", "WIP debug logging");
        repo.Run("switch", "-q", "main");
        repo.Commit("app.txt", "main line 1\n", "Change line 1 on main");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// main and feature both start at "Add app" (line 1 of app.txt). main changes line 1; feature has two commits that each change
    /// line 1 again, so rebasing onto main conflicts at the first one, and skipping it conflicts at the second. HEAD is on feature.
    /// </summary>
    public static TempRepo SkipStopsOnNextConflict()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("app.txt", "feature one\n", "Change line 1 on feature");
        repo.Commit("app.txt", "feature two\n", "Change line 1 again on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("app.txt", "main line 1\n", "Change line 1 on main");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// main has three commits of its own; feature has three (Add login screen, Fix null check in auth, Add remember-me option),
    /// all from the same base. HEAD is on main.
    /// </summary>
    public static TempRepo Picks()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "app\n", "Add app");
        repo.Commit("auth.ts", "auth\n", "Add auth");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("login.ts", "login\n", "Add login screen");
        repo.Commit("auth.ts", "auth fixed\n", "Fix null check in auth");
        repo.Commit("remember.ts", "remember\n", "Add remember-me option");
        repo.Run("switch", "-q", "main");
        repo.Commit("main-a.txt", "a\n", "Add main a");
        repo.Commit("main-b.txt", "b\n", "Add main b");
        repo.Commit("main-c.txt", "c\n", "Add main c");
        return repo;
    }

    /// <summary>
    /// The second feature commit (Fix null check in auth) and main both change auth.ts, so picking it conflicts. HEAD is on main.
    /// </summary>
    public static TempRepo PickConflict()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "app\n", "Add app");
        repo.Commit("auth.ts", "auth\n", "Add auth");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("login.ts", "login\n", "Add login screen");
        repo.Commit("auth.ts", "auth fixed\n", "Fix null check in auth");
        repo.Run("switch", "-q", "main");
        repo.Commit("auth.ts", "auth main\n", "Change auth on main");
        return repo;
    }

    /// <summary>
    /// The picks of a three-commit feature (login, fix, remember). The fix conflicts with main's change to auth.ts, so a pick of all three
    /// pauses on the second and leaves the third to come. HEAD is on main.
    /// </summary>
    public static TempRepo PickConflictThenMore()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "app\n", "Add app");
        repo.Commit("auth.ts", "auth\n", "Add auth");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("login.ts", "login\n", "Add login screen");
        repo.Commit("auth.ts", "auth fixed\n", "Fix null check in auth");
        repo.Commit("remember.ts", "remember\n", "Add remember-me option");
        repo.Run("switch", "-q", "main");
        repo.Commit("auth.ts", "auth main\n", "Change auth on main");
        return repo;
    }

    /// <summary>
    /// The picks of a user's branch: feature has three commits in three different files (README.md, log.txt, VERSION), oldest first.
    /// Fix typo in README, Add logging, Bump version to 0.3. main has one commit of its own (main.txt). HEAD is on feature.
    /// </summary>
    public static TempRepo TypoLogVersion()
    {
        var repo = TempRepo.Create();
        repo.Commit("README.md", "hello\n", "Initial");
        repo.Commit("VERSION", "0.2\n", "Add version");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("README.md", "hallo\n", "Fix typo in README");
        repo.Commit("log.txt", "log\n", "Add logging");
        repo.Commit("VERSION", "0.3\n", "Bump version to 0.3");
        repo.Run("switch", "-q", "main");
        repo.Commit("main.txt", "main\n", "Add main file");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// main has logo.bin ("Add logo"). feature changes it ("Feature logo") and main changes it too ("Main logo"): a binary conflict. HEAD is on
    /// feature.
    /// </summary>
    public static TempRepo BinaryConflict() => BinaryPair(headOnMain: false);

    /// <summary>BinaryConflict with HEAD on main: picking "Feature logo" onto main conflicts the same way.</summary>
    public static TempRepo PickBinary() => BinaryPair(headOnMain: true);

    /// <summary>
    /// BinaryConflict's logo.bin, and app.txt changed on both sides by the same feature commit, so a rebase onto main pauses with two
    /// conflicted files: app.txt with markers, logo.bin without. HEAD is on feature.
    /// </summary>
    public static TempRepo BinaryAndTextConflict()
    {
        var repo = TempRepo.Create();
        CommitBoth(repo, Blob("base", 1), "line 1\n", "Add logo and app");
        repo.Run("switch", "-q", "-c", "feature");
        CommitBoth(repo, Blob("feat", 2), "feature line 1\n", "Feature logo and app");
        repo.Run("switch", "-q", "main");
        CommitBoth(repo, Blob("main", 3), "main line 1\n", "Main logo and app");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// gone.txt is edited on feature ("Edit gone", content "feature") and deleted on main ("Delete gone"): a modify/delete conflict where
    /// the main side deleted the file. HEAD is on feature.
    /// </summary>
    public static TempRepo ModifyDelete()
    {
        var repo = TempRepo.Create();
        repo.Commit("gone.txt", "d\n", "Add gone");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("gone.txt", "feature\n", "Edit gone");
        repo.Run("switch", "-q", "main");
        repo.Run("rm", "-q", "gone.txt");
        repo.Run("commit", "-q", "-m", "Delete gone");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// gone.txt is deleted on feature ("Delete gone") and edited on main ("Edit gone on main"): a delete/modify conflict where the feature
    /// side deleted the file. HEAD is on feature.
    /// </summary>
    public static TempRepo DeleteModify()
    {
        var repo = TempRepo.Create();
        repo.Commit("gone.txt", "d\n", "Add gone");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("rm", "-q", "gone.txt");
        repo.Run("commit", "-q", "-m", "Delete gone");
        repo.Run("switch", "-q", "main");
        repo.Commit("gone.txt", "main\n", "Edit gone on main");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// a.txt is renamed to c.txt on feature ("Rename to c") and to b.txt on main ("Rename to b"): a rename/rename conflict that leaves a.txt
    /// with only the base stage (deleted on both sides), b.txt only on ours and c.txt only on theirs. HEAD is on feature.
    /// </summary>
    public static TempRepo RenameRename()
    {
        var repo = TempRepo.Create();
        repo.Commit("a.txt", "one\ntwo\nthree\nfour\n", "Add a");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("mv", "a.txt", "c.txt");
        repo.Run("commit", "-q", "-m", "Rename to c");
        repo.Run("switch", "-q", "main");
        repo.Run("mv", "a.txt", "b.txt");
        repo.Run("commit", "-q", "-m", "Rename to b");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// app.lock is set to merge=binary, so git does not merge it: it leaves the file unmerged with no markers, holding main's bytes. feature
    /// and main both change app.lock ("Feature lock", "Main lock"). HEAD is on feature.
    /// </summary>
    public static TempRepo MergeBinaryLock()
    {
        var repo = TempRepo.Create();
        repo.Commit(".gitattributes", "*.lock merge=binary\n", "Mark lock files binary");
        repo.Commit("app.lock", "base\n", "Add lock");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("app.lock", "feature\n", "Feature lock");
        repo.Run("switch", "-q", "main");
        repo.Commit("app.lock", "main\n", "Main lock");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// ln is a symbolic link to app.txt on the base. feature and main each turn it into a file with different text ("Feature ln", "Main ln"),
    /// so the conflict has a link in the base and text on both sides. HEAD is on feature.
    /// </summary>
    public static TempRepo LinkReplacedByText()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "app\n", "Add app");
        File.CreateSymbolicLink(Path.Combine(repo.Dir, "ln"), "app.txt");
        repo.Run("add", "ln");
        repo.Run("commit", "-q", "-m", "Add ln");
        repo.Run("switch", "-q", "-c", "feature");
        File.Delete(Path.Combine(repo.Dir, "ln"));
        repo.Commit("ln", "feature text\n", "Feature ln");
        repo.Run("switch", "-q", "main");
        File.Delete(Path.Combine(repo.Dir, "ln"));
        repo.Commit("ln", "main text\n", "Main ln");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// main and feature both add a symbolic link named link, with different targets ("Main link" to b.txt, which does not exist, so the
    /// link is dangling; "Feature link" to app.txt). HEAD is on feature.
    /// </summary>
    public static TempRepo SymlinkConflict()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "app\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        File.CreateSymbolicLink(Path.Combine(repo.Dir, "link"), "app.txt");
        repo.Run("add", "link");
        repo.Run("commit", "-q", "-m", "Feature link");
        repo.Run("switch", "-q", "main");
        File.CreateSymbolicLink(Path.Combine(repo.Dir, "link"), "b.txt");
        repo.Run("add", "link");
        repo.Run("commit", "-q", "-m", "Main link");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// feature adds a file named dir ("Feature file dir"); main adds a folder named dir with x.txt in it ("Main dir"). Git's rebase keeps
    /// main's folder and leaves the file under a longer name, so the conflict is a stage 3 entry alone: only theirs added it. HEAD is on feature.
    /// </summary>
    public static TempRepo FileVersusFolder()
    {
        var repo = TempRepo.Create();
        repo.Commit("README", "base\n", "Add readme");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("dir", "file\n", "Feature file dir");
        repo.Run("switch", "-q", "main");
        Directory.CreateDirectory(Path.Combine(repo.Dir, "dir"));
        repo.Commit("dir/x.txt", "x\n", "Main dir");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>
    /// Both branches add a submodule named sub, each pinned to a different commit (a gitlink needs no checkout of the submodule). HEAD is on
    /// feature.
    /// </summary>
    public static TempRepo SubmoduleConflict()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "app\n", "Add app");
        var baseCommit = repo.Head;
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("update-index", "--add", "--cacheinfo", "160000," + baseCommit + ",sub");
        repo.Run("commit", "-q", "-m", "Feature sub");
        repo.Run("switch", "-q", "main");
        repo.Commit("main.txt", "main\n", "Add main file");
        var mainCommit = repo.Head;
        repo.Run("update-index", "--add", "--cacheinfo", "160000," + mainCommit + ",sub");
        repo.Run("commit", "-q", "-m", "Main sub");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>The two logo.bin commits, with HEAD on main or on feature.</summary>
    private static TempRepo BinaryPair(bool headOnMain)
    {
        var repo = TempRepo.Create();
        CommitBytes(repo, "logo.bin", Blob("base", 1), "Add logo");
        repo.Run("switch", "-q", "-c", "feature");
        CommitBytes(repo, "logo.bin", Blob("feat", 2), "Feature logo");
        repo.Run("switch", "-q", "main");
        CommitBytes(repo, "logo.bin", Blob("main", 3), "Main logo");
        if (!headOnMain)
            repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>Bytes that are not text: the head as ASCII, a NUL and a tail byte.</summary>
    private static byte[] Blob(string head, byte tail)
    {
        var text = Encoding.ASCII.GetBytes(head);
        return [.. text, 0, tail];
    }

    /// <summary>Writes the bytes to a file, stages it and commits it.</summary>
    private static void CommitBytes(TempRepo repo, string file, byte[] bytes, string message)
    {
        File.WriteAllBytes(Path.Combine(repo.Dir, file), bytes);
        repo.Run("add", file);
        repo.Run("commit", "-q", "-m", message);
    }

    /// <summary>Writes logo.bin and app.txt, stages both and commits them together.</summary>
    private static void CommitBoth(TempRepo repo, byte[] logo, string app, string message)
    {
        File.WriteAllBytes(Path.Combine(repo.Dir, "logo.bin"), logo);
        repo.Write("app.txt", app);
        repo.Run("add", "logo.bin", "app.txt");
        repo.Run("commit", "-q", "-m", message);
    }

    /// <summary>
    /// Starts `git rebase -i onto` by hand with the given todo lines, the way graftui runs it (a sequence editor that copies the list).
    /// Returns git's result. The todo file is removed once git has copied it.
    /// </summary>
    public static GitResult RebaseByHand(TempRepo repo, string onto, params string[] todoLines)
    {
        var todo = Path.Combine(Path.GetTempPath(), "graftui-todo-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(todo, string.Join('\n', todoLines) + "\n", new UTF8Encoding(false));
        try
        {
            var env = new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = "cp '" + todo + "'" };
            return repo.Git.RunWithEnv(env, "rebase", "-i", onto);
        }
        finally
        {
            File.Delete(todo);
        }
    }
}
