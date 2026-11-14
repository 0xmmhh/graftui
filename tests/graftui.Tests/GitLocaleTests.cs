using graftui.Git;

namespace graftui.Tests;

/// <summary>Changes the test process's own environment, so it runs alone, never beside other tests.</summary>
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

/// <summary>
/// git speaks English for graftui whatever the user's language: graftui reads some of git's words (CONFLICT, error:, fatal:).
/// Only the message language changes: hooks keep the user's character set, and commit messages and file names come back unchanged.
/// Each test runs with a German locale in this process's environment (which git inherits); Dispose puts the old values back.
/// </summary>
[Collection("ProcessEnvironment")]
public sealed class GitLocaleTests : IDisposable
{
    private static readonly string[] Names = ["LC_ALL", "LANG", "LANGUAGE", "LC_MESSAGES", "LC_CTYPE"];

    private readonly Dictionary<string, string?> _saved = new();

    public GitLocaleTests()
    {
        foreach (var name in Names)
            _saved[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable("LC_ALL", "de_DE.UTF-8");
        Environment.SetEnvironmentVariable("LANG", "en_US.UTF-8");
        Environment.SetEnvironmentVariable("LANGUAGE", "de");
        Environment.SetEnvironmentVariable("LC_MESSAGES", "de_DE.UTF-8");
        Environment.SetEnvironmentVariable("LC_CTYPE", "C");
    }

    public void Dispose()
    {
        foreach (var name in Names)
            Environment.SetEnvironmentVariable(name, _saved[name]);
    }

    [Fact]
    public void Git_and_its_hooks_get_english_messages_and_keep_the_users_character_set()
    {
        using var repo = TempRepo.Create();

        // An alias starting with ! runs in a shell with git's environment, as a hook does.
        var result = repo.Git.Run("-c", "alias.showenv=!env", "showenv");
        Assert.True(result.Success, result.StdErr);
        var env = result.StdOut.Split('\n');

        Assert.Contains("LC_MESSAGES=C", env);
        Assert.Contains("LANGUAGE=C", env);
        Assert.Contains("LANG=de_DE.UTF-8", env);
        Assert.Contains("LC_CTYPE=de_DE.UTF-8", env);
        Assert.DoesNotContain(env, line => line.StartsWith("LC_ALL=", StringComparison.Ordinal));
    }

    [Fact]
    public void Git_messages_are_in_english()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");

        var result = repo.Git.Run("checkout", "no-such-branch");

        Assert.False(result.Success);
        Assert.Contains("error: pathspec 'no-such-branch' did not match", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_ascii_subjects_and_file_names_come_back_unchanged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");
        repo.Commit("größe.txt", "x\n", "Größe ändern ✓");

        var commit = repo.Git.GetCommit(repo.Head);
        var files = repo.Git.Run("-c", "core.quotePath=false", "ls-files").StdOut;

        Assert.NotNull(commit);
        Assert.Equal("Größe ändern ✓", commit.Subject);
        Assert.Contains("größe.txt", files, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LC_CTYPE")]
    [InlineData("LC_COLLATE")]
    [InlineData("LC_MONETARY")]
    [InlineData("LC_NUMERIC")]
    [InlineData("LC_TIME")]
    [InlineData("LC_ADDRESS")]
    [InlineData("LC_IDENTIFICATION")]
    [InlineData("LC_MEASUREMENT")]
    [InlineData("LC_NAME")]
    [InlineData("LC_PAPER")]
    [InlineData("LC_TELEPHONE")]
    public void Lc_all_preserves_the_effective_non_message_category(string category)
    {
        var env = new Dictionary<string, string?>
        {
            ["LC_ALL"] = "de_DE.UTF-8", ["LANG"] = "en_US.UTF-8", [category] = "C",
        };

        GitClient.UseEnglishMessages(env);

        Assert.Equal("de_DE.UTF-8", env[category]);
        Assert.Equal("de_DE.UTF-8", env["LANG"]);
        Assert.Equal("C", env["LC_MESSAGES"]);
        Assert.Equal("C", env["LANGUAGE"]);
        Assert.False(env.ContainsKey("LC_ALL"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_locale_without_lc_all_keeps_lang_and_only_the_messages_change(string? all)
    {
        var env = new Dictionary<string, string?>
        {
            ["LANG"] = "pl_PL.UTF-8", ["LC_CTYPE"] = "pl_PL.UTF-8", ["LC_TIME"] = "C",
        };
        if (all is not null)
            env["LC_ALL"] = all;

        GitClient.UseEnglishMessages(env);

        Assert.Equal("pl_PL.UTF-8", env["LANG"]);
        Assert.Equal("pl_PL.UTF-8", env["LC_CTYPE"]);
        Assert.Equal("C", env["LC_TIME"]);
        Assert.False(env.ContainsKey("LC_NUMERIC"));
        Assert.Equal("C", env["LC_MESSAGES"]);
        Assert.Equal("C", env["LANGUAGE"]);
        Assert.False(env.ContainsKey("LC_ALL"));
    }
}
