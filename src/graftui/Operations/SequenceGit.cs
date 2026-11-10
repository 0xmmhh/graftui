using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// The git helpers that the rebase and the cherry-pick share. Every git call goes through <see cref="BranchGit.Call"/>, so it is logged.
/// The reads throw when git fails; the operations turn the throw into a refusal or a failure.
/// </summary>
internal static class SequenceGit
{
    /// <summary>A path as one shell word: single quotes, with each quote in the path written as '\''.</summary>
    public static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>The paths git lists as unmerged (conflicted). Throws when git fails.</summary>
    public static IReadOnlyList<string> UnmergedFiles(GitClient git, List<GitResult> log)
    {
        var r = BranchGit.Call(git, log, "diff", "--name-only", "--diff-filter=U", "-z");
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));
        return r.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// True when the file still holds a conflict marker line: a line that starts with exactly seven <c>&lt;</c> or seven <c>&gt;</c>, followed
    /// by a space or by the end of the line. A lone <c>=======</c> is not a marker: it is also a line that real text can hold. A missing
    /// file has none.
    /// </summary>
    public static bool HasMarkers(string fullPath)
    {
        if (!File.Exists(fullPath))
            return false;

        foreach (var line in File.ReadLines(fullPath))
        {
            if (IsMarkerLine(line.TrimEnd('\r')))
                return true;
        }
        return false;
    }

    /// <summary>True when the line is a marker: seven '&lt;' or seven '&gt;', then a space or the end of the line.</summary>
    private static bool IsMarkerLine(string line)
    {
        if (!line.StartsWith("<<<<<<<", StringComparison.Ordinal) && !line.StartsWith(">>>>>>>", StringComparison.Ordinal))
            return false;
        return line.Length == 7 || line[7] == ' ';
    }

    /// <summary>
    /// The conflicted files, from git's index (<c>git ls-files -u -z</c>): every path with an unmerged entry, in git's order. A text file is
    /// resolved once its work-tree file has no marker line. The other kinds are never resolved by markers. Throws when git fails.
    /// </summary>
    internal static IReadOnlyList<ConflictFile> ReadConflicts(GitClient git, List<GitResult> log)
    {
        var r = BranchGit.Call(git, log, "ls-files", "-u", "-z");
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));

        var entries = new List<UnmergedEntry>();
        var byPath = new Dictionary<string, UnmergedEntry>(StringComparer.Ordinal);
        foreach (var record in r.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // Each record is "<mode> <sha> <stage>", a tab, then the path.
            var tab = record.IndexOf('\t');
            if (tab < 0)
                throw new InvalidOperationException("git ls-files printed a line without a tab: " + record);
            var meta = record[..tab].Split(' ');
            var name = record[(tab + 1)..];

            if (!byPath.TryGetValue(name, out var entry))
            {
                entry = new UnmergedEntry(name);
                byPath[name] = entry;
                entries.Add(entry);
            }
            if (meta[2] == "2")
                entry.Ours = meta[1];
            else if (meta[2] == "3")
                entry.Theirs = meta[1];
            if (meta[0] == "120000" || meta[0] == "160000")
                entry.Link = true;
        }

        var files = new List<ConflictFile>(entries.Count);
        foreach (var entry in entries)
        {
            var kind = KindOf(git, log, entry);
            var resolved = kind == ConflictKind.Text && !HasMarkers(Path.Combine(git.RepoPath, entry.Name));
            files.Add(new ConflictFile(entry.Name, resolved, kind, entry.Ours, entry.Theirs));
        }
        return files;
    }

    /// <summary>
    /// What kind of conflict a path is. A missing side is a delete. A link or a submodule is binary. Otherwise git's numstat decides:
    /// it prints "-" for a binary pair.
    /// </summary>
    private static ConflictKind KindOf(GitClient git, List<GitResult> log, UnmergedEntry entry)
    {
        if (entry.Ours.Length == 0)
            return ConflictKind.DeletedOurs;
        if (entry.Theirs.Length == 0)
            return ConflictKind.DeletedTheirs;
        if (entry.Link)
            return ConflictKind.Binary;
        return IsBinaryPair(git, log, entry.Name) ? ConflictKind.Binary : ConflictKind.Text;
    }

    /// <summary>
    /// True when git's diff of the two sides of a path is binary. The sides are named as <c>:2:path</c> and <c>:3:path</c>, so the path's
    /// .gitattributes apply. Throws when git fails.
    /// </summary>
    private static bool IsBinaryPair(GitClient git, List<GitResult> log, string path)
    {
        var r = BranchGit.Call(git, log, "diff", "--numstat", "-z", "--no-textconv", ":2:" + path, ":3:" + path);
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));
        return r.StdOut.StartsWith("-\t-\t", StringComparison.Ordinal);
    }

    /// <summary>
    /// The same files with each text file's resolved state read again from the work tree. The other kinds keep theirs. No git runs here,
    /// so the pause screen's poll can use it.
    /// </summary>
    public static IReadOnlyList<ConflictFile> Recheck(string repoPath, IReadOnlyList<ConflictFile> files)
    {
        var result = new List<ConflictFile>(files.Count);
        foreach (var f in files)
        {
            if (f.Kind == ConflictKind.Text)
                result.Add(f with { Resolved = !HasMarkers(Path.Combine(repoPath, f.Path)) });
            else
                result.Add(f);
        }
        return result;
    }

    /// <summary>
    /// Which version the work tree holds for a conflicted file: the stage 2 blob, the stage 3 blob, an edited file, or no file. The hash is
    /// git's own (hash-object, with the clean filters git would apply when it stages). Throws when git fails.
    /// </summary>
    internal static TreeSide WorkTreeSide(GitClient git, List<GitResult> log, ConflictFile f)
    {
        if (!InWorkTree(Path.Combine(git.RepoPath, f.Path)))
            return TreeSide.Deleted;

        var r = BranchGit.Call(git, log, "hash-object", "--", f.Path);
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));

        var hash = r.StdOut.Trim();
        if (hash == f.Ours)
            return TreeSide.Ours;
        if (hash == f.Theirs)
            return TreeSide.Theirs;
        return TreeSide.Edited;
    }

    /// <summary>True when the work tree holds something at the path: a file, a directory (a submodule) or a link, even a dangling one.</summary>
    internal static bool InWorkTree(string fullPath)
    {
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            return true;
        return new FileInfo(fullPath).LinkTarget != null;
    }

    /// <summary>
    /// One conflicted path while its unmerged entries are read: the stage 2 and stage 3 blobs, and whether git lists a link or a submodule.
    /// </summary>
    private sealed class UnmergedEntry(string name)
    {
        public string Name { get; } = name;

        public string Ours { get; set; } = "";

        public string Theirs { get; set; } = "";

        public bool Link { get; set; }
    }

    /// <summary>
    /// The paths of every change the status lists (staged, unstaged and untracked, a rename's original too), each once, in git's order.
    /// Returns git's failure text when status cannot be read, and null when it can (paths then holds the list).
    /// </summary>
    public static string? Dirty(GitClient git, List<GitResult> log, out IReadOnlyList<string> paths)
    {
        var r = BranchGit.Call(git, log, BranchGit.StatusArgs);
        if (!r.Success)
        {
            paths = [];
            return GitClient.Describe(r);
        }
        paths = BranchGit.Paths(StatusParser.Parse(r.StdOut)).Distinct().ToList();
        return null;
    }

    /// <summary>The full message of a commit, without the trailing newline. Throws when git fails.</summary>
    public static string FullMessage(GitClient git, List<GitResult> log, string hash)
    {
        var r = BranchGit.Call(git, log, "log", "-1", "--no-show-signature", "--format=%B", hash, "--");
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));
        return r.StdOut.TrimEnd();
    }

    /// <summary>
    /// Where each original commit went. Equal counts pair up by position. Otherwise each original takes the first unused new commit
    /// with the same subject, and an original with no match gets New = null (it was dropped, folded or skipped).
    /// </summary>
    public static IReadOnlyList<CommitMove> MapMoves(IReadOnlyList<(string Hash, string Subject)> oldKept, IReadOnlyList<(string Hash, string Subject)> newCommits)
    {
        if (oldKept.Count == newCommits.Count)
            return oldKept.Select((o, i) => new CommitMove(o.Hash, newCommits[i].Hash)).ToList();

        var used = new bool[newCommits.Count];
        var moves = new List<CommitMove>(oldKept.Count);
        foreach (var o in oldKept)
        {
            var match = -1;
            for (var j = 0; j < newCommits.Count && match < 0; j++)
            {
                if (!used[j] && newCommits[j].Subject == o.Subject)
                    match = j;
            }
            if (match >= 0)
                used[match] = true;
            moves.Add(new CommitMove(o.Hash, match >= 0 ? newCommits[match].Hash : null));
        }
        return moves;
    }

    /// <summary>
    /// The undo step for a branch that moved from the state's old tip to <paramref name="newTip"/>. Null when nothing moved, and when the
    /// save fails: then Problem says why, and the caller adds it to its message. The step is an ordinary merge-kind step, so the CLI can undo it.
    /// </summary>
    public static (UndoRecord? Undo, string? Problem) SaveUndo(GitClient git, List<GitResult> log, SequenceState s, string newTip, string what)
    {
        if (newTip == s.OldTip)
            return (null, null);

        var record = new UndoRecord(UndoKind.Merge, what, [new RefChange(BranchGit.HeadsPrefix + s.Branch, s.OldTip, newTip)],
            Branch: s.Branch, Head: newTip);
        var problem = new UndoLog(git).Push(record, log);
        return problem is null ? (record, null) : (null, problem);
    }

    /// <summary>
    /// What a paused rebase waits for, read from git's rebase-merge folder and the state. Null when git cannot say: the done list is
    /// missing (a rebase started with a todo git rejected) or git fails. Otherwise a conflict, an edit stop, a reword pause (an edit stop
    /// whose row is a reword that has not been reworded yet) or a squash pause (a break after a fold group). Null too when the stop is not
    /// a row of the plan, or a break has no fold group before it: such a stop cannot be placed, so it is unreadable, never a conflict.
    /// </summary>
    internal static SequenceStop? ReadRebaseStop(GitClient git, string gitDir, SequenceState s, List<GitResult> log)
    {
        var files = ReadConflicts(git, log);
        var folder = Path.Combine(gitDir, "rebase-merge");
        var donePath = Path.Combine(folder, "done");
        if (!File.Exists(donePath))
            return null;

        var done = ReadDone(donePath);
        if (done.Count == 0)
            return null;

        var (cmd, hash) = done[^1];
        var stopped = ReadTrimmed(Path.Combine(folder, "stopped-sha"));
        var stopCommit = stopped.Length > 0 ? stopped : hash ?? "";

        if (files.Count > 0)
            return RebaseStop(s, PauseKind.Conflict, FullOrGiven(s, stopCommit), files, "", []);

        if (cmd is "edit" or "e")
        {
            var commit = FullOrGiven(s, hash ?? "");
            var row = s.Rows.FirstOrDefault(r => r.Hash == commit);
            if (row is not null && row.Action == RebaseAction.Reword && !s.Reworded.Contains(row.Hash))
                return RebaseStop(s, PauseKind.Reword, row.Hash, [], FullMessage(git, log, row.Hash), []);
            return RebaseStop(s, PauseKind.Edit, commit, [], "", []);
        }

        // A break is graftui's squash stop. A break with no fold group before it is not a stop graftui can show: it is unreadable, not a conflict.
        if (cmd is "break" or "b")
        {
            if (TryFoldGroup(done, out var target, out var folded))
                return SquashStop(git, log, s, target, folded);
            return null;
        }

        return RebaseStop(s, PauseKind.Conflict, FullOrGiven(s, stopCommit), [], "", []);
    }

    /// <summary>
    /// What a paused cherry-pick waits for: always a conflict, on the commit in CHERRY_PICK_HEAD. The files are read from the work tree.
    /// Null when no commit is held: the sequencer is left between two picks (the current one was committed by hand), and there is no
    /// stop to show. Null too when the held commit is not one of the picks. The sequence is then still in progress, but its stop cannot be read.
    /// </summary>
    internal static SequenceStop? ReadPickStop(GitClient git, SequenceState s, List<GitResult> log)
    {
        var head = BranchGit.Call(git, log, GitArgs.RevParse, "-q", GitArgs.Verify, "CHERRY_PICK_HEAD");
        var commit = head.Success ? head.StdOut.Trim() : "";
        if (commit.Length == 0)
            return null;

        // A commit that is not one of graftui's picks has no place in the sequence, so there is no stop to show (Step is never 0).
        var step = IndexOfHash(s.Picks, commit);
        if (step < 0)
            return null;

        var files = ReadConflicts(git, log);
        return new SequenceStop(SequenceKind.CherryPick, PauseKind.Conflict, s.Branch, s.Onto, commit, BranchGit.Short(commit),
            Subject(git, log, commit), files, step + 1, s.Picks.Count, "", [], Ours: true);
    }

    /// <summary>
    /// The stop at a plan row, numbered from 1 (Step is never 0). Null when the commit is not a row of the plan: graftui cannot show a stop
    /// it cannot place in the plan, so the caller reports the stop as unreadable instead.
    /// </summary>
    private static SequenceStop? RebaseStop(SequenceState s, PauseKind pause, string commit, IReadOnlyList<ConflictFile> files,
        string message, IReadOnlyList<string> folded)
    {
        var index = IndexOfRow(s.Rows, commit);
        if (index < 0)
            return null;

        return new SequenceStop(SequenceKind.Rebase, pause, s.Branch, s.Onto, commit, BranchGit.Short(commit),
            s.Rows[index].Subject, files, index + 1, s.Rows.Count, message, folded, Ours: true, Rows: s.Rows);
    }

    /// <summary>The squash pause for a fold group: the target's message, then each squash row's message. Null when the target is not a row.</summary>
    private static SequenceStop? SquashStop(GitClient git, List<GitResult> log, SequenceState s, string target, IReadOnlyList<string> folded)
    {
        var message = FullMessage(git, log, "HEAD");
        foreach (var hash in folded)
        {
            var row = s.Rows.FirstOrDefault(r => r.Hash == hash);
            if (row is not null && row.Action == RebaseAction.Squash)
                message += "\n\n" + FullMessage(git, log, hash);
        }
        return RebaseStop(s, PauseKind.Squash, target, [], message, folded);
    }

    /// <summary>
    /// Walks the done list back from the break (the last line) to the commit the group folds into. Folded is the group's fixup hashes,
    /// oldest first. A drop between the fold and its target is passed over, since git folds into the last commit it kept. An earlier break
    /// (the end of a fold group before this one) is passed over with the fixups above it, which belong to that group; the walk goes on to
    /// the commit the earlier group folded into. False when the lines do not form a group.
    /// </summary>
    private static bool TryFoldGroup(IReadOnlyList<(string Cmd, string? Hash)> done, out string target, out List<string> folded)
    {
        folded = [];
        target = "";
        var inOwnGroup = true;
        for (var i = done.Count - 2; i >= 0; i--)
        {
            var (cmd, hash) = done[i];
            if (cmd is "break" or "b")
            {
                inOwnGroup = false;
                continue;
            }
            if (cmd is "drop" or "d")
                continue;
            if (cmd is "fixup" or "f")
            {
                if (inOwnGroup)
                    folded.Add(hash ?? "");
                continue;
            }
            if (cmd is "pick" or "p" or "edit" or "e" or "reword" or "r")
            {
                if (hash is null)
                    return false;
                target = hash;
                folded.Reverse();
                return folded.Count > 0 && folded.All(h => h.Length > 0);
            }
            return false;
        }
        return false;
    }

    /// <summary>The non-comment lines of a done list as (command, hash). The hash is null when the line has none.</summary>
    private static List<(string Cmd, string? Hash)> ReadDone(string path)
    {
        var lines = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'));
        var done = new List<(string Cmd, string? Hash)>();
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            done.Add((parts[0], parts.Length > 1 ? parts[1] : null));
        }
        return done;
    }

    /// <summary>The full hash of a row when the given text is a prefix of one, otherwise the text itself.</summary>
    private static string FullOrGiven(SequenceState s, string hash)
    {
        if (hash.Length == 0)
            return hash;
        var row = s.Rows.FirstOrDefault(r => r.Hash.StartsWith(hash, StringComparison.Ordinal));
        return row?.Hash ?? hash;
    }

    private static int IndexOfRow(IReadOnlyList<RebaseRow> rows, string hash)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Hash == hash)
                return i;
        }
        return -1;
    }

    private static int IndexOfHash(IReadOnlyList<string> hashes, string hash)
    {
        for (var i = 0; i < hashes.Count; i++)
        {
            if (hashes[i] == hash)
                return i;
        }
        return -1;
    }

    /// <summary>The subject of a commit, "" when git cannot read it.</summary>
    private static string Subject(GitClient git, List<GitResult> log, string hash)
    {
        var r = BranchGit.Call(git, log, "log", "-1", "--no-show-signature", "--format=%s", hash, "--");
        return r.Success ? r.StdOut.Trim() : "";
    }

    private static string ReadTrimmed(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : "";

    /// <summary>
    /// Classifies where a rebase or a cherry-pick ended after git ran it (Run) or after a continue, skip or abort (Resume): paused at a stop,
    /// done, or failed. Saves or deletes the state file to match. Done saves one undo step when the branch moved.
    /// </summary>
    internal static SequenceOutcome Settle(GitClient git, string gitDir, SequenceState s, GitResult last, List<GitResult> log)
    {
        if (s.Kind == SequenceKind.CherryPick)
            return SettlePick(git, gitDir, s, last, log);

        if (RebaseInProgress(gitDir))
            return SettleRebasePaused(git, gitDir, s, last, log);

        // Git no longer holds the rebase, so its state is over whatever the reads below find. It goes first, so a read that throws
        // cannot leave it behind for the next rebase to mistake for its own.
        SequenceStateFile.Delete(gitDir);
        var symbolic = BranchGit.SymbolicHead(git, log);
        if (symbolic != BranchGit.HeadsPrefix + s.Branch)
            return Failed(s, $"the rebase finished off {s.Branch}; check git status", log);

        var newTip = BranchGit.RefOrEmpty(git, log, "HEAD");
        if (!last.Success && newTip == s.OldTip)
            return Failed(s, BranchGit.FirstLine(last), log);

        var ontoTip = BranchGit.RefOrEmpty(git, log, BranchGit.HeadsPrefix + s.Onto);
        var newCommits = ReadPairs(git, log, ontoTip + ".." + newTip);
        var kept = s.Rows.Where(r => (r.Action is RebaseAction.Pick or RebaseAction.Reword or RebaseAction.Edit) && !s.Skipped.Contains(r.Hash))
            .Select(r => (r.Hash, r.Subject))
            .ToList();
        var mapped = MapMoves(kept, newCommits);
        var moves = s.Rows.Select(r => mapped.FirstOrDefault(m => m.Old == r.Hash) ?? new CommitMove(r.Hash, null)).ToList();

        var (undo, problem) = SaveUndo(git, log, s, newTip, $"rebase of {s.Branch} onto {s.Onto}");
        var message = $"rebased {s.Branch} onto {s.Onto}";
        if (problem is not null)
            message += $"; could not save the undo step: {problem}";
        return Outcome(SequenceOutcomeKind.Done, SequenceKind.Rebase, s.Branch, s.Onto, s.OldTip, message, log,
            newTip: newTip, moves: moves, s: s, undo: undo);
    }

    /// <summary>
    /// The cherry-pick's classifier. While git holds a pick, a pick with no conflict and nothing staged is empty: it is skipped and recorded.
    /// Any other pick is a pause. Each skip uses up one pick, so the loop cannot run past the picks. When no pick is held, the sequence is
    /// done, or failed when git's first pick failed before anything landed.
    /// </summary>
    private static SequenceOutcome SettlePick(GitClient git, string gitDir, SequenceState s, GitResult last, List<GitResult> log)
    {
        for (var pass = 0; PickInProgress(gitDir); pass++)
        {
            if (pass == s.Picks.Count || UnmergedFiles(git, log).Count > 0 || !NothingStaged(git, log))
                return PausedPick(git, gitDir, s, log);

            var empty = BranchGit.RefOrEmpty(git, log, "CHERRY_PICK_HEAD");
            last = BranchGit.Call(git, log, "cherry-pick", "--skip");
            // git can run on to the next pick, and stop there with exit 1. Only a skip that did not move on is a failed skip.
            if (!PickMovedOn(git, log, empty))
                return PausedPick(git, gitDir, s, log, BranchGit.FirstLine(last));

            s.AlreadyApplied.Add(empty);
        }

        // No pick is held any more, so the sequence is over: its state goes before the reads below, as in the rebase.
        SequenceStateFile.Delete(gitDir);
        var newTip = BranchGit.RefOrEmpty(git, log, "HEAD");
        if (!last.Success && newTip == s.OldTip && s.Skipped.Count == 0 && s.AlreadyApplied.Count == 0)
            return Failed(s, BranchGit.FirstLine(last), log);

        var kept = s.Picks.Where(p => !s.Skipped.Contains(p) && !s.AlreadyApplied.Contains(p))
            .Select(p => (Hash: p, Subject: Subject(git, log, p)))
            .ToList();
        var newCommits = ReadPairs(git, log, s.OldTip + ".." + newTip);
        var mapped = MapMoves(kept, newCommits);
        var moves = s.Picks.Select(p => mapped.FirstOrDefault(m => m.Old == p) ?? new CommitMove(p, null)).ToList();

        var what = s.Picks.Count == 1 ? $"cherry-pick of {BranchGit.Short(s.Picks[0])}" : $"cherry-pick of {s.Picks.Count} commits";
        var (undo, problem) = SaveUndo(git, log, s, newTip, what);
        var count = newCommits.Count;
        var message = $"picked {count} {(count == 1 ? "commit" : "commits")} onto {s.Branch}";
        if (problem is not null)
            message += $"; could not save the undo step: {problem}";
        return Outcome(SequenceOutcomeKind.Done, SequenceKind.CherryPick, s.Branch, s.Onto, s.OldTip, message, log,
            newTip: newTip, moves: moves, s: s, undo: undo);
    }

    /// <summary>The cherry-pick stopped: on a conflict, or on an empty pick that could not be skipped. Its state is saved for the resume operation.</summary>
    private static SequenceOutcome PausedPick(GitClient git, string gitDir, SequenceState s, List<GitResult> log, string? note = null)
    {
        var stop = ReadPickStop(git, s, log);
        if (stop is null)
            return Failed(s, "could not read the paused cherry-pick; check git status", log);

        var message = $"cherry-pick paused at {stop.ShortCommit}";
        if (note is not null)
            message += $"; {note}";
        var saved = SequenceStateFile.Save(gitDir, s);
        if (saved is not null)
            message += $"; could not save the cherry-pick state: {saved}";
        return Outcome(SequenceOutcomeKind.Paused, SequenceKind.CherryPick, s.Branch, s.Onto, s.OldTip, message, log, stop: stop, s: s);
    }

    /// <summary>
    /// True while git holds a cherry-pick: CHERRY_PICK_HEAD exists, or the sequencer folder does (more picks wait). It reads the files
    /// that rev-parse reads, so it never throws and never needs a logged call.
    /// </summary>
    internal static bool PickInProgress(string gitDir) =>
        gitDir.Length > 0
        && (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")) || Directory.Exists(Path.Combine(gitDir, "sequencer")));

    /// <summary>True when git no longer waits on <paramref name="commit"/>: it skipped that pick, or it moved on to a later one. Throws when git fails.</summary>
    internal static bool PickMovedOn(GitClient git, List<GitResult> log, string commit) =>
        BranchGit.RefOrEmpty(git, log, "CHERRY_PICK_HEAD") != commit;

    /// <summary>
    /// True when git no longer waits on <paramref name="commit"/> in a rebase: the rebase is over, or it stopped on a different commit
    /// (plan rows have unique hashes, so a different stop means git moved on). A rebase that still waits on it, or whose stop cannot
    /// be read, is not moved on.
    /// </summary>
    internal static bool RebaseMovedOn(GitClient git, string gitDir, SequenceState s, List<GitResult> log, string commit)
    {
        if (!RebaseInProgress(gitDir))
            return true;

        var stop = ReadRebaseStop(git, gitDir, s, log);
        return stop is not null && stop.Commit != commit;
    }

    /// <summary>True when the index matches HEAD (`git diff --cached --quiet` exits 0).</summary>
    private static bool NothingStaged(GitClient git, List<GitResult> log) => BranchGit.Call(git, log, "diff", "--cached", "--quiet").Success;

    /// <summary>The rebase stopped (conflict, edit, reword, squash). Its stop is saved with the state, or the rebase is aborted when git never started it.</summary>
    private static SequenceOutcome SettleRebasePaused(GitClient git, string gitDir, SequenceState s, GitResult last, List<GitResult> log)
    {
        var stop = ReadRebaseStop(git, gitDir, s, log);
        if (stop is null)
        {
            if (File.Exists(Path.Combine(gitDir, "rebase-merge", "done")))
            {
                return Failed(s, "could not read the paused rebase; check git status", log);
            }

            // Git rejected the todo: the rebase is started but has no steps. Abort it so the branch is exactly as before.
            var abort = BranchGit.Call(git, log, "rebase", "--abort");
            if (!RebaseInProgress(gitDir))
                SequenceStateFile.Delete(gitDir);
            var reason = BranchGit.FirstLine(last);
            return Failed(s, abort.Success ? reason : reason + "; the rebase could not be aborted: check git status", log);
        }

        var message = $"rebase paused at {stop.ShortCommit}";
        var saved = SequenceStateFile.Save(gitDir, s);
        if (saved is not null)
            message += $"; could not save the rebase state: {saved}";
        return Outcome(SequenceOutcomeKind.Paused, SequenceKind.Rebase, s.Branch, s.Onto, s.OldTip, message, log,
            stop: stop, s: s);
    }

    /// <summary>A failed outcome for the sequence. Its state must already be deleted or be the one git still holds.</summary>
    internal static SequenceOutcome Failed(SequenceState s, string message, List<GitResult> log) =>
        Outcome(SequenceOutcomeKind.Failed, s.Kind, s.Branch, s.Onto, s.OldTip, message, log, s: s);

    /// <summary>
    /// The outcome of a sequence operation. Lists come from the state when one is given. Commands are copied, so the outcome
    /// does not change when the caller keeps logging.
    /// </summary>
    internal static SequenceOutcome Outcome(SequenceOutcomeKind kind, SequenceKind op, string branch, string onto, string oldTip,
        string message, List<GitResult> log, string? newTip = null, SequenceStop? stop = null,
        IReadOnlyList<CommitMove>? moves = null, SequenceState? s = null, UndoRecord? undo = null) =>
        new(kind, op, branch, onto, oldTip, newTip, stop, moves ?? [],
            s?.Skipped ?? [], s?.Resolved ?? [], s?.Reworded ?? [], s?.Amended ?? [], s?.AlreadyApplied ?? [],
            message, [.. log], undo);

    /// <summary>
    /// True while git holds a rebase (its folder exists), whichever backend made it. An empty git dir is no repo at all: it is never
    /// read relative to the working directory.
    /// </summary>
    internal static bool RebaseInProgress(string gitDir) =>
        gitDir.Length > 0
        && (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")));

    /// <summary>Hash and subject of each commit in a range, oldest first. Throws when git fails.</summary>
    internal static List<(string Hash, string Subject)> ReadPairs(GitClient git, List<GitResult> log, string range)
    {
        var r = BranchGit.Call(git, log, "log", "--reverse", "--no-show-signature", "--format=%H%x1f%s", range, "--");
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));
        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\x1f', 2))
            .Where(f => f.Length == 2)
            .Select(f => (f[0], f[1]))
            .ToList();
    }

    /// <summary>Removes a temporary file. Best effort.</summary>
    internal static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Left behind; the next step overwrites it.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
