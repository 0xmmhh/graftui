using graftui.Ui.Views;

namespace graftui;

/// <summary>Either the options, or the one stderr line for a bad command line (Program adds "Try 'graftui --help'.").</summary>
internal sealed record CliParse(CliOptions? Options, string? Error);

/// <summary>
/// Reads a command line one argument at a time. Every error is kept in the order found; the first one is reported.
/// The argument being read, and the value taken for an option, are kept in fields, so the helper methods take no arguments.
/// Pure: no console, no repo, no palette.
/// </summary>
internal sealed class CliParser
{
    private readonly Queue<string> _queue;
    private readonly string? _glyphsEnvironment;
    private readonly List<string> _errors = [];
    private readonly HashSet<ValueOption> _valuesGiven = [];
    private bool _help, _version, _sandbox, _makeEdits, _newUi, _pathsOnly, _basicGlyphs;
    private string? _theme, _path;

    // The argument being read, and the value of the option being read (or of GRAFTUI_GLYPHS). Each is set just before a helper uses it.
    private string _arg = string.Empty;
    private string? _value;

    /// <summary>The options that take a value. Each one may be given only once.</summary>
    private enum ValueOption
    {
        Theme,
        Glyphs,
    }

    public CliParser(IReadOnlyList<string> args, string? glyphsEnvironment)
    {
        _queue = new Queue<string>(args);
        _glyphsEnvironment = glyphsEnvironment;
    }

    /// <summary>Reads every argument, then checks the combinations and the environment, and returns the result.</summary>
    public CliParse ReadAll()
    {
        while (_queue.Count > 0)
            ReadNext();
        return Result();
    }

    // Takes the next argument off the queue and reads it. A path is the last resort.
    private void ReadNext()
    {
        _arg = _queue.Dequeue();
        if (_pathsOnly)
        {
            TakePath();
            return;
        }
        if (TryReadEqualsForm())
            return;
        if (TryReadFlag())
            return;
        if (TryReadValueOption())
            return;
        ReadUnknownOrPath();
    }

    private CliParse Result()
    {
        // Help and version beat any error.
        if (_help)
            return OptionsResult();
        if (_version)
            return OptionsResult();

        CheckCombinations();
        CheckGlyphsEnvironment();

        if (_errors.Count > 0)
            return new CliParse(null, _errors[0]);
        return OptionsResult();
    }

    private CliParse OptionsResult() => new CliParse(BuildOptions(), null);

    private CliOptions BuildOptions() =>
        new CliOptions(_help, _version, _sandbox, _makeEdits, _theme, _path, _newUi, _basicGlyphs);

    // --theme=name and --glyphs=set. Returns false when the argument has neither prefix.
    private bool TryReadEqualsForm()
    {
        if (_arg.StartsWith("--theme=", StringComparison.Ordinal))
        {
            _value = _arg["--theme=".Length..];
            SetTheme();
            return true;
        }
        if (_arg.StartsWith("--glyphs=", StringComparison.Ordinal))
        {
            _value = _arg["--glyphs=".Length..];
            SetGlyphs();
            return true;
        }
        return false;
    }

    // The options that take no value. Returns false when the argument is not one of them.
    private bool TryReadFlag()
    {
        switch (_arg)
        {
            case "--":
                _pathsOnly = true;
                return true;
            case "--help":
            case "-h":
                _help = true;
                return true;
            case "--version":
                _version = true;
                return true;
            case "--sandbox":
                _sandbox = true;
                return true;
            case "--new-ui":
                _newUi = true;
                return true;
            // Hidden on purpose: a developer flag, documented in CONTRIBUTING.md and not in --help.
            case "--make-edits":
                _makeEdits = true;
                return true;
            default:
                return false;
        }
    }

    // --theme and --glyphs with the value as the next argument. Returns false for any other argument.
    private bool TryReadValueOption()
    {
        if (_arg == "--theme")
        {
            // The name is the next argument unless that is another option, in which case it is missing.
            TakeValue();
            SetTheme();
            return true;
        }
        if (_arg == "--glyphs")
        {
            // The same rule as --theme: the set is the next argument unless that is another option.
            TakeValue();
            SetGlyphs();
            return true;
        }
        return false;
    }

    // Sets _value to the next argument when it is a value and not another option, and takes it off the queue. Otherwise _value is null.
    private void TakeValue()
    {
        if (NextIsValue())
            _value = _queue.Dequeue();
        else
            _value = null;
    }

    private bool NextIsValue()
    {
        if (_queue.Count == 0)
            return false;
        return !_queue.Peek().StartsWith('-');
    }

    private void ReadUnknownOrPath()
    {
        if (_arg.StartsWith('-'))
            _errors.Add($"graftui: unknown option '{_arg}'");
        else
            TakePath();
    }

    private void TakePath()
    {
        // "" is no folder: it must not reach Path.GetFullPath, which throws on it.
        if (_arg.Length == 0)
            _errors.Add("graftui: empty path");
        else if (_path is null)
            _path = _arg;
        else
            _errors.Add("graftui: too many arguments");
    }

    private void CheckCombinations()
    {
        if (_sandbox)
        {
            if (_path is not null)
                _errors.Add("graftui: --sandbox opens ~/.graftui/sandbox; it can't be combined with a path");

            // Both rebuild or change the practice repo, so they are run one at a time.
            if (_makeEdits)
                _errors.Add("graftui: --make-edits can't be combined with --sandbox; run them one after the other");
        }
    }

    private void CheckGlyphsEnvironment()
    {
        // The environment is read only when no --glyphs flag was given: the flag wins over it.
        if (_valuesGiven.Contains(ValueOption.Glyphs))
            return;
        if (string.IsNullOrEmpty(_glyphsEnvironment))
            return;

        _value = _glyphsEnvironment;
        if (!TrySetGlyphSetFromValue())
            _errors.Add($"graftui: GRAFTUI_GLYPHS is '{_glyphsEnvironment}'; choose basic or unicode");
    }

    private void SetGlyphs()
    {
        if (!IsFirstTime(ValueOption.Glyphs))
            return;
        if (!HasValue(ValueOption.Glyphs))
            return;

        if (TrySetGlyphSetFromValue())
            return;
        _errors.Add($"graftui: unknown glyph set '{_value}'; choose basic or unicode");
    }

    // Sets basic or unicode from _value, in any case. Returns false, and changes nothing, for any other name.
    private bool TrySetGlyphSetFromValue()
    {
        if (string.Equals(_value, "basic", StringComparison.OrdinalIgnoreCase))
        {
            _basicGlyphs = true;
            return true;
        }
        if (string.Equals(_value, "unicode", StringComparison.OrdinalIgnoreCase))
        {
            _basicGlyphs = false;
            return true;
        }
        return false;
    }

    private void SetTheme()
    {
        if (!IsFirstTime(ValueOption.Theme))
            return;
        if (!HasValue(ValueOption.Theme))
            return;

        if (Themes.TryGet(_value ?? string.Empty, out var t))
            _theme = t.Name;
        else
            _errors.Add($"graftui: unknown theme '{_value}'; choose one of: {ThemeList}");
    }

    // The first time an option is given this is true. A second time adds its error and this is false.
    // A second --glyphs is an error whatever the first one held, as for --theme.
    private bool IsFirstTime(ValueOption option)
    {
        if (_valuesGiven.Add(option))
            return true;
        _errors.Add($"graftui: {FlagName(option)} given more than once");
        return false;
    }

    // True when the option has a value. An empty or missing value adds that option's "needs a name" error.
    private bool HasValue(ValueOption option)
    {
        if (!string.IsNullOrEmpty(_value))
            return true;
        _errors.Add(NeedsMessage(option));
        return false;
    }

    private static string FlagName(ValueOption option) => option == ValueOption.Glyphs ? "--glyphs" : "--theme";

    private static string NeedsMessage(ValueOption option) => option == ValueOption.Glyphs ? GlyphsNeedName : NeedsName;

    private static string ThemeList => string.Join(", ", Themes.Names);

    private static string NeedsName => $"graftui: --theme needs a name: {ThemeList}";

    private const string GlyphsNeedName = "graftui: --glyphs needs a name: basic or unicode";
}
