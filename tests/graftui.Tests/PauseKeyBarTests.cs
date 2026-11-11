using graftui.Model;
using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>
/// The key bar while a rebase is paused: the pause's keys for each kind of stop, the Changes keys that the pause hides, and the
/// snapshot that turns the pause on for the key bar.
/// </summary>
public class PauseKeyBarTests
{
    private static IEnumerable<string> Keys(IEnumerable<(string Key, string Label, int Priority)> items) => items.Select(i => i.Key);

    private static string Bar(IEnumerable<(string Key, string Label, int Priority)> items) =>
        string.Join("  ", items.Select(i => i.Key + i.Label));

    private static readonly ChangesKeyState AllChanges = new(RowCount: 5, SelectedSide: ChangeSide.Unstaged, HasUnstaged: true, HasStaged: true,
        HasHead: true, HasStash: true, HasUndo: true);

    [Fact]
    public void A_conflict_pause_lists_continue_skip_abort_then_changes_and_quit()
    {
        Assert.Equal("Enter continue  S skip  X abort  Tab changes  ? help  Esc quit", Bar(KeyBarView.PauseItems(PauseKind.Conflict)));
    }

    [Theory]
    [InlineData(PauseKind.Edit, "Enter amend & continue  X abort  Tab changes  ? help  Esc quit")]
    [InlineData(PauseKind.Reword, "Enter continue  X abort  Tab changes  ? help  Esc quit")]
    [InlineData(PauseKind.Squash, "Enter continue  X abort  Tab changes  ? help  Esc quit")]
    public void An_edit_or_message_pause_has_no_skip(PauseKind kind, string expected) =>
        Assert.Equal(expected, Bar(KeyBarView.PauseItems(kind)));

    [Fact]
    public void The_branches_bar_shows_the_pause_keys_instead_of_the_branch_keys_while_paused()
    {
        var state = new BranchesKeyState(BranchCount: 3, CanMerge: true, HasUndo: true, CanSwitch: true, Pause: PauseKind.Conflict);

        Assert.Equal(Bar(KeyBarView.PauseItems(PauseKind.Conflict)), Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void The_branches_bar_is_the_branch_keys_when_nothing_is_paused()
    {
        var state = new BranchesKeyState(BranchCount: 3, CanMerge: true, HasUndo: false, CanSwitch: true);

        Assert.Contains("m", Keys(KeyBarView.BranchesItems(state)));
        Assert.DoesNotContain("X", Keys(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void A_paused_changes_tab_hides_commit_and_undo_but_keeps_staging_and_help()
    {
        var items = Keys(KeyBarView.ChangesItems(AllChanges with { Paused = true }));

        Assert.DoesNotContain("c", items);
        Assert.DoesNotContain("u", items);
        Assert.Contains("Space", items);
        Assert.Contains("?", items);
        Assert.Contains("Tab", items);
        Assert.Contains("Esc", items);
    }

    [Fact]
    public void The_changes_bar_without_a_pause_keeps_every_key()
    {
        var items = Keys(KeyBarView.ChangesItems(AllChanges));

        Assert.Contains("c", items);
        Assert.Contains("u", items);
    }

    [Fact]
    public void The_pause_bar_fits_at_a_full_width_and_keeps_its_keys_at_a_narrow_one()
    {
        var kept = KeyBarView.FitItems(KeyBarView.PauseItems(PauseKind.Conflict), 100, out _);

        Assert.Equal(new[] { "Enter", "S", "X", "Tab", "?", "Esc" }, kept.Select(i => i.Key));
    }

    [Fact]
    public void The_key_state_of_a_paused_snapshot_names_the_pause_kind_and_an_unpaused_one_does_not()
    {
        var branch = new Branch("feature", "a111111", "a111111", false);
        var stop = new SequenceStop(SequenceKind.Rebase, PauseKind.Edit, "feature", "main", "a111111", "a111111", "s", [], 1, 2, "", [], true);
        var paused = new RepoSnapshot("", "a111111", true, [branch], new Dictionary<string, (int, int)>()) { Paused = stop };
        var clear = new RepoSnapshot("feature", "a111111", true, [branch], new Dictionary<string, (int, int)>());

        Assert.Equal(PauseKind.Edit, BranchesKeyState.For(paused, branch).Pause);
        Assert.Null(BranchesKeyState.For(clear, branch).Pause);
    }
}
