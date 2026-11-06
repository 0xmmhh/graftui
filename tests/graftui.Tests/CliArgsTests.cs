namespace graftui.Tests;

public class CliArgsTests
{
    private static CliParse P(params string[] args) => CliArgs.Parse(args);

    private static CliOptions Ok(params string[] args)
    {
        var parsed = CliArgs.Parse(args);
        Assert.Null(parsed.Error);
        return parsed.Options!;
    }

    [Fact]
    public void No_arguments_means_a_plain_run_with_nothing_set()
    {
        Assert.Equal(new CliOptions(false, false, false, false, null, null), Ok());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_flags_set_Help(string flag)
    {
        Assert.True(Ok(flag).Help);
    }

    [Fact]
    public void Version_flag_sets_Version()
    {
        Assert.True(Ok("--version").Version);
    }

    [Fact]
    public void Sandbox_is_a_flag_and_repeating_it_is_harmless()
    {
        Assert.True(Ok("--sandbox").Sandbox);
        Assert.True(Ok("--sandbox", "--sandbox").Sandbox);
    }

    [Fact]
    public void Make_edits_is_a_flag_that_sets_MakeEdits()
    {
        Assert.True(Ok("--make-edits").MakeEdits);
        Assert.False(Ok("--sandbox").MakeEdits);
    }

    [Fact]
    public void Make_edits_keeps_a_given_path()
    {
        var o = Ok("--make-edits", "/tmp/practice");
        Assert.True(o.MakeEdits);
        Assert.Equal("/tmp/practice", o.RepoPath);
    }

    [Fact]
    public void Make_edits_with_sandbox_is_refused()
    {
        const string expected = "graftui: --make-edits can't be combined with --sandbox; run them one after the other";
        Assert.Equal(expected, P("--make-edits", "--sandbox").Error);
        Assert.Equal(expected, P("--sandbox", "--make-edits").Error);
    }

    [Fact]
    public void Make_edits_is_not_in_the_help_text()
    {
        Assert.DoesNotContain("make-edits", CliArgs.HelpText);
    }

    [Fact]
    public void Theme_takes_the_next_argument_or_an_equals_sign()
    {
        Assert.Equal("light", Ok("--theme", "light").Theme);
        Assert.Equal("light", Ok("--theme=light").Theme);
    }

    [Theory]
    [InlineData("LIGHT", "light")]
    [InlineData("High-Contrast", "high-contrast")]
    public void Theme_names_are_case_insensitive_and_stored_in_lower_case(string given, string expected)
    {
        Assert.Equal(expected, Ok("--theme", given).Theme);
    }

    [Fact]
    public void Sandbox_with_a_theme_is_fine()
    {
        var o = Ok("--sandbox", "--theme", "light");
        Assert.True(o.Sandbox);
        Assert.Equal("light", o.Theme);
        Assert.Null(o.RepoPath);
    }

    [Fact]
    public void A_path_with_a_theme_is_fine()
    {
        var o = Ok("path", "--theme", "muted");
        Assert.Equal("path", o.RepoPath);
        Assert.Equal("muted", o.Theme);
    }

    [Fact]
    public void Options_may_come_after_the_path()
    {
        var o = Ok("~/repo", "--theme=light");
        Assert.Equal("~/repo", o.RepoPath);
        Assert.Equal("light", o.Theme);
    }

    [Fact]
    public void Double_dash_makes_the_rest_paths()
    {
        var o = Ok("--", "-odd-dir");
        Assert.Equal("-odd-dir", o.RepoPath);
        Assert.False(o.Help);
    }

    [Fact]
    public void Double_dash_keeps_option_words_as_paths()
    {
        Assert.Equal("--help", Ok("--", "--help").RepoPath);
    }

    [Fact]
    public void Two_paths_after_double_dash_are_too_many()
    {
        Assert.Equal("graftui: too many arguments", P("--", "-odd-dir", "extra").Error);
    }

    [Fact]
    public void An_empty_path_is_refused_and_never_reaches_the_path_code()
    {
        Assert.Equal("graftui: empty path", P("").Error);
        Assert.Equal("graftui: empty path", P("--", "").Error);
        Assert.Equal("graftui: empty path", P("", "/tmp").Error);
        Assert.Equal("graftui: empty path", P("--sandbox", "").Error);
        Assert.Null(P("", "--help").Error);
        Assert.True(P("", "--help").Options!.Help);
    }

    [Fact]
    public void Two_paths_are_too_many_arguments()
    {
        Assert.Equal("graftui: too many arguments", P("a", "b").Error);
    }

    [Fact]
    public void An_unknown_option_is_named_in_the_error()
    {
        Assert.Equal("graftui: unknown option '--bogus'", P("--bogus").Error);
        Assert.Equal("graftui: unknown option '-x'", P("-x").Error);
    }

    [Fact]
    public void A_lone_dash_is_an_unknown_option()
    {
        Assert.Equal("graftui: unknown option '-'", P("-").Error);
    }

    [Fact]
    public void Theme_without_a_name_is_an_error()
    {
        const string expected = "graftui: --theme needs a name: default, lifted, muted, high-contrast, light";
        Assert.Equal(expected, P("--theme").Error);
        Assert.Equal(expected, P("--theme=").Error);
        Assert.Equal(expected, P("--theme", "").Error);
    }

    [Fact]
    public void Theme_followed_by_another_option_has_no_name()
    {
        var parsed = P("--theme", "--sandbox");
        Assert.Equal("graftui: --theme needs a name: default, lifted, muted, high-contrast, light", parsed.Error);
    }

    [Fact]
    public void Unknown_theme_lists_the_choices()
    {
        Assert.Equal(
            "graftui: unknown theme 'nope'; choose one of: default, lifted, muted, high-contrast, light",
            P("--theme", "nope").Error);
        Assert.Equal(
            "graftui: unknown theme 'nope'; choose one of: default, lifted, muted, high-contrast, light",
            P("--theme=nope").Error);
    }

    [Fact]
    public void Theme_given_twice_is_an_error()
    {
        Assert.Equal("graftui: --theme given more than once", P("--theme", "light", "--theme", "muted").Error);
        Assert.Equal("graftui: --theme given more than once", P("--theme=light", "--theme=light").Error);
    }

    [Fact]
    public void The_first_error_is_the_one_reported()
    {
        Assert.Equal("graftui: unknown option '--bogus'", P("--bogus", "a", "b").Error);
        Assert.Equal("graftui: unknown theme 'nope'; choose one of: default, lifted, muted, high-contrast, light",
            P("--theme", "nope", "--theme", "light").Error);
    }

    [Fact]
    public void Sandbox_with_a_path_is_refused()
    {
        const string expected = "graftui: --sandbox opens ~/.graftui/sandbox; it can't be combined with a path";
        Assert.Equal(expected, P("--sandbox", "/tmp").Error);
        Assert.Equal(expected, P("/tmp", "--sandbox").Error);
        Assert.Equal(expected, P("--sandbox", "--", "-odd").Error);
    }

    [Fact]
    public void Help_wins_over_a_bad_theme_and_over_too_many_arguments()
    {
        var help = P("--help", "--theme", "nope");
        Assert.Null(help.Error);
        Assert.True(help.Options!.Help);

        var late = P("a", "b", "--help");
        Assert.Null(late.Error);
        Assert.True(late.Options!.Help);
    }

    [Fact]
    public void Version_wins_over_errors()
    {
        var parsed = P("--version", "--bogus");
        Assert.Null(parsed.Error);
        Assert.True(parsed.Options!.Version);
    }

    [Fact]
    public void Help_and_version_together_both_set()
    {
        var o = Ok("--version", "--help");
        Assert.True(o.Help);
        Assert.True(o.Version);
    }

    [Fact]
    public void VersionLine_is_the_version_without_a_build_suffix()
    {
        Assert.Equal("graftui 0.1.0", CliArgs.VersionLine());
    }

    [Fact]
    public void HelpText_lists_the_new_options_and_the_keys()
    {
        var help = CliArgs.HelpText;
        Assert.StartsWith("graftui: an animated git TUI\n\nUsage:", help);
        Assert.Contains("--theme <name>", help);
        Assert.Contains("--version", help);
        Assert.Contains("--help", help);
        Assert.Contains("x discard", help);
        Assert.Contains("Z pop", help);
        Assert.Contains("u undo", help);
        Assert.Contains("v all commands", help);
        Assert.EndsWith("\n", help);
    }

    [Fact]
    public void HelpText_gives_each_tab_its_own_keys_line_and_the_shared_keys_their_own_line()
    {
        var lines = CliArgs.HelpText.Split('\n');

        Assert.Contains(lines, l => l.StartsWith("Branches: ", StringComparison.Ordinal) && l.Contains("r rebase"));
        Assert.Contains(lines, l => l.StartsWith("Branches: ", StringComparison.Ordinal) && l.Contains("i interactive"));
        Assert.Contains(lines, l => l.StartsWith("Todo rebase: ", StringComparison.Ordinal) && l.Contains("⇧↑↓/J K reorder") && l.Contains("Enter start"));
        Assert.Contains(lines, l => l.StartsWith("Changes: ", StringComparison.Ordinal) && l.Contains("Space stage/unstage"));
        // v and Esc work on both tabs, so they are not on either tab's line.
        Assert.Contains("Both tabs: v all commands · Esc quit", lines);
        // A paused rebase takes its own keys line.
        Assert.Contains(lines, l => l.StartsWith("Paused rebase: ", StringComparison.Ordinal) && l.Contains("X abort"));
        Assert.DoesNotContain(lines, l => l.StartsWith("Changes: ", StringComparison.Ordinal) && l.Contains("Esc quit"));
    }
}
