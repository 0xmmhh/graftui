using System.Reflection;
using graftui.Ui.Views;

namespace graftui;

/// <summary>
/// The parsed command line. Help and Version are acted on by Program in that order; neither needs a repo.
/// MakeEdits is the hidden developer flag (see CONTRIBUTING.md); it is left out of HelpText on purpose.
/// </summary>
internal sealed record CliOptions(bool Help, bool Version, bool Sandbox, bool MakeEdits, string? Theme, string? RepoPath);

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

Branches: Tab Changes · ↑/↓ select · m merge · s switch · n new · R rename · D delete · r rebase · u undo
Changes: Space stage/unstage · a stage all · U unstage all · c commit · A amend · x discard · z stash · Z pop · u undo · v all commands · Esc quit

""";

    /// <summary>
    /// Reads the arguments left to right. Help beats Version, and both beat any error. Otherwise the first error wins.
    /// `--` ends the options; anything after it is a path. `--theme` takes the next argument, or `--theme=name`.
    /// </summary>
    public static CliParse Parse(IReadOnlyList<string> args)
    {
        var p = new Parser();
        var queue = new Queue<string>(args);
        while (queue.TryDequeue(out var arg))
            p.Read(arg, queue);
        return p.Result();
    }

    /// <summary>The parse state. Every error is kept in the order found; the first one is reported.</summary>
    private sealed class Parser
    {
        private readonly List<string> _errors = [];
        private bool _help, _version, _sandbox, _makeEdits, _themeSeen, _pathsOnly;
        private string? _theme, _path;

        public void Read(string arg, Queue<string> rest)
        {
            if (_pathsOnly)
            {
                TakePath(arg);
                return;
            }
            if (arg.StartsWith("--theme=", StringComparison.Ordinal))
            {
                SetTheme(arg["--theme=".Length..]);
                return;
            }

            switch (arg)
            {
                case "--":
                    _pathsOnly = true;
                    break;
                case "--help" or "-h":
                    _help = true;
                    break;
                case "--version":
                    _version = true;
                    break;
                case "--sandbox":
                    _sandbox = true;
                    break;
                // Hidden on purpose: a developer flag, documented in CONTRIBUTING.md and not in --help.
                case "--make-edits":
                    _makeEdits = true;
                    break;
                case "--theme":
                    // The name is the next argument unless that is another option, in which case it is missing.
                    SetTheme(rest.TryPeek(out var next) && !next.StartsWith('-') ? rest.Dequeue() : null);
                    break;
                default:
                    if (arg.StartsWith('-'))
                        _errors.Add($"graftui: unknown option '{arg}'");
                    else
                        TakePath(arg);
                    break;
            }
        }

        public CliParse Result()
        {
            if (_help || _version)
                return new CliParse(new CliOptions(_help, _version, _sandbox, _makeEdits, _theme, _path), null);

            if (_sandbox && _path is not null)
                _errors.Add("graftui: --sandbox opens ~/.graftui/sandbox; it can't be combined with a path");

            // Both rebuild or change the practice repo, so they are run one at a time.
            if (_sandbox && _makeEdits)
                _errors.Add("graftui: --make-edits can't be combined with --sandbox; run them one after the other");

            return _errors.Count == 0
                ? new CliParse(new CliOptions(false, false, _sandbox, _makeEdits, _theme, _path), null)
                : new CliParse(null, _errors[0]);
        }

        private void TakePath(string arg)
        {
            // "" is no folder: it must not reach Path.GetFullPath, which throws on it.
            if (arg.Length == 0)
                _errors.Add("graftui: empty path");
            else if (_path is null)
                _path = arg;
            else
                _errors.Add("graftui: too many arguments");
        }

        private void SetTheme(string? name)
        {
            // A second --theme is an error whatever the first one held.
            if (_themeSeen)
            {
                _errors.Add("graftui: --theme given more than once");
                return;
            }
            _themeSeen = true;

            if (string.IsNullOrEmpty(name))
                _errors.Add(NeedsName);
            else if (Themes.TryGet(name, out var t))
                _theme = t.Name;
            else
                _errors.Add($"graftui: unknown theme '{name}'; choose one of: {ThemeList}");
        }
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
