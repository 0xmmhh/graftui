using graftui.Git;

namespace graftui.Tests;

/// <summary>Changes the test process's own environment, so it runs alone, never beside other tests.</summary>
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

/// <summary>
/// git speaks English for graftui whatever the user's language: graftui reads some of git's words (CONFLICT, error:, fatal:).
/// Commit messages and file names are not translated, so they come back unchanged.
/// </summary>
[Collection("ProcessEnvironment")]
public sealed class GitLocaleTests
{
    private static readonly string[] Names = ["LC_ALL", "LANG", "LANGUAGE", "LC_MESSAGES"];

    [Fact]
    public void Git_messages_are_in_english_when_the_user_runs_a_german_locale()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");

        var result = WithGermanLocale(() => repo.Git.Run("checkout", "no-such-branch"));

        Assert.False(result.Success);
        Assert.Contains("error: pathspec 'no-such-branch' did not match", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_ascii_subjects_and_file_names_come_back_unchanged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");

        var commit = WithGermanLocale(() =>
        {
            repo.Commit("größe.txt", "x\n", "Größe ändern ✓");
            return repo.Git.GetCommit(repo.Head);
        });
        var files = repo.Git.Run("-c", "core.quotePath=false", "ls-files").StdOut;

        Assert.Equal("Größe ändern ✓", commit!.Subject);
        Assert.Contains("größe.txt", files, StringComparison.Ordinal);
    }

    // Runs the call with a German locale in this process's environment (which git inherits), then puts the old values back.
    private static T WithGermanLocale<T>(Func<T> call)
    {
        var saved = new Dictionary<string, string?>();
        foreach (var name in Names)
            saved[name] = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable("LC_ALL", "de_DE.UTF-8");
            Environment.SetEnvironmentVariable("LANG", "de_DE.UTF-8");
            Environment.SetEnvironmentVariable("LANGUAGE", "de");
            Environment.SetEnvironmentVariable("LC_MESSAGES", "de_DE.UTF-8");
            return call();
        }
        finally
        {
            foreach (var name in Names)
                Environment.SetEnvironmentVariable(name, saved[name]);
        }
    }
}
