using graftui.Model;
using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

public class KeyBarFitTests
{
    private static readonly ChangesKeyState AllChanges = new(RowCount: 5, SelectedSide: ChangeSide.Unstaged, HasUnstaged: true, HasStaged: true,
        HasHead: true, HasStash: true, HasUndo: true);

    private static readonly BranchesKeyState AllBranches = new(BranchCount: 3, CanMerge: true, HasUndo: true);

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

    [Fact]
    public void Branches_bar_at_40_columns_keeps_tab_merge_and_esc()
    {
        // At 64 columns every Branches key fits, so the narrow case is the one that drops keys.
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(AllBranches), 40, out _);

        Assert.Equal(new[] { "Tab", "m", "Esc" }, Keys(kept));
    }

    [Fact]
    public void Branches_bar_at_64_columns_keeps_every_key()
    {
        var all = KeyBarView.BranchesItems(AllBranches);
        var kept = KeyBarView.FitItems(all, 64, out var separator);

        Assert.Equal(all.Select(i => i.Key), Keys(kept));
        Assert.Equal(2, separator);
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
}
