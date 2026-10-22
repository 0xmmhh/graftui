using Graft.Model;
using Graft.Ui.Text;

namespace Graft.Tests;

public class CommitListsTests
{
    [Fact]
    public void After_a_conflict_lists_each_conflicting_file_last()
    {
        var outcome = new MergeOutcome(MergeKind.Conflict, "feature", "main", null, ["app.txt"], "merge aborted", []);

        var rows = CommitLists.After(Layouts.Diverged(), outcome, null);

        var last = rows[^1];
        Assert.Equal('✕', last.Dot);
        Assert.Equal("app.txt", last.Hash);
    }
}
