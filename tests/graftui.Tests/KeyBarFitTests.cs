using graftui.Model;
using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

public class KeyBarFitTests
{
    private static readonly ChangesKeyState AllChanges = new(RowCount: 5, SelectedSide: ChangeSide.Unstaged, HasUnstaged: true, HasStaged: true,
        HasHead: true, HasStash: true, HasUndo: true);

    private static readonly BranchesKeyState AllBranches = new(BranchCount: 3, CanMerge: true, HasUndo: true, CanSwitch: true,
        CanCreate: true, CanRename: true, CanDelete: true);

    private static IEnumerable<string> Keys(IReadOnlyList<(string Key, string Label)> items) => items.Select(i => i.Key);

    /// <summary>The bar as the screen words it: "Key label", items two spaces apart.</summary>
    private static string Bar(IEnumerable<(string Key, string Label, int Priority)> items) =>
        string.Join("  ", items.Select(i => i.Key + i.Label));

    private static IEnumerable<string> Names(IEnumerable<(string Key, string Label, int Priority)> items) => items.Select(i => i.Key);

    /// <summary>The bar of fitted items (key and label, no priority), as the screen words it.</summary>
    private static string Pairs(IEnumerable<(string Key, string Label)> items) =>
        string.Join("  ", items.Select(i => i.Key + i.Label));

    // ----- fitting a narrow bar -----

    [Fact]
    public void Changes_bar_at_80_columns_keeps_everything_one_space_apart()
    {
        // The slim bar: only the main keys stay, so at 80 columns everything fits with one space between items.
        var kept = KeyBarView.FitItems(KeyBarView.ChangesItems(AllChanges), 80, out var separator);

        Assert.Equal(new[] { "Tab", "↑↓", "Space", "c", "u", "?", "Esc" }, Keys(kept));
        Assert.Equal(1, separator);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(80)]
    [InlineData(64)]
    [InlineData(40)]
    public void Tab_and_Esc_stay_on_the_changes_bar_at_every_width(int width)
    {
        var kept = Keys(KeyBarView.FitItems(KeyBarView.ChangesItems(AllChanges), width, out _));

        Assert.Contains("Tab", kept);
        Assert.Contains("Esc", kept);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(80)]
    [InlineData(64)]
    [InlineData(40)]
    public void Tab_and_Esc_stay_on_the_branches_bar_at_every_width(int width)
    {
        var kept = Keys(KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), width, out _));

        Assert.Contains("Tab", kept);
        Assert.Contains("Esc", kept);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(80)]
    [InlineData(100)]
    public void Input_bar_keeps_tab_next_field_before_ctrl_enter_save_from_body(int width)
    {
        // Subject focused, the bar of the commit form. At 64 columns the lowest item, Ctrl+Enter, goes before Tab does.
        var kept = Keys(KeyBarView.FitItems(KeyBarView.InputItems(bodyFocused: false), width, out _));

        Assert.Contains("Tab", kept);
        Assert.Contains("Esc", kept);
        Assert.Contains("Enter", kept);
        if (width == 64)
            Assert.DoesNotContain("Ctrl+Enter", kept);
    }

    [Fact]
    public void Changes_bar_at_200_columns_keeps_everything_with_two_spaces()
    {
        var all = KeyBarView.ChangesItems(AllChanges);
        var kept = KeyBarView.FitItems(all, 200, out var separator);

        Assert.Equal(all.Select(i => i.Key), Keys(kept));
        Assert.Equal(2, separator);
    }

    [Fact]
    public void In_the_body_the_bar_says_ctrl_j_saves_because_enter_adds_a_line()
    {
        Assert.Equal("Ctrl+J save  Tab next field  Esc cancel", Bar(KeyBarView.InputItems(bodyFocused: true)));
        Assert.Equal("Enter save  Tab next field  Ctrl+Enter save from body  Esc cancel", Bar(KeyBarView.InputItems(bodyFocused: false)));
    }

    [Fact]
    public void Branches_bar_at_40_columns_keeps_tab_help_and_esc()
    {
        // Narrow screens keep discoverability first: ? outranks the action keys, so m, s and u go before it does.
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 40, out _);

        Assert.Equal(new[] { "Tab", "?", "Esc" }, Keys(kept));
    }

    [Fact]
    public void Branches_bar_at_64_columns_drops_only_undo()
    {
        // The slim bar at 64 columns: Tab, m, s, ? and Esc fit one space apart; u goes first (lowest priority).
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 64, out var separator);

        Assert.Equal(new[] { "Tab", "m", "s", "?", "Esc" }, Keys(kept));
        Assert.Equal(1, separator);
    }

    [Fact]
    public void Branches_bar_at_80_columns_keeps_everything_with_two_spaces()
    {
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 80, out var separator);

        Assert.Equal(new[] { "Tab", "m", "s", "u", "?", "Esc" }, Keys(kept));
        Assert.Equal(2, separator);
    }

    [Fact]
    public void Branches_bar_with_every_key_lists_them_in_order()
    {
        var all = KeyBarView.BranchesItems(AllBranches);

        Assert.Equal(new[] { "Tab", "m", "s", "u", "?", "Esc" }, Names(all));
        Assert.Equal("Tab changes  m merge  s switch  u undo  ? help  Esc quit", Bar(all));
    }

    [Fact]
    public void Branches_bar_for_the_selected_current_branch_has_no_merge_or_switch()
    {
        var snapshot = new RepoSnapshot("main", "a111111", true,
            [new Branch("main", "a111111", "a111111", true), new Branch("feature/x", "b222222", "b222222", false)],
            new Dictionary<string, (int, int)>());

        var state = BranchesKeyState.For(snapshot, snapshot.Branches[0]);

        Assert.Equal("Tab changes  ? help  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Branches_bar_for_a_branch_ahead_has_merge_switch_and_undo()
    {
        var snapshot = new RepoSnapshot("main", "a111111", true,
            [new Branch("main", "a111111", "a111111", true), new Branch("feature/x", "b222222", "b222222", false)],
            new Dictionary<string, (int, int)> { ["feature/x"] = (1, 0) }, HasUndo: true);

        var state = BranchesKeyState.For(snapshot, snapshot.Branches[1]);

        Assert.Equal("Tab changes  m merge  s switch  u undo  ? help  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Detached_head_has_no_new_branch_key_but_keeps_rename_and_delete_rules()
    {
        var snapshot = new RepoSnapshot("", "a111111", true,
            [new Branch("feature/x", "b222222", "b222222", false)], new Dictionary<string, (int, int)>());

        var state = BranchesKeyState.For(snapshot, snapshot.Branches[0]);

        Assert.False(state.CanCreate);
        Assert.True(state.CanRename);
        Assert.True(state.CanDelete);
        Assert.True(state.CanSwitch);
    }

    [Fact]
    public void Message_bar_names_the_enter_action_it_is_given()
    {
        Assert.Equal("Enter create  Esc cancel", Bar(KeyBarView.MessageItems(" create")));
        Assert.Equal("Enter stash  Esc cancel", Bar(KeyBarView.MessageItems(" stash")));
    }

    [Fact]
    public void A_kept_bar_never_outgrows_the_width_it_was_given()
    {
        foreach (var width in new[] { 30, 40, 60, 80, 100 })
        {
            var kept = KeyBarView.FitItems(KeyBarView.ChangesItems(AllChanges with { SelectedSide = ChangeSide.Staged }), width, out var separator);
            var used = kept.Sum(i => (" " + i.Key + " ").Length + i.Label.Length) + separator * (kept.Count - 1) + 2;

            Assert.True(used <= width || kept.Count == 1, $"width {width}: {used} columns for {kept.Count} items");
        }
    }

    // ----- Changes: each key shows when it acts -----

    [Fact]
    public void Nothing_to_act_on_leaves_only_tab_help_and_esc()
    {
        Assert.Equal("Tab branches  ? help  Esc quit", Bar(KeyBarView.ChangesItems(ChangesKeyState.Empty)));
    }

    [Fact]
    public void Up_and_down_show_only_with_two_or_more_rows()
    {
        Assert.DoesNotContain("↑↓", Names(KeyBarView.ChangesItems(AllChanges with { RowCount = 1 })));
        Assert.Contains("↑↓", Names(KeyBarView.ChangesItems(AllChanges with { RowCount = 2 })));
    }

    [Fact]
    public void Space_shows_for_a_selected_row_and_says_stage_or_unstage()
    {
        Assert.DoesNotContain("Space", Names(KeyBarView.ChangesItems(AllChanges with { SelectedSide = null })));
        Assert.Equal(" stage", KeyBarView.ChangesItems(AllChanges with { SelectedSide = ChangeSide.Unstaged }).Single(i => i.Key == "Space").Label);
        Assert.Equal(" unstage", KeyBarView.ChangesItems(AllChanges with { SelectedSide = ChangeSide.Staged }).Single(i => i.Key == "Space").Label);
    }

    [Fact]
    public void The_slim_changes_bar_keeps_only_tab_arrows_space_commit_undo_help_and_esc()
    {
        Assert.Equal("Tab branches  ↑↓ file  Space stage  c commit  u undo  ? help  Esc quit",
            Bar(KeyBarView.ChangesItems(AllChanges)));
    }

    [Fact]
    public void The_dropped_changes_keys_still_work_but_are_not_on_the_bar()
    {
        // a, U, A, x, z and Z left the bar for the ? popup; their keys act as before.
        var names = Names(KeyBarView.ChangesItems(AllChanges));

        Assert.DoesNotContain("a", names);
        Assert.DoesNotContain("U", names);
        Assert.DoesNotContain("A", names);
        Assert.DoesNotContain("x", names);
        Assert.DoesNotContain("z", names);
        Assert.DoesNotContain("Z", names);
    }

    [Fact]
    public void Space_label_follows_the_selected_row()
    {
        var kept = KeyBarView.FitItems(KeyBarView.ChangesItems(AllChanges with { SelectedSide = ChangeSide.Staged }), 200, out _);

        Assert.Equal(" unstage", kept.Single(i => i.Key == "Space").Label);
    }

    // ----- Branches -----

    [Fact]
    public void Branches_bar_on_the_current_branch_has_no_merge_or_switch()
    {
        // The mock: main (current) selected.
        var state = new BranchesKeyState(BranchCount: 3, CanMerge: false, HasUndo: false);

        Assert.Equal("Tab changes  ? help  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Branches_bar_for_a_branch_ahead_of_main_has_merge()
    {
        var state = new BranchesKeyState(BranchCount: 3, CanMerge: true, HasUndo: false);

        Assert.Equal("Tab changes  m merge  ? help  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Branches_bar_shows_undo_only_with_a_step()
    {
        Assert.Equal("Tab changes  m merge  u undo  ? help  Esc quit",
            Bar(KeyBarView.BranchesItems(new BranchesKeyState(BranchCount: 1, CanMerge: true, HasUndo: true))));
    }

    [Theory]
    [InlineData(true, true, false)]    // the current branch is never merged into itself
    [InlineData(false, true, true)]    // ahead of HEAD
    [InlineData(false, false, false)]  // nothing to merge: behind or level
    public void Merge_is_offered_for_a_non_current_branch_that_is_ahead_or_unknown(bool isCurrent, bool ahead, bool expected)
    {
        var branch = new Branch("feature/x", "abc", "abc", isCurrent);
        var counts = new Dictionary<string, (int Ahead, int Behind)> { ["feature/x"] = (ahead ? 1 : 0, 0) };

        Assert.Equal(expected, BranchesKeyState.MergeOffered(branch, counts));
    }

    [Fact]
    public void Merge_is_offered_when_the_counts_are_not_known_yet()
    {
        Assert.True(BranchesKeyState.MergeOffered(new Branch("feature/x", "abc", "abc", false), new Dictionary<string, (int, int)>()));
        Assert.False(BranchesKeyState.MergeOffered(null, new Dictionary<string, (int, int)>()));
    }

    [Fact]
    public void Branches_bar_no_longer_lists_rebase_or_interactive_but_the_popup_does()
    {
        var with = BranchesKeyState.Empty with { BranchCount = 3, CanRebase = true };

        Assert.DoesNotContain("r", Names(KeyBarView.BranchesItems(with)));
        Assert.DoesNotContain("i", Names(KeyBarView.BranchesItems(with)));
        Assert.Contains("r", HelpRows().Select(r => r.Key));
        Assert.Contains("i", HelpRows().Select(r => r.Key));
    }

    private static IReadOnlyList<HelpRow> HelpRows() =>
        HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Branches }).Rows;

    [Fact]
    public void Confirm_bar_lists_enter_rebase_and_esc_cancel()
    {
        Assert.Equal("Enter rebase  Esc cancel", Bar(KeyBarView.ConfirmItems()));
    }

    [Fact]
    public void The_bar_in_confirm_mode_shows_the_confirm_keys()
    {
        var bar = new KeyBarView { Mode = KeyBarMode.Confirm };

        Assert.Equal("Enter rebase  Esc cancel", Bar(bar.Items()));
    }

    [Fact]
    public void Branches_bar_lists_nothing_extra_for_rebase_but_the_popup_names_it()
    {
        var bar = new KeyBarView { Mode = KeyBarMode.Branches };

        Assert.DoesNotContain("i interactive", Bar(bar.Items()));
        Assert.Contains("i", HelpOverlay.ForBar(bar).Rows.Select(r => r.Key));
    }

    [Fact]
    public void The_todo_bar_at_100_columns_drops_only_the_arrows_and_edit()
    {
        var bar = new KeyBarView { Mode = KeyBarMode.Todo };

        var fitted = KeyBarView.FitItems(bar.Items(), 100, out var separator);

        Assert.Equal(1, separator);
        Assert.Equal("⇧↑↓ reorder  p pick  r reword  s squash  f fixup  d drop  Enter start  Esc cancel", Pairs(fitted));
    }

    [Fact]
    public void The_todo_bar_at_64_columns_keeps_reorder_pick_drop_start_and_cancel()
    {
        var fitted = KeyBarView.FitItems(KeyBarView.TodoItems(), 64, out _);

        Assert.Equal("⇧↑↓ reorder  p pick  d drop  Enter start  Esc cancel", Pairs(fitted));
    }

    [Fact]
    public void The_todo_bar_lists_the_plan_keys_in_order_when_there_is_room()
    {
        Assert.Equal("↑↓←→ move  ⇧↑↓ reorder  p pick  r reword  e edit  s squash  f fixup  d drop  Enter start  Esc cancel",
            Bar(KeyBarView.TodoItems()));
    }
}
