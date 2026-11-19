namespace graftui.Model;

/// <summary>
/// What the builder reads. BaseChain is the base's first-parent history, newest first (the reader keeps at most
/// <see cref="LaneGraphBuilder.BaseMax"/> of it and sets BaseChainMore when it cut more). Others are the commits of the other
/// branches that the base does not have, in any order. Branches is the branch list in git's order; the base's own entry is
/// skipped by name.
/// </summary>
public sealed record LaneGraphInput(string BaseName, IReadOnlyList<GraphCommit> BaseChain, bool BaseChainMore,
    IReadOnlyList<Branch> Branches, IReadOnlyList<GraphCommit> Others, bool OthersCut);

/// <summary>
/// Builds a <see cref="LaneGraph"/> from git history. Pure: no git calls and no screen. The steps run in order in
/// <c>Work.Run</c>, and each one is a private method there.
/// </summary>
public static class LaneGraphBuilder
{
    /// <summary>Base commits shown at most.</summary>
    public const int BaseMax = 12;

    /// <summary>Base commits shown at least, when the base has that many.</summary>
    public const int MinBase = 3;

    /// <summary>Newest commits kept per lane. Older ones are counted as Hidden.</summary>
    public const int PerLaneMax = 8;

    /// <summary>Rows at most, the base included. The screen fit drops more than this when it has less room.</summary>
    public const int MaxLanes = 60;

    /// <summary>Commits of the other branches to use at most.</summary>
    public const int OthersMax = 2000;

    /// <summary>The lane graph for the input. The empty graph when the base has no commits.</summary>
    public static LaneGraph Build(LaneGraphInput input)
    {
        if (input.BaseChain.Count == 0)
            return LaneGraph.Empty;
        return new Work(input).Run();
    }

    /// <summary>
    /// The branch name of a merge commit's subject: "Merge branch 'x' into y" gives "x", "Merge pull request #12 from o/x" gives
    /// "o/x", "Merge remote-tracking branch 'origin/x'" gives "origin/x". "" when the subject names no branch.
    /// </summary>
    internal static string SideName(string subject)
    {
        const string BranchPrefix = "Merge branch '";
        const string RemotePrefix = "Merge remote-tracking branch '";
        const string PullPrefix = "Merge pull request #";
        const string FromWord = " from ";

        if (subject.StartsWith(BranchPrefix, StringComparison.Ordinal))
            return QuotedName(subject, BranchPrefix.Length);
        if (subject.StartsWith(RemotePrefix, StringComparison.Ordinal))
            return QuotedName(subject, RemotePrefix.Length);
        if (subject.StartsWith(PullPrefix, StringComparison.Ordinal))
        {
            var from = subject.IndexOf(FromWord, StringComparison.Ordinal);
            if (from < 0)
                return "";
            return subject[(from + FromWord.Length)..].Trim();
        }

        return "";
    }

    // The text from start up to the next quote mark, or "" when there is no closing quote.
    private static string QuotedName(string subject, int start)
    {
        var end = subject.IndexOf('\'', start);
        return end < 0 ? "" : subject[start..end];
    }

    /// <summary>
    /// One build. It keeps the working state that the steps share. Lanes are "drafts" with ids while they are built: id 0 is the
    /// base, the others are numbered in the order they were made. The rows are numbered at the very end.
    /// </summary>
    private sealed class Work
    {
        private const int BaseId = 0;

        private readonly LaneGraphInput _input;

        // Every commit the build knows, by hash (the base chain and the others).
        private readonly Dictionary<string, GraphCommit> _commits = new();
        private readonly Dictionary<string, GraphCommit> _others = new();

        // Base commits by position, oldest = 0. The position does not change when the window later drops the oldest ones.
        private readonly Dictionary<string, int> _baseIndex = new();

        // The draft id of the lane that collected each commit. The base commits belong to the base (id 0).
        private readonly Dictionary<string, int> _owner = new();
        private readonly HashSet<string> _hidden = new();
        private readonly HashSet<string> _pointerNames = new();
        private readonly Dictionary<string, int> _colors = new();
        private readonly List<Draft> _drafts = new();
        private readonly List<MergeSpec> _merges = new();

        // The first base position shown.
        private int _start;

        // The columns are placed on a graph of nodes: one per shown commit, and one "gap" per lane that has hidden commits or a fork
        // from the left. Each node has its predecessors (the ones that must come before it, to its left).
        private readonly Dictionary<string, int> _nodeOf = new();
        private readonly Dictionary<int, int> _gapOf = new();
        private readonly List<List<int>> _preds = new();
        private readonly List<List<int>> _succs = new();
        private int[] _col = [];

        public Work(LaneGraphInput input)
        {
            _input = input;
            var lanes = BranchLanes.Assign(input.Branches);
            for (var i = 0; i < input.Branches.Count; i++)
                _colors[input.Branches[i].Name] = lanes[i];
        }

        public LaneGraph Run()
        {
            AddBaseDraft();
            IndexOthers();
            ClaimBranches();
            ClaimSideLanes();
            ResolveForks();
            ChooseWindow();
            AddNodes();
            AddEdges();
            ComputeColumns();
            var links = LinksWithColumns();
            var pointers = PointersWithLanes();
            var rows = RowsThatFit(BuildGroups());
            return Assemble(rows, links, pointers);
        }

        // Step 2: the base lane. Its commits are all known now; the window later keeps only the newest of them.
        private void AddBaseDraft()
        {
            var count = _input.BaseChain.Count;
            var draft = new Draft { Kind = LaneKind.Base, Name = _input.BaseName, ColorLane = ColorOf(_input.BaseName) };
            for (var i = 0; i < count; i++)
            {
                // The chain is newest first, so the position i is counted from the end of the list.
                var commit = _input.BaseChain[count - 1 - i];
                _commits[commit.Hash] = commit;
                _baseIndex[commit.Hash] = i;
                _owner[commit.Hash] = BaseId;
                draft.Hashes.Add(commit.Hash);
            }

            _drafts.Add(draft);
        }

        // Step 2: the other branches' commits, keyed by hash. A commit read twice counts once; the base's own commits never count.
        private void IndexOthers()
        {
            foreach (var commit in _input.Others)
            {
                if (_owner.ContainsKey(commit.Hash) || _others.ContainsKey(commit.Hash))
                    continue;
                _others[commit.Hash] = commit;
                _commits[commit.Hash] = commit;
            }
        }

        // Step 3 and 4: the branch lanes. A branch that is stacked on another one has the smaller depth, so it claims first and the
        // longer branch forks from its tip.
        private void ClaimBranches()
        {
            var ordered = _input.Branches
                .Where(b => b.Name != _input.BaseName)
                .OrderBy(b => DepthOf(b.Hash))
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var branch in ordered)
            {
                var laneId = _drafts.Count;
                var walk = WalkFrom(branch.Hash, laneId);
                if (walk.Collected.Count == 0)
                {
                    // Nothing of its own: the tip is on the base, or another branch already owns it. It becomes a pointer.
                    _pointerNames.Add(branch.Name);
                    continue;
                }

                AddLane(LaneKind.Branch, branch.Name, ColorOf(branch.Name), walk);
            }
        }

        // Step 3: how many first-parent steps from the tip are still among the others.
        private int DepthOf(string tip)
        {
            var depth = 0;
            var current = tip;
            while (_others.ContainsKey(current))
            {
                depth++;
                var parents = _others[current].Parents;
                if (parents.Count == 0)
                    return depth;
                current = parents[0];
            }

            return depth;
        }

        // Step 4: walks first parents from the tip and collects the commits that are among the others and not owned yet. They are
        // marked as owned by laneId. The walk stops at the first commit it may not take: the fork point (Stop), or null at a root.
        private ClaimWalk WalkFrom(string tip, int laneId)
        {
            var collected = new List<string>();
            var current = tip;
            while (true)
            {
                if (!_others.ContainsKey(current) || _owner.ContainsKey(current))
                    return new ClaimWalk(collected, current);

                collected.Add(current);
                _owner[current] = laneId;
                var parents = _others[current].Parents;
                if (parents.Count == 0)
                    return new ClaimWalk(collected, null);
                current = parents[0];
            }
        }

        // Makes a draft lane from a walk. The walk went newest first; the lane lists its commits oldest first, and the oldest
        // ones beyond PerLaneMax are hidden.
        private Draft AddLane(LaneKind kind, string name, int colorLane, ClaimWalk walk)
        {
            var draft = new Draft { Kind = kind, Name = name, ColorLane = colorLane, Stop = walk.Stop };
            draft.Hashes.AddRange(walk.Collected);
            draft.Hashes.Reverse();
            draft.Hidden = Math.Max(0, draft.Hashes.Count - PerLaneMax);
            for (var i = 0; i < draft.Hidden; i++)
                _hidden.Add(draft.Hashes[i]);

            _drafts.Add(draft);
            return draft;
        }

        // Step 5: the side lanes. Every lane's kept merge commits are scanned, breadth first, so a side lane's own merges come after
        // the lanes that found it. Base merges are scanned too, though a merge on the base rarely brings in one of the others.
        private void ClaimSideLanes()
        {
            var queue = new Queue<int>();
            for (var id = 0; id < _drafts.Count; id++)
                queue.Enqueue(id);

            while (queue.Count > 0)
                ScanMerges(queue.Dequeue(), queue);
        }

        // Every kept merge commit of a lane. Its first parent is the line the lane follows; each other parent is a merge.
        private void ScanMerges(int laneId, Queue<int> queue)
        {
            foreach (var hash in KeptOf(_drafts[laneId]))
            {
                var merge = _commits[hash];
                for (var i = 1; i < merge.Parents.Count; i++)
                    LinkMergedParent(laneId, merge, merge.Parents[i], queue);
            }
        }

        private void LinkMergedParent(int mergeLane, GraphCommit merge, string parent, Queue<int> queue)
        {
            if (_owner.TryGetValue(parent, out var owner))
            {
                // Already on a lane: a base commit, or a branch merged in earlier. Link to it and make no new lane.
                _merges.Add(new MergeSpec(owner, parent, mergeLane, merge.Hash));
                return;
            }

            // Older history that is not one of the others draws as a plain merge dot. Past MaxLanes there is no room for another lane.
            if (!_others.ContainsKey(parent) || _drafts.Count >= MaxLanes)
                return;

            var laneId = _drafts.Count;
            var walk = WalkFrom(parent, laneId);
            var side = AddLane(LaneKind.Side, SideName(merge.Subject), _drafts[mergeLane].ColorLane, walk);
            side.Parent = mergeLane;
            side.MergeHash = merge.Hash;
            _merges.Add(new MergeSpec(laneId, parent, mergeLane, merge.Hash));
            queue.Enqueue(laneId);
        }

        // Step 4: where each lane starts. A fork from a base commit, or from a commit of another lane, or from older history (drawn
        // from the left), or nowhere (the lane is unrelated). Side lanes keep the merge lane as their Parent.
        private void ResolveForks()
        {
            for (var id = 1; id < _drafts.Count; id++)
            {
                var draft = _drafts[id];
                var stop = draft.Stop;
                if (stop is null)
                {
                    draft.ForkLane = -1;
                    draft.ForkHash = null;
                }
                else if (_baseIndex.ContainsKey(stop))
                {
                    draft.ForkLane = BaseId;
                    draft.ForkHash = stop;
                }
                else if (_owner.ContainsKey(stop))
                {
                    draft.ForkLane = _owner[stop];
                    draft.ForkHash = stop;
                }
                else
                {
                    // Older history than the read: no column to fork from, so it is drawn from the left.
                    draft.ForkLane = BaseId;
                    draft.ForkHash = null;
                }

                if (draft.Kind == LaneKind.Branch)
                    draft.Parent = draft.ForkLane;
            }
        }

        // Step 6: the base window. It starts one commit below the oldest base commit something hangs from, shows at least MinBase
        // and at most BaseMax commits, and a fork from a commit that is no longer shown comes in from the left.
        private void ChooseWindow()
        {
            var count = _input.BaseChain.Count;
            var oldest = count;
            foreach (var draft in _drafts)
            {
                var forkHash = draft.ForkHash;
                if (draft.ForkLane == BaseId && forkHash is not null)
                    oldest = Math.Min(oldest, _baseIndex[forkHash]);
            }

            foreach (var branch in _input.Branches)
            {
                if (_pointerNames.Contains(branch.Name) && _baseIndex.ContainsKey(branch.Hash))
                    oldest = Math.Min(oldest, _baseIndex[branch.Hash]);
            }

            var start = count - MinBase;
            if (oldest < count)
                start = Math.Min(start, oldest - 1);
            start = Math.Clamp(start, Math.Max(0, count - BaseMax), count - 1);
            _start = start;

            _drafts[BaseId].Hashes.RemoveRange(0, start);
            foreach (var draft in _drafts)
            {
                var forkHash = draft.ForkHash;
                if (draft.ForkLane == BaseId && forkHash is not null && _baseIndex[forkHash] < start)
                    draft.ForkHash = null;
            }
        }

        // Step 7: one node per shown commit and one gap node per lane that needs one.
        private void AddNodes()
        {
            for (var id = 0; id < _drafts.Count; id++)
            {
                foreach (var hash in KeptOf(_drafts[id]))
                    _nodeOf[hash] = AddNode();

                if (HasGap(id))
                    _gapOf[id] = AddNode();
            }
        }

        // A lane has a gap when it has hidden commits, or when it forks from older history (drawn from the left).
        private bool HasGap(int id)
        {
            var draft = _drafts[id];
            return draft.Hidden > 0 || (draft.ForkLane == BaseId && draft.ForkHash is null);
        }

        private int AddNode()
        {
            _preds.Add(new List<int>());
            _succs.Add(new List<int>());
            return _preds.Count - 1;
        }

        private void AddEdge(int from, int to)
        {
            _succs[from].Add(to);
            _preds[to].Add(from);
        }

        // The predecessors of each shown commit: its parents, resolved to the nodes they are drawn as.
        private void AddEdges()
        {
            for (var id = 0; id < _drafts.Count; id++)
            {
                var kept = KeptOf(_drafts[id]);
                for (var k = 0; k < kept.Count; k++)
                {
                    var commit = _commits[kept[k]];
                    var node = _nodeOf[kept[k]];
                    for (var j = 0; j < commit.Parents.Count; j++)
                    {
                        // The first commit of a lane with a gap takes the gap as its first parent. The gap stands in for the hidden
                        // commits (or the fork from the left) that its first parent is.
                        if (j == 0 && k == 0 && _gapOf.TryGetValue(id, out var gap))
                            AddEdge(gap, node);
                        else
                            AddEdgeFrom(commit.Parents[j], node);
                    }
                }

                // A gap comes after the commit its lane forks from.
                var forkHash = _drafts[id].ForkHash;
                if (forkHash is not null && _gapOf.TryGetValue(id, out var forkGap))
                    AddEdgeFrom(forkHash, forkGap);
            }
        }

        private void AddEdgeFrom(string parentHash, int node)
        {
            var from = NodeOf(parentHash);
            if (from >= 0)
                AddEdge(from, node);
        }

        // Step 7: the column of every node is one more than the highest column of its predecessors (0 when it has none). Each node
        // is placed once all its predecessors are, so the walk goes in topological order (Kahn's method, no recursion).
        private void ComputeColumns()
        {
            var count = _preds.Count;
            _col = new int[count];
            var waiting = new int[count];
            var ready = new Queue<int>();
            for (var n = 0; n < count; n++)
            {
                waiting[n] = _preds[n].Count;
                if (waiting[n] == 0)
                    ready.Enqueue(n);
            }

            while (ready.Count > 0)
            {
                var current = ready.Dequeue();
                foreach (var next in _succs[current])
                {
                    _col[next] = Math.Max(_col[next], _col[current] + 1);
                    waiting[next]--;
                    if (waiting[next] == 0)
                        ready.Enqueue(next);
                }
            }
        }

        // The node that draws a commit. A hidden commit has no node of its own: it is drawn in its lane's gap. -1 when the commit is
        // not shown (older base history, or not in the build at all).
        private int NodeOf(string hash)
        {
            if (_nodeOf.TryGetValue(hash, out var node))
                return node;
            if (_hidden.Contains(hash) && _gapOf.TryGetValue(_owner[hash], out var gap))
                return gap;
            return -1;
        }

        private int ColumnOf(string hash)
        {
            var node = NodeOf(hash);
            return node < 0 ? -1 : _col[node];
        }

        private int GapColumn(int id) => _gapOf.TryGetValue(id, out var gap) ? _col[gap] : -1;

        // Step 7: the links, with their columns. Merges from a commit that is not shown have no column and are left out.
        private List<LaneLink> LinksWithColumns()
        {
            var links = new List<LaneLink>();
            for (var id = 1; id < _drafts.Count; id++)
            {
                var draft = _drafts[id];
                if (draft.ForkLane < 0)
                    continue;

                var forkHash = draft.ForkHash;
                var fromColumn = forkHash is null ? -1 : ColumnOf(forkHash);
                var toColumn = ColumnOf(FirstKept(draft));
                links.Add(new LaneLink(LinkKind.Fork, draft.ForkLane, fromColumn, id, toColumn));
            }

            foreach (var merge in _merges)
            {
                var fromColumn = ColumnOf(merge.FromHash);
                var toColumn = ColumnOf(merge.ToHash);
                if (fromColumn >= 0 && toColumn >= 0)
                    links.Add(new LaneLink(LinkKind.Merge, merge.FromLane, fromColumn, merge.ToLane, toColumn));
            }

            return links;
        }

        // Step 8: each pointer branch sits on the lane that owns its tip. A tip on the base, or on older history, belongs to the base.
        private List<BranchPointer> PointersWithLanes()
        {
            var pointers = new List<BranchPointer>();
            foreach (var branch in _input.Branches)
            {
                if (!_pointerNames.Contains(branch.Name))
                    continue;

                var lane = _owner.TryGetValue(branch.Hash, out var owner) ? owner : BaseId;
                pointers.Add(new BranchPointer(branch.Name, lane, ColumnOf(branch.Hash)));
            }

            return pointers;
        }

        // Step 9: the groups. A group is a top-level lane and every lane under it, in the order the rows are drawn. Top-level lanes
        // come from the base (by fork column, highest first) and then the unrelated ones.
        private List<List<int>> BuildGroups()
        {
            var children = new List<int>[_drafts.Count];
            for (var id = 0; id < _drafts.Count; id++)
                children[id] = new List<int>();

            var unrelated = new List<int>();
            for (var id = 1; id < _drafts.Count; id++)
            {
                var parent = _drafts[id].Parent;
                if (parent >= 0)
                    children[parent].Add(id);
                else
                    unrelated.Add(id);
            }

            var tops = SortRows(children[BaseId]);
            tops.AddRange(SortRows(unrelated));

            var groups = new List<List<int>>();
            foreach (var top in tops)
            {
                var group = new List<int>();
                AddWithChildren(top, children, group);
                groups.Add(group);
            }

            return groups;
        }

        // Depth first: a lane, then the lanes that hang under it.
        private void AddWithChildren(int id, List<int>[] children, List<int> group)
        {
            group.Add(id);
            foreach (var child in SortRows(children[id]))
                AddWithChildren(child, children, group);
        }

        // Fork column (or merge column for a side lane), highest first, so a connector never crosses a line to its right. Then the name.
        private List<int> SortRows(IEnumerable<int> ids) =>
            ids.OrderByDescending(id => SortKey(id))
                .ThenBy(id => _drafts[id].Name, StringComparer.Ordinal)
                .ThenBy(id => id)
                .ToList();

        // -1 for a fork from older history, and for an unrelated lane: they sort last among their group.
        private int SortKey(int id)
        {
            var draft = _drafts[id];
            var hash = draft.Kind == LaneKind.Side ? draft.MergeHash : draft.ForkHash;
            return hash is null ? -1 : ColumnOf(hash);
        }

        // Step 10: keeps whole groups, in row order, while the rows fit in MaxLanes. The first group that does not fit ends the list.
        private List<int> RowsThatFit(List<List<int>> groups)
        {
            var rows = new List<int> { BaseId };
            foreach (var group in groups)
            {
                if (rows.Count + group.Count > MaxLanes)
                    break;
                rows.AddRange(group);
            }

            return rows;
        }

        // Step 9 and 11: numbers the rows, makes the lanes and the links with the row numbers, and works out the counts.
        private LaneGraph Assemble(List<int> rows, List<LaneLink> links, List<BranchPointer> pointers)
        {
            var rowOf = new int[_drafts.Count];
            for (var id = 0; id < rowOf.Length; id++)
                rowOf[id] = -1;
            for (var r = 0; r < rows.Count; r++)
                rowOf[rows[r]] = r;

            var lanes = new List<Lane>();
            for (var r = 0; r < rows.Count; r++)
            {
                var id = rows[r];
                var draft = _drafts[id];
                // A lane's parent is always kept with it (a group holds the whole subtree), so its row exists.
                var parent = draft.Parent < 0 ? -1 : rowOf[draft.Parent];
                lanes.Add(new Lane(r, draft.Kind, draft.Name, draft.ColorLane, parent, CommitsOf(draft), draft.Hidden, GapColumn(id)));
            }

            var shownLinks = new List<LaneLink>();
            foreach (var link in links)
            {
                var from = rowOf[link.FromLane];
                var to = rowOf[link.ToLane];
                if (from >= 0 && to >= 0)
                    shownLinks.Add(new LaneLink(link.Kind, from, link.FromColumn, to, link.ToColumn));
            }

            var shownPointers = new List<BranchPointer>();
            foreach (var pointer in pointers)
            {
                var row = rowOf[pointer.Lane];
                if (row >= 0)
                    shownPointers.Add(new BranchPointer(pointer.Name, row, pointer.Column));
            }

            var highest = -1;
            foreach (var lane in lanes)
            {
                foreach (var commit in lane.Commits)
                    highest = Math.Max(highest, commit.Column);
                highest = Math.Max(highest, lane.Gap);
            }

            foreach (var link in shownLinks)
                highest = Math.Max(highest, Math.Max(link.FromColumn, link.ToColumn));
            foreach (var pointer in shownPointers)
                highest = Math.Max(highest, pointer.Column);

            var allIds = Enumerable.Range(0, _drafts.Count).ToList();
            var leftOut = CountBranches(allIds) - CountBranches(rows);
            var baseMore = _start > 0 || _input.BaseChainMore;
            return new LaneGraph(_input.BaseName, lanes, shownLinks, shownPointers, highest + 1, baseMore, _input.OthersCut, leftOut);
        }

        private int CountBranches(IEnumerable<int> ids)
        {
            var count = 0;
            foreach (var id in ids)
            {
                if (_drafts[id].Kind == LaneKind.Branch)
                    count++;
            }

            return count;
        }

        private List<LaneCommit> CommitsOf(Draft draft)
        {
            var commits = new List<LaneCommit>();
            foreach (var hash in KeptOf(draft))
            {
                var commit = _commits[hash];
                commits.Add(new LaneCommit(hash, commit.ShortHash, commit.Subject, ColumnOf(hash), commit.Parents.Count > 1));
            }

            return commits;
        }

        // A branch's colour from the BranchLanes assignment. The base falls back to the trunk colour when its name is not listed.
        private int ColorOf(string name) => _colors.TryGetValue(name, out var lane) ? lane : BranchLanes.Trunk;

        private static List<string> KeptOf(Draft draft) => draft.Hashes.Skip(draft.Hidden).ToList();

        private static string FirstKept(Draft draft) => draft.Hashes[draft.Hidden];

        // The working lane while it is built. Stop is the fork point the walk ended at (null when it reached a root). The fork fields
        // are filled in by ResolveForks. ForkLane -1 means no fork; ForkHash null with ForkLane 0 means a fork from the left.
        private sealed class Draft
        {
            public LaneKind Kind { get; set; }

            public string Name { get; set; } = "";

            public int ColorLane { get; set; }

            public int Parent { get; set; } = -1;

            public string? Stop { get; set; }

            public int ForkLane { get; set; } = -1;

            public string? ForkHash { get; set; }

            // Side lanes only: the merge commit they hang off.
            public string? MergeHash { get; set; }

            // Oldest first, hidden ones included.
            public List<string> Hashes { get; } = new();

            public int Hidden { get; set; }
        }

        // What one walk collected: newest first, and the commit it stopped at (null at a root).
        private sealed record ClaimWalk(List<string> Collected, string? Stop);

        // A merge link as hashes. The columns are looked up after the columns are placed.
        private sealed record MergeSpec(int FromLane, string FromHash, int ToLane, string ToHash);
    }
}
