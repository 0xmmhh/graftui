namespace graftui;

/// <summary>
/// The parsed command line. Help and Version are acted on by Program in that order; neither needs a repo.
/// MakeEdits is the hidden developer flag (see CONTRIBUTING.md); it is left out of HelpText on purpose.
/// Set each property with an object initializer; every value defaults to false or null.
/// </summary>
internal sealed record CliOptions
{
    public bool Help { get; init; }

    public bool Version { get; init; }

    public bool Sandbox { get; init; }

    public bool MakeEdits { get; init; }

    public string? Theme { get; init; }

    public string? RepoPath { get; init; }

    public bool NewUi { get; init; }

    public bool BasicGlyphs { get; init; }

    public bool Calm { get; init; }
}
