using graftui.Git;

namespace graftui.Model;

/// <summary>What `m` would do. Unavailable means "say nothing" (old git, uncertain config, git failed).</summary>
public enum ForecastKind { Clean, FastForward, UpToDate, Conflict, Refused, Unavailable }

/// <summary>
/// Why a file would conflict. Content: both sides changed the text. BothAdded: no stage 1 (added on both sides).
/// DeletedCurrent: stage 2 is missing (deleted on the current branch). DeletedSelected: stage 3 is missing (deleted on the selected branch).
/// Binary: `CONFLICT (binary)`. Rename: any `CONFLICT (rename/...)`. Other: anything else (file/directory, submodule, mode...).
/// </summary>
public enum ForecastConflict { Content, BothAdded, DeletedCurrent, DeletedSelected, Binary, Rename, Other }

public sealed record ForecastFile(string Path, ForecastConflict Kind);

/// <summary>
/// The forecast of one merge. <see cref="Note"/> is a short reason for a Clean merge that is a merge commit only because of config, or
/// for a Refused merge (one of the <c>Note</c> constants below), else null. <see cref="Commands"/> holds the git calls the forecast made:
/// they are not logged in the Commands pane.
/// </summary>
public sealed record MergeForecast(ForecastKind Kind, IReadOnlyList<ForecastFile> Files, string? Note,
    IReadOnlyList<GitResult> Commands)
{
    /// <summary>A Clean merge that is a merge commit because merge.ff is false.</summary>
    public const string FfOffNote = "merge.ff=false";

    /// <summary>A refused merge because merge.ff is only: the branches diverged.</summary>
    public const string FfOnlyNote = "merge.ff=only";

    public static MergeForecast Unavailable(IReadOnlyList<GitResult> commands) => new(ForecastKind.Unavailable, [], null, commands);
}
