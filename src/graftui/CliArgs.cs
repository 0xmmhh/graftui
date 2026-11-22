using System.Reflection;

namespace graftui;

/// <summary>Command-line parsing: the public entry points and the help text. The reader itself is in CliParser.cs.</summary>
internal static class CliArgs
{
    public const string HelpText = """
graftui: an animated git TUI

Usage:
  graftui [options] [path-to-repo]   open a repository (default: the current directory)
  graftui --sandbox [options]        rebuild the practice repo at ~/.graftui/sandbox and open it

Options:
  --theme <name>   colours: default, lifted, muted, high-contrast, light
  --new-ui         the new look, still being built (same as GRAFTUI_UI=new)
  --glyphs <set>   basic (plain ASCII) or unicode (default), for the new look (--new-ui);
                   the old look is only partly covered. Also GRAFTUI_GLYPHS
  --version        print the version
  --help           show this help

Branches: Tab Changes · ↑/↓ select · m merge · s switch · n new · R rename · D delete · r rebase · i interactive · u undo
Changes: Space stage/unstage · a stage all · U unstage all · c commit · A amend · x discard · z stash · Z pop · u undo
Todo rebase: ↑/↓/←/→ move · ⇧↑↓/J K reorder · p pick · r reword · e edit · s squash · f fixup · d drop · Enter start · Esc cancel
Paused rebase: Enter continue · S skip · X abort · Tab changes · Esc quit
Both tabs: v all commands · Esc quit

""";

    /// <summary>
    /// Reads the arguments left to right. Help beats Version, and both beat any error. Otherwise the first error wins.
    /// `--` ends the options; anything after it is a path. `--theme` takes the next argument, or `--theme=name`.
    /// </summary>
    public static CliParse Parse(IReadOnlyList<string> args) => Parse(args, null);

    /// <summary>
    /// The same, with the value of GRAFTUI_GLYPHS passed in (null or empty when unset). A --glyphs flag wins over that value;
    /// the value is only checked when no flag gave a set.
    /// </summary>
    public static CliParse Parse(IReadOnlyList<string> args, string? glyphsEnvironment) =>
        new CliParser(args, glyphsEnvironment).ReadAll();

    /// <summary>
    /// Whether the new look is on: the --new-ui flag, or GRAFTUI_UI=new in the environment. The environment value is passed in, so this
    /// stays pure. Any other environment value leaves the new look off.
    /// </summary>
    public static bool NewUiOn(bool flag, string? environmentValue)
    {
        if (flag)
            return true;
        return string.Equals(environmentValue, "new", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"graftui 0.1.0": the informational version without any "+build" suffix.</summary>
    public static string VersionLine()
    {
        var assembly = typeof(CliArgs).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        var plus = info.IndexOf('+');
        return "graftui " + (plus >= 0 ? info[..plus] : info);
    }
}
