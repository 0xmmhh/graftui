using graftui.Git;

namespace graftui.Tests;

/// <summary>
/// git starts with a pristine environment: only the allowlisted parent entries pass through, so an inherited GIT_DIR
/// (or GIT_ASKPASS, EDITOR, GIT_CONFIG_COUNT, ...) can never redirect graftui or stall it waiting for input.
/// These tests change this process's own environment, so they run alone, never beside other tests.
/// </summary>
[Collection("ProcessEnvironment")]
public sealed class GitEnvAllowlistTests : IDisposable
{
    private readonly Dictionary<string, string?> _saved = new();

    public void Dispose()
    {
        foreach (var pair in _saved)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private void SetEnv(string name, string value)
    {
        if (!_saved.ContainsKey(name))
            _saved[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    [Theory]
    [InlineData("HOME")]
    [InlineData("PATH")]
    [InlineData("USER")]
    [InlineData("LOGNAME")]
    [InlineData("SHELL")]
    [InlineData("TMPDIR")]
    [InlineData("TERM")]
    [InlineData("SSH_AUTH_SOCK")]
    [InlineData("SSH_AGENT_PID")]
    [InlineData("GPG_AGENT_INFO")]
    [InlineData("GNUPGHOME")]
    [InlineData("DISPLAY")]
    [InlineData("WAYLAND_DISPLAY")]
    [InlineData("GIT_CONFIG_GLOBAL")]
    [InlineData("GIT_CONFIG_NOSYSTEM")]
    [InlineData("XDG_CACHE_HOME")]
    [InlineData("XDG_CONFIG_HOME")]
    [InlineData("XDG_RUNTIME_DIR")]
    [InlineData("SYSTEMROOT")]
    [InlineData("TEMP")]
    [InlineData("TMP")]
    [InlineData("USERNAME")]
    [InlineData("USERPROFILE")]
    [InlineData("LANG")]
    [InlineData("LC_ALL")]
    [InlineData("LC_CTYPE")]
    [InlineData("LC_TIME")]
    public void Allowed_names_pass_the_allowlist(string name)
    {
        Assert.True(GitClient.IsEnvAllowed(name));
    }

    [Theory]
    [InlineData("GIT_DIR")]
    [InlineData("GIT_WORK_TREE")]
    [InlineData("GIT_INDEX_FILE")]
    [InlineData("GIT_ASKPASS")]
    [InlineData("SSH_ASKPASS")]
    [InlineData("EDITOR")]
    [InlineData("VISUAL")]
    [InlineData("GIT_EDITOR")]
    [InlineData("GIT_SEQUENCE_EDITOR")]
    [InlineData("GIT_CONFIG_COUNT")]
    [InlineData("GIT_CONFIG_KEY_0")]
    [InlineData("GIT_CONFIG_VALUE_0")]
    [InlineData("GIT_CONFIG_PARAMETERS")]
    [InlineData("GIT_CONFIG_FOO")]
    [InlineData("GPG_TTY")]
    [InlineData("GIT_CEILING_DIRECTORIES")]
    [InlineData("GIT_DISCOVERY_ACROSS_FILESYSTEM")]
    public void Dangerous_names_do_not_pass_the_allowlist(string name)
    {
        Assert.False(GitClient.IsEnvAllowed(name));
    }

    [Fact]
    public void Git_dir_in_the_parent_env_does_not_redirect_graftui()
    {
        using var repoA = TempRepo.Create();
        using var repoB = TempRepo.Create();
        repoA.Commit("a.txt", "a\n", "Commit in A");
        repoB.Commit("b.txt", "b\n", "Commit in B");

        SetEnv("GIT_DIR", Path.Combine(repoB.Dir, ".git"));
        SetEnv("GIT_WORK_TREE", repoB.Dir);

        var head = repoA.Git.Run("rev-parse", "HEAD");
        Assert.True(head.Success, head.StdErr);
        Assert.Equal(repoA.Head, head.StdOut.Trim());
        Assert.NotEqual(repoB.Head, head.StdOut.Trim());

        repoA.Write("dirty.txt", "dirty\n");
        var status = repoA.Git.Run("status", "--porcelain");
        Assert.True(status.Success, status.StdErr);
        Assert.Contains("dirty.txt", status.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_allowlist_reaches_git()
    {
        using var repo = TempRepo.Create();

        SetEnv("SSH_AUTH_SOCK", "/tmp/graftui-test-agent.sock");
        SetEnv("GPG_TTY", "/dev/graftui-test-tty");
        SetEnv("XDG_GRAFTUI_TEST", "xdg-test-value");
        SetEnv("GIT_ASKPASS", "/tmp/graftui-test-askpass");
        SetEnv("SSH_ASKPASS", "/tmp/graftui-test-ssh-askpass");
        SetEnv("EDITOR", "false");
        SetEnv("VISUAL", "false");
        SetEnv("GIT_CONFIG_COUNT", "1");
        SetEnv("GIT_CONFIG_KEY_0", "user.name");
        SetEnv("GIT_CONFIG_VALUE_0", "impostor");
        SetEnv("GIT_DIR", "/tmp/graftui-test-no-such-repo.git");
        SetEnv("GIT_WORK_TREE", "/tmp/graftui-test-no-such-tree");
        SetEnv("GIT_INDEX_FILE", "/tmp/graftui-test-index");

        Assert.Equal("1", Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT"));
        Assert.Equal("user.name", Environment.GetEnvironmentVariable("GIT_CONFIG_KEY_0"));
        Assert.Equal("impostor", Environment.GetEnvironmentVariable("GIT_CONFIG_VALUE_0"));

        // An alias starting with ! runs in a shell with git's environment, as a hook does.
        var result = repo.Git.Run("-c", "alias.showenv=!env", "showenv");
        Assert.True(result.Success, result.StdErr);
        var env = result.StdOut.Split('\n');

        Assert.Contains("SSH_AUTH_SOCK=/tmp/graftui-test-agent.sock", env);
        Assert.Contains("XDG_GRAFTUI_TEST=xdg-test-value", env);

        // The tests' hermetic config still reaches git.
        Assert.Contains(env, line => line.StartsWith("GIT_CONFIG_GLOBAL=", StringComparison.Ordinal));
        Assert.Contains("GIT_CONFIG_NOSYSTEM=1", env);

        var home = Environment.GetEnvironmentVariable("HOME");
        if (home is not null)
            Assert.Contains("HOME=" + home, env);
        // git prepends its own exec dir to PATH for its children (so hooks find git), so the child's PATH only
        // has to contain the parent's value, not equal it.
        var parentPath = Environment.GetEnvironmentVariable("PATH");
        var pathLine = env.FirstOrDefault(line => line.StartsWith("PATH=", StringComparison.Ordinal));
        Assert.NotNull(pathLine);
        if (parentPath is not null)
            Assert.Contains(parentPath, pathLine, StringComparison.Ordinal);

        var dropped = new[]
        {
            "GIT_DIR=", "GIT_WORK_TREE=", "GIT_INDEX_FILE=",
            "GIT_ASKPASS=", "SSH_ASKPASS=",
            "EDITOR=", "VISUAL=",
            "GIT_CONFIG_COUNT=", "GIT_CONFIG_KEY_0=", "GIT_CONFIG_VALUE_0=",
            "LC_ALL=", "GPG_TTY=",
        };
        foreach (var prefix in dropped)
            Assert.DoesNotContain(env, line => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void The_users_locale_reaches_git_with_english_messages()
    {
        SetEnv("LANG", "pl_PL.UTF-8");
        SetEnv("LC_CTYPE", "pl_PL.UTF-8");
        SetEnv("LC_ALL", "");

        using var repo = TempRepo.Create();
        var result = repo.Git.Run("-c", "alias.showenv=!env", "showenv");

        Assert.True(result.Success, result.StdErr);
        var env = result.StdOut.Split('\n');
        // The character set stays the user's, so hooks and signing helpers read and write UTF-8.
        Assert.Contains("LANG=pl_PL.UTF-8", env);
        Assert.Contains("LC_CTYPE=pl_PL.UTF-8", env);
        // Only the message language is English.
        Assert.Contains("LC_MESSAGES=C", env);
        Assert.Contains("LANGUAGE=C", env);
        Assert.DoesNotContain(env, line => line.StartsWith("LC_ALL=", StringComparison.Ordinal));
    }

    [Fact]
    public void The_callers_env_is_applied_after_the_allowlist()
    {
        using var repo = TempRepo.Create();
        var caller = new Dictionary<string, string> { ["GRAFTUI_CALLER_TEST"] = "caller-test-value" };

        var result = repo.Git.RunWithEnv(caller, "-c", "alias.showenv=!env", "showenv");

        Assert.True(result.Success, result.StdErr);
        Assert.Contains("GRAFTUI_CALLER_TEST=caller-test-value", result.StdOut.Split('\n'));
    }
}
