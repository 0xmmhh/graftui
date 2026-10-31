namespace graftui.Ui.App;

/// <summary>Cancellation helpers for the reads the tabs start on worker threads.</summary>
internal static class Cancellation
{
    /// <summary>
    /// Cancels a read still in flight and disposes its source. The worker keeps its token, which stays valid after disposal;
    /// it only reads IsCancellationRequested.
    /// </summary>
    internal static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
