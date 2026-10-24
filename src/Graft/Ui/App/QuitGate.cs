namespace Graft.Ui.App;

/// <summary>
/// Makes the 'is graft quitting?' check and MarkQuitting mutually exclusive, nothing more. It does not hold a merge back:
/// a merge whose check passed just before MarkQuitting still runs, and Program waits for it and reports it.
/// </summary>
internal sealed class QuitGate
{
    private readonly object _lock = new();
    private bool _quitting;

    /// <summary>Marks graft as quitting. Takes the same lock as <see cref="IsQuitting"/>.</summary>
    public void MarkQuitting()
    {
        lock (_lock)
            _quitting = true;
    }

    public bool IsQuitting
    {
        get
        {
            lock (_lock)
                return _quitting;
        }
    }
}
