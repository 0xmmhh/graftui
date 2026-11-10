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
