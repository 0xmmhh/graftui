using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

public class SequenceListsTests
{
    // feature/picks is the current branch; main is the onto branch, whose tip has the subject "Change line 1 on main".
    private static readonly GraphLayout Layout = Layouts.Diverged() with
    {
        Current = "feature/picks",
        Selected = "main",
        SelectedTipCommit = Layouts.C(Layouts.Sel, subject: "Change line 1 on main"),
    };

    private static RebaseRow Row(string shortHash, string subject, RebaseAction action = RebaseAction.Pick) =>
        new(shortHash + new string('0', 33), shortHash, subject, action);

    // Oldest first, as the check gives them.
    private static readonly IReadOnlyList<RebaseRow> Rows =
    [
        Row("a1b2c3d", "Fix typo in README"),
        Row("b2c3d4e", "Add logging"),
        Row("c3d4e5f", "Bump version to 0.3"),
    ];

    private static SequenceOutcome Outcome(params CommitMove[] moves) =>
        new(SequenceOutcomeKind.Done, SequenceKind.Rebase, "feature/picks", "main", "oldtip", "newtip", null, moves,
            [], [], [], [], [], "rebased", []);

    private static string Text(CommitRow r) => r.Hash + " " + r.Subject;

    [Fact]
    public void Confirm_lists_the_rewritten_commits_newest_first_and_the_new_base_last()
    {
        var panel = SequenceLists.Confirm(Layout, Rows);
        var rows = panel.Left.Rows;

        Assert.Equal(4, rows.Count);
        Assert.Equal("c3d4e5f Bump version to 0.3  will be rewritten", Text(rows[0]));
        Assert.Equal("a1b2c3d Fix typo in README  will be rewritten", Text(rows[2]));
        Assert.Equal("d222222 Change line 1 on main  new base", Text(rows[3]));
    }

    [Fact]
    public void Confirm_header_names_the_current_branch()
    {
        var header = SequenceLists.Confirm(Layout, Rows).Left.Header;

        Assert.Equal("feature/picks (current)", string.Concat(header.Select(s => s.Text)));
    }

    [Fact]
    public void Running_gives_each_commit_its_action_newest_first()
    {
        var rows = SequenceLists.Running(Layout, Rows).Left.Rows;

        Assert.Equal(3, rows.Count);
        Assert.Equal("c3d4e5f Bump version to 0.3  pick", Text(rows[0]));
        Assert.Equal("a1b2c3d Fix typo in README  pick", Text(rows[2]));
    }

    [Fact]
    public void Rebased_lists_each_landed_commit_with_the_hash_it_had()
    {
        var o = Outcome(
            new CommitMove(Rows[0].Hash, "9e8d7c6" + new string('0', 33)),
            new CommitMove(Rows[1].Hash, "8d7c6b5" + new string('0', 33)),
            new CommitMove(Rows[2].Hash, "7c6b5a4" + new string('0', 33)));

        var rows = SequenceLists.Rebased(o, Rows, Layout).Left.Rows;

        Assert.Equal(4, rows.Count);
        Assert.Equal("7c6b5a4 Bump version to 0.3  was c3d4e5f", Text(rows[0]));
        Assert.Equal("9e8d7c6 Fix typo in README  was a1b2c3d", Text(rows[2]));
        Assert.Equal("d222222 Change line 1 on main", Text(rows[3]));
    }

    [Fact]
    public void Rebased_marks_a_fold_on_the_commit_it_went_into_and_shows_drops_and_skips()
    {
        var plan = new List<RebaseRow>
        {
            Row("a1b2c3d", "Fix typo in README"),
            Row("b2c3d4e", "Add logging", RebaseAction.Squash),
            Row("c3d4e5f", "Bump version to 0.3", RebaseAction.Drop),
            Row("d4e5f6a", "Tidy the tests"),
        };
        var o = Outcome(
            new CommitMove(plan[0].Hash, "9e8d7c6" + new string('0', 33)),
            new CommitMove(plan[1].Hash, null),
            new CommitMove(plan[2].Hash, null),
            new CommitMove(plan[3].Hash, null));

        var rows = SequenceLists.Rebased(o, plan, Layout).Left.Rows;

        Assert.Equal("9e8d7c6 Fix typo in README  was a1b2c3d + squashed b2c3d4e", Text(rows[0]));
        Assert.Equal("c3d4e5f Bump version to 0.3  dropped", Text(rows[1]));
        Assert.Equal('✕', rows[1].Dot);
        Assert.Equal("d4e5f6a Tidy the tests  skipped", Text(rows[2]));
        Assert.Equal("d222222 Change line 1 on main", Text(rows[3]));
    }
}
