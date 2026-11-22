namespace graftui;

/// <summary>
/// The parsed command line. Help and Version are acted on by Program in that order; neither needs a repo.
/// MakeEdits is the hidden developer flag (see CONTRIBUTING.md); it is left out of HelpText on purpose.
/// </summary>
internal sealed record CliOptions(bool Help, bool Version, bool Sandbox, bool MakeEdits, string? Theme, string? RepoPath, bool NewUi = false, bool BasicGlyphs = false);
