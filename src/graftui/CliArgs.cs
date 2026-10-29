using System.Reflection;
using graftui.Ui.Views;

namespace graftui;

/// <summary>The parsed command line. Help and Version are acted on by Program in that order; neither needs a repo.</summary>
internal sealed record CliOptions(bool Help, bool Version, bool Sandbox, string? Theme, string? RepoPath);

/// <summary>Either the options, or the one stderr line for a bad command line (Program adds "Try 'graftui --help'.").</summary>
internal sealed record CliParse(CliOptions? Options, string? Error);

/// <summary>Command-line parsing. Pure: no console, no repo, no palette.</summary>
internal static class CliArgs
{
    public const string HelpText = """
graftui: an animated git TUI

Usage:
  graftui [options] [path-to-repo]   open a repository (default: the current directory)
  graftui --sandbox [options]        rebuild the practice repo at ~/.graftui/sandbox and open it

Options:
  --theme <name>   colours: default, lifted, muted, high-contrast, light
  --version        print the version
  --help           show this help

Keys: Tab Branches/Changes · ↑/↓ select · m merge · Space stage/unstage · a stage all · U unstage all · c commit · A amend · x discard · z stash · Z pop · u undo · v all commands · Esc quit

""";

    /// <summary>
    /// Reads the arguments left to right. Help beats Version, and both beat any error. Otherwise the first error wins.
    /// `--` ends the options; anything after it is a path. `--theme` takes the next argument, or `--theme=name`.
    /// </summary>
    public static CliParse Parse(IReadOnlyList<string> args)
    {
        var help = false;
        var version = false;
        var sandbox = false;
        var themeSeen = false;
        string? theme = null;
        string? path = null;
        string? error = null;
        var pathsOnly = false;

        void Fail(string message) => error ??= message;

        void TakePath(string arg)
        {
            // "" is no folder: it must not reach Path.GetFullPath, which throws on it.
            if (arg.Length == 0)
                Fail("graftui: empty path");
            else if (path is null)
                path = arg;
            else
                Fail("graftui: too many arguments");
        }

        void SetTheme(string? name)
        {
            // A second --theme is an error whatever the first one held.
            var repeated = themeSeen;
            themeSeen = true;
            if (repeated)
            {
                Fail("graftui: --theme given more than once");
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                Fail(NeedsName);
                return;
            }

            if (!Themes.TryGet(name, out var t))
            {
                Fail($"graftui: unknown theme '{name}'; choose one of: {ThemeList}");
                return;
            }

            theme = t.Name;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (pathsOnly)
            {
                TakePath(arg);
                continue;
            }

            if (arg.StartsWith("--theme=", StringComparison.Ordinal))
            {
                SetTheme(arg["--theme=".Length..]);
                continue;
            }

            switch (arg)
            {
                case "--":
                    pathsOnly = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--version":
                    version = true;
                    break;
                case "--sandbox":
                    sandbox = true;
                    break;
                case "--theme":
                    // The name is the next argument unless that is another option, in which case it is missing.
                    if (i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                    {
                        i++;
                        SetTheme(args[i]);
                    }
                    else
                    {
                        SetTheme(null);
                    }
                    break;
                default:
                    if (arg.StartsWith('-'))
                        Fail($"graftui: unknown option '{arg}'");
                    else
                        TakePath(arg);
                    break;
            }
        }

        if (help || version)
            return new CliParse(new CliOptions(help, version, sandbox, theme, path), null);

        if (error is null && sandbox && path is not null)
            error = "graftui: --sandbox opens ~/.graftui/sandbox; it can't be combined with a path";

        return error is null
            ? new CliParse(new CliOptions(false, false, sandbox, theme, path), null)
            : new CliParse(null, error);
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

    private static string ThemeList => string.Join(", ", Themes.Names);

    private static string NeedsName => $"graftui: --theme needs a name: {ThemeList}";
}
