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

    // ----- fitting a narrow bar -----

    [Fact]
    public void Changes_bar_at_80_columns_keeps_tab_space_c_x_z_and_esc_in_order()
    {
        // Tab is second only to Esc, so it stays; the lower ones (u, Z, A, a, U, ↑↓) go first.
        var kept = KeyBarView.FitItems(KeyBarView.ChangesItems(AllChanges), 80, out var separator);

        Assert.Equal(new[] { "Tab", "Space", "c", "x", "z", "Esc" }, Keys(kept));
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
    public void Branches_bar_at_40_columns_keeps_tab_merge_and_esc()
    {
        // At 64 columns every Branches key fits, so the narrow case is the one that drops keys.
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 40, out _);

        Assert.Equal(new[] { "Tab", "m", "Esc" }, Keys(kept));
    }

    [Fact]
    public void Branches_bar_at_64_columns_drops_rename_and_delete_and_the_arrows()
    {
        // With every key on, the bar is one space apart at 64 columns: ↑↓, D and R go first (lowest priority).
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 64, out var separator);

        Assert.Equal(new[] { "Tab", "m", "s", "n", "u", "Esc" }, Keys(kept));
        Assert.Equal(1, separator);
    }

    [Fact]
    public void Branches_bar_at_80_columns_drops_only_the_arrows_and_delete()
    {
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 80, out _);

        Assert.Equal(new[] { "Tab", "m", "s", "n", "R", "u", "Esc" }, Keys(kept));
    }

    [Fact]
    public void Branches_bar_with_every_key_lists_them_in_order()
    {
        var all = KeyBarView.BranchesItems(AllBranches);

        Assert.Equal(new[] { "Tab", "↑↓", "m", "s", "n", "R", "D", "u", "Esc" }, Names(all));
        Assert.Equal("Tab changes  ↑↓ select  m merge  s switch  n new  R rename  D delete  u undo  Esc quit", Bar(all));
    }

    [Fact]
    public void Branches_bar_for_the_selected_current_branch_has_no_switch_merge_or_delete()
    {
        var snapshot = new RepoSnapshot("main", "a111111", true,
            [new Branch("main", "a111111", "a111111", true), new Branch("feature/x", "b222222", "b222222", false)],
            new Dictionary<string, (int, int)>());

        var state = BranchesKeyState.For(snapshot, snapshot.Branches[0]);

        Assert.Equal("Tab changes  ↑↓ select  n new  R rename  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Branches_bar_for_a_branch_ahead_has_every_key_it_can_act_on()
    {
        var snapshot = new RepoSnapshot("main", "a111111", true,
            [new Branch("main", "a111111", "a111111", true), new Branch("feature/x", "b222222", "b222222", false)],
            new Dictionary<string, (int, int)> { ["feature/x"] = (1, 0) }, HasUndo: true);

        var state = BranchesKeyState.For(snapshot, snapshot.Branches[1]);

        Assert.Equal("Tab changes  ↑↓ select  m merge  s switch  n new  R rename  D delete  u undo  Esc quit", Bar(KeyBarView.BranchesItems(state)));
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
    public void Nothing_to_act_on_leaves_only_tab_and_esc()
    {
        Assert.Equal("Tab branches  Esc quit", Bar(KeyBarView.ChangesItems(ChangesKeyState.Empty)));
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
    public void a_shows_only_with_unstaged_files_and_U_and_c_only_with_staged_files()
    {
        var none = AllChanges with { HasUnstaged = false, HasStaged = false };
        Assert.DoesNotContain("a", Names(KeyBarView.ChangesItems(none)));
        Assert.DoesNotContain("U", Names(KeyBarView.ChangesItems(none)));
        Assert.DoesNotContain("c", Names(KeyBarView.ChangesItems(none)));
    }

    [Fact]
    public void A_shows_only_when_head_exists()
    {
        Assert.DoesNotContain("A", Names(KeyBarView.ChangesItems(AllChanges with { HasHead = false })));
    }

    [Fact]
    public void x_shows_only_for_an_unstaged_selected_row()
    {
        Assert.DoesNotContain("x", Names(KeyBarView.ChangesItems(AllChanges with { SelectedSide = ChangeSide.Staged })));
        Assert.DoesNotContain("x", Names(KeyBarView.ChangesItems(AllChanges with { SelectedSide = null })));
    }

    [Fact]
    public void z_shows_with_any_change_and_Z_and_u_with_a_stash_and_an_undo_step()
    {
        var none = AllChanges with { RowCount = 0, SelectedSide = null, HasStash = false, HasUndo = false };
        Assert.DoesNotContain("z", Names(KeyBarView.ChangesItems(none)));
        Assert.DoesNotContain("Z", Names(KeyBarView.ChangesItems(none)));
        Assert.DoesNotContain("u", Names(KeyBarView.ChangesItems(none)));
        Assert.Contains("z", Names(KeyBarView.ChangesItems(none with { RowCount = 1 })));
        Assert.Contains("Z", Names(KeyBarView.ChangesItems(none with { HasStash = true })));
        Assert.Contains("u", Names(KeyBarView.ChangesItems(none with { HasUndo = true })));
    }

    [Fact]
    public void The_mock_bar_for_an_unstaged_file_selected_with_everything_in_reach()
    {
        // The mock's "unstaged file selected" bar, with the stash and undo step it names. The table says A shows whenever HEAD exists,
        // so the bar also has A amend; the mock leaves it out.
        var state = new ChangesKeyState(RowCount: 4, SelectedSide: ChangeSide.Unstaged, HasUnstaged: true, HasStaged: false, HasHead: true,
            HasStash: true, HasUndo: true);

        Assert.Equal("Tab branches  ↑↓ file  Space stage  a stage all  A amend  x discard  z stash  Z pop  u undo  Esc quit",
            Bar(KeyBarView.ChangesItems(state)));
    }

    [Fact]
    public void The_mock_bar_when_only_staged_files_exist()
    {
        var state = new ChangesKeyState(RowCount: 2, SelectedSide: ChangeSide.Staged, HasUnstaged: false, HasStaged: true, HasHead: true,
            HasStash: false, HasUndo: false);

        Assert.Equal("Tab branches  ↑↓ file  Space unstage  U unstage all  c commit  A amend  z stash  Esc quit",
            Bar(KeyBarView.ChangesItems(state)));
    }

    [Fact]
    public void The_mock_bar_with_nothing_changed_and_no_stash_or_undo_step()
    {
        var state = new ChangesKeyState(RowCount: 0, SelectedSide: null, HasUnstaged: false, HasStaged: false, HasHead: true,
            HasStash: false, HasUndo: false);

        Assert.Equal("Tab branches  A amend  Esc quit", Bar(KeyBarView.ChangesItems(state)));
    }

    [Fact]
    public void Space_label_follows_the_selected_row()
    {
        var kept = KeyBarView.FitItems(KeyBarView.ChangesItems(AllChanges with { SelectedSide = ChangeSide.Staged }), 200, out _);

        Assert.Equal(" unstage", kept.Single(i => i.Key == "Space").Label);
    }

    // ----- Branches -----

    [Fact]
    public void Branches_bar_on_the_current_branch_has_no_merge()
    {
        // The mock: main (current) selected.
        var state = new BranchesKeyState(BranchCount: 3, CanMerge: false, HasUndo: false);

        Assert.Equal("Tab changes  ↑↓ select  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Branches_bar_for_a_branch_ahead_of_main_has_merge()
    {
        var state = new BranchesKeyState(BranchCount: 3, CanMerge: true, HasUndo: false);

        Assert.Equal("Tab changes  ↑↓ select  m merge  Esc quit", Bar(KeyBarView.BranchesItems(state)));
    }

    [Fact]
    public void Branches_bar_shows_undo_only_with_a_step_and_up_down_only_with_two_branches()
    {
        Assert.Equal("Tab changes  m merge  u undo  Esc quit",
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
    public void Branches_bar_lists_r_rebase_only_when_r_can_rebase()
    {
        var with = BranchesKeyState.Empty with { BranchCount = 3, CanRebase = true };
        var without = BranchesKeyState.Empty with { BranchCount = 3, CanRebase = false };

        Assert.Contains("r rebase", Bar(KeyBarView.BranchesItems(with)));
        Assert.DoesNotContain("r rebase", Bar(KeyBarView.BranchesItems(without)));
    }

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
}
