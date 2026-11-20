using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The detail block under the branch list (the new look): the selected branch's new commits and the files they change. The files come from
/// one git diff per selected branch, cached by its merge base and tip for the snapshot (ClearCache empties the cache). A read whose selection
/// or snapshot has gone is cancelled, and its answer is dropped. While the marks are blank (an operation has started, or the snapshot is being
/// read again) the block is blank too, so no row shows what the operation may have changed. UI thread only, except the worker's read.
/// </summary>
internal sealed partial class BranchesTab
{
    // The files of each (merge base, tip) read in this snapshot. A null value means the read failed. ClearCache empties it.
    private readonly Dictionary<(string Base, string Tip), IncomingFiles?> _files = new();

    // The key of the files read running now, or null when none runs.
    private (string Base, string Tip)? _filesKey;

    // Stops the running files read. Made per read, and disposed when the read ends or is cancelled.
    private CancellationTokenSource? _filesCts;

    // The number of the newest files read. An answer with an older number is dropped.
    private int _filesSeq;

    /// <summary>The number of the newest files read. The tests read it.</summary>
    internal int IncomingFilesSeq => _filesSeq;

    /// <summary>
    /// Sets the detail block for the selected branch from its cached layout and files, and starts the files read when they are needed and not
    /// known yet. Null for no branches or a pause. Blank while the marks are blank. The new look only. UI thread only.
    /// </summary>
    internal void UpdateDetail()
    {
        if (!_newUi)
            return;

        var snap = _app.Snapshot;
        if (_app.IsPaused || snap.Branches.Count == 0 || _selected >= snap.Branches.Count)
        {
            CancelFiles();
            SetDetail(null);
            return;
        }

        // The marks are blank: the snapshot on screen is being replaced, so the block shows nothing of it until the marks come back.
        if (_marksKey is null)
        {
            CancelFiles();
            SetDetail(BlankDetail());
            return;
        }

        var b = snap.Branches[_selected];
        var layout = DetailLayout(snap, b);
        var baseHash = layout?.MergeBase;
        if (layout is null || baseHash is null || !NeedsFiles(layout))
        {
            CancelFiles();
            SetDetail(DetailOf(b, layout, null, IncomingFilesState.NotNeeded));
            return;
        }

        var key = (baseHash, layout.SelectedTip);
        if (_files.TryGetValue(key, out var files))
        {
            CancelFiles();
            var state = files is null ? IncomingFilesState.Failed : IncomingFilesState.Read;
            SetDetail(DetailOf(b, layout, files, state));
            return;
        }

        if (FilesReadingFor(key))
        {
            SetDetail(DetailOf(b, layout, null, IncomingFilesState.Reading));
            return;
        }

        StartFilesRead(key, b, layout);
    }

    /// <summary>
    /// A files read finished (a null <paramref name="files"/> means it failed). An answer that is no longer the newest read is dropped. Otherwise
    /// it is cached and the block is set again. UI thread only.
    /// </summary>
    internal void FilesLoaded(int seq, (string Base, string Tip) key, IncomingFiles? files)
    {
        if (seq != _filesSeq)
            return;

        _files[key] = files;
        _filesKey = null;
        Cancellation.CancelAndDispose(ref _filesCts);
        UpdateDetail();
    }

    /// <summary>
    /// Files are needed when HEAD is attached, the branch has a merge base and new commits, and it is ahead of the current branch (a
    /// fast-forward or a diverged branch). The current branch, the same commit, up to date and unrelated branches need none.
    /// </summary>
    internal static bool NeedsFiles(GraphLayout layout) =>
        !layout.Detached && layout.MergeBase is not null && layout.Ahead > 0
        && (layout.Kind == GraphKind.FastForward || layout.Kind == GraphKind.Diverged);

    // Starts the files read for a key on a worker. Any read for another key is stopped first: its answer is for a selection that has moved on.
    private void StartFilesRead((string Base, string Tip) key, Branch b, GraphLayout layout)
    {
        CancelFiles();
        _filesCts = new CancellationTokenSource();
        var ct = _filesCts.Token;
        var seq = ++_filesSeq;
        _filesKey = key;
        var reader = _app.Reader;

        Task.Run(() =>
        {
            try
            {
                var files = reader.LoadIncomingFiles(key.Base, key.Tip, ct);
                _app.Ui(() => FilesLoaded(seq, key, files));
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer selection or snapshot: nothing to show
            }
            catch (Exception)
            {
                // Not logged: the read is not a git command the Commands pane shows. The block gives the summary only.
                _app.Ui(() => FilesLoaded(seq, key, null));
            }
        }, CancellationToken.None);

        SetDetail(DetailOf(b, layout, null, IncomingFilesState.Reading));
    }

    // Stops the running files read, if one runs. Its answer, if it still comes, is dropped: the number moves on.
    private void CancelFiles()
    {
        if (!_filesKey.HasValue)
            return;

        Cancellation.CancelAndDispose(ref _filesCts);
        _filesKey = null;
        _filesSeq++;
    }

    private bool FilesReadingFor((string Base, string Tip) key) => _filesKey.HasValue && _filesKey.Value.Equals(key);

    /// <summary>
    /// The layout the block is drawn from: the cached preview layout, or the shown layout when it is for this snapshot. The cache is emptied by
    /// every snapshot, but a preview that still matches the snapshot needs no read again.
    /// </summary>
    private GraphLayout? DetailLayout(RepoSnapshot snap, Branch b)
    {
        var cached = CachedLayout(snap, b);
        if (cached is not null)
            return cached;

        var shown = _shown;
        if (shown is not null && ShowsSnapshot(shown, snap))
            return shown;
        return null;
    }

    private void SetDetail(BranchDetailInput? detail)
    {
        _app.BranchList.Detail = detail;
        _app.BranchList.SetNeedsDraw();
    }

    private BranchDetailInput DetailOf(Branch b, GraphLayout? layout, IncomingFiles? files, IncomingFilesState state) =>
        new(b.Name, _app.InkOf(b.Name), b.IsCurrent, _app.Snapshot.CurrentBranch.Length == 0, layout, files, state);

    // The block while the marks are blank: its rows are kept (so the list does not change height), and they say "reading…".
    private static BranchDetailInput BlankDetail() =>
        new("", Ink.Fg, false, false, null, null, IncomingFilesState.NotNeeded);
}
