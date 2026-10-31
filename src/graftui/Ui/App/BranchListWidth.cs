namespace graftui.Ui.App;

/// <summary>
/// The width of the branch list's column, which sets the art's width. A switch's last frame holds it: the list's wanted width (from the
/// new counts) waits until <see cref="Release"/>, which the app calls when the selection moves, a change starts, the tab changes, undo
/// runs, or the screen is resized. While nothing is held, an offer takes effect at once.
/// </summary>
internal sealed class BranchListWidth(int initial)
{
    private int? _pending;

    /// <summary>The width the layout uses.</summary>
    internal int Width { get; private set; } = initial;

    /// <summary>True while the width is held.</summary>
    internal bool Held { get; private set; }

    /// <summary>The list's wanted width: used now, or kept until the hold is released.</summary>
    internal void Offer(int wanted)
    {
        if (Held)
            _pending = wanted;
        else
            Width = wanted;
    }

    /// <summary>Holds the width: offers wait until <see cref="Release"/>.</summary>
    internal void Hold() => Held = true;

    /// <summary>Ends the hold and applies the wanted width that waited. Returns true when the width changed (the layout must run again).</summary>
    internal bool Release()
    {
        if (!Held)
            return false;
        Held = false;
        if (_pending is not { } wanted)
            return false;
        _pending = null;
        if (wanted == Width)
            return false;
        Width = wanted;
        return true;
    }
}
