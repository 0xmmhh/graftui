using System.Reflection;
using graftui.Ui.Views;

namespace graftui;

/// <summary>
/// The parsed command line. Help and Version are acted on by Program in that order; neither needs a repo.
/// MakeEdits is the hidden developer flag (see CONTRIBUTING.md); it is left out of HelpText on purpose.
/// </summary>
internal sealed record CliOptions(bool Help, bool Version, bool Sandbox, bool MakeEdits, string? Theme, string? RepoPath, bool NewUi = false, bool BasicGlyphs = false);

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
  --new-ui         the new look, still being built (same as GRAFTUI_UI=new)
  --glyphs <set>   basic (plain ASCII) or unicode (default); also GRAFTUI_GLYPHS
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
    public static CliParse Parse(IReadOnlyList<string> args, string? glyphsEnvironment)
    {
        var p = new Parser(glyphsEnvironment);
        var queue = new Queue<string>(args);
        while (queue.TryDequeue(out var arg))
            p.Read(arg, queue);
        return p.Result();
    }

    /// <summary>The parse state. Every error is kept in the order found; the first one is reported.</summary>
    private sealed class Parser
    {
        private readonly List<string> _errors = [];
        private readonly string? _glyphsEnvironment;
        private bool _help, _version, _sandbox, _makeEdits, _newUi, _themeSeen, _pathsOnly, _glyphsSeen, _basicGlyphs;
        private string? _theme, _path;

        public Parser(string? glyphsEnvironment)
        {
            _glyphsEnvironment = glyphsEnvironment;
        }

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
            if (arg.StartsWith("--glyphs=", StringComparison.Ordinal))
            {
                SetGlyphs(arg["--glyphs=".Length..]);
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
                case "--new-ui":
                    _newUi = true;
                    break;
                // Hidden on purpose: a developer flag, documented in CONTRIBUTING.md and not in --help.
                case "--make-edits":
                    _makeEdits = true;
                    break;
                case "--theme":
                    // The name is the next argument unless that is another option, in which case it is missing.
                    SetTheme(rest.TryPeek(out var next) && !next.StartsWith('-') ? rest.Dequeue() : null);
                    break;
                case "--glyphs":
                    // The same rule as --theme: the set is the next argument unless that is another option.
                    SetGlyphs(rest.TryPeek(out var glyphName) && !glyphName.StartsWith('-') ? rest.Dequeue() : null);
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
                return new CliParse(new CliOptions(_help, _version, _sandbox, _makeEdits, _theme, _path, _newUi, _basicGlyphs), null);

            if (_sandbox && _path is not null)
                _errors.Add("graftui: --sandbox opens ~/.graftui/sandbox; it can't be combined with a path");

            // Both rebuild or change the practice repo, so they are run one at a time.
            if (_sandbox && _makeEdits)
                _errors.Add("graftui: --make-edits can't be combined with --sandbox; run them one after the other");

            // The environment is read only when no --glyphs flag was given: the flag wins over it.
            if (!_glyphsSeen && !string.IsNullOrEmpty(_glyphsEnvironment))
            {
                if (TryGlyphSet(_glyphsEnvironment, out var fromEnvironment))
                    _basicGlyphs = fromEnvironment;
                else
                    _errors.Add($"graftui: GRAFTUI_GLYPHS is '{_glyphsEnvironment}'; choose basic or unicode");
            }

            return _errors.Count == 0
                ? new CliParse(new CliOptions(false, false, _sandbox, _makeEdits, _theme, _path, _newUi, _basicGlyphs), null)
                : new CliParse(null, _errors[0]);
        }

        private void SetGlyphs(string? name)
        {
            // A second --glyphs is an error whatever the first one held, as for --theme.
            if (_glyphsSeen)
            {
                _errors.Add("graftui: --glyphs given more than once");
                return;
            }
            _glyphsSeen = true;

            if (string.IsNullOrEmpty(name))
                _errors.Add(GlyphsNeedName);
            else if (TryGlyphSet(name, out var basic))
                _basicGlyphs = basic;
            else
                _errors.Add($"graftui: unknown glyph set '{name}'; choose basic or unicode");
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

    /// <summary>
    /// Whether the new look is on: the --new-ui flag, or GRAFTUI_UI=new in the environment. The environment value is passed in, so this
    /// stays pure. Any other environment value leaves the new look off.
    /// </summary>
    public static bool NewUiOn(bool flag, string? environmentValue) =>
        flag || string.Equals(environmentValue, "new", StringComparison.OrdinalIgnoreCase);

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

    private const string GlyphsNeedName = "graftui: --glyphs needs a name: basic or unicode";

    /// <summary>
    /// The glyph set a name picks: basic gives true, unicode false. Any other name gives false from the method and is not a set.
    /// Case does not matter, as for theme names.
    /// </summary>
    private static bool TryGlyphSet(string name, out bool basic)
    {
        basic = false;
        if (string.Equals(name, "basic", StringComparison.OrdinalIgnoreCase))
        {
            basic = true;
            return true;
        }
        return string.Equals(name, "unicode", StringComparison.OrdinalIgnoreCase);
    }
}
