namespace graftui.Ui.Scenes;

/// <summary>
/// One change's animation on the three-areas art: held at its hold point until git answers, then played out to its final frame.
/// <see cref="ChangesAnimation"/> (stage, unstage, commit, amend) and <see cref="StashAnimation"/> (discard, stash, pop, undo)
/// both implement it, so the Changes tab drives them the same way.
/// </summary>
public interface IAreasAnimation
{
    /// <summary>How long the scene is held at its hold point while git runs.</summary>
    long HoldMs { get; }

    /// <summary>When the scene is finished, measured from the hold point.</summary>
    long TotalMs { get; }

    /// <summary>git's answer, once it has arrived.</summary>
    AreasResult? Result { get; }

    /// <summary>Records git's answer. <paramref name="elapsedMs"/> is the time on the animation clock.</summary>
    void SetResult(AreasResult r, long elapsedMs);

    /// <summary>The frame the animation ends on. Only meaningful once <see cref="IsDone"/> is true.</summary>
    SceneFrame Final(ArtGeometry g);

    /// <summary>True once the scene has played out after the result arrived.</summary>
    bool IsDone(long elapsedMs);

    /// <summary>Running titles while the change is in flight; the result's title once it is done.</summary>
    string Title(long elapsedMs);

    /// <summary>The frame at this time on the animation clock.</summary>
    SceneFrame Render(ArtGeometry g, long elapsedMs);
}
