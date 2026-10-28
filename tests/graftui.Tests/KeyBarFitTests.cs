using graftui.Ui.Views;

namespace graftui.Tests;

public class KeyBarFitTests
{
    private static IEnumerable<string> Keys(IReadOnlyList<(string Key, string Label)> items) => items.Select(i => i.Key);

    [Fact]
    public void Changes_bar_at_80_columns_keeps_space_c_x_z_Z_u_and_esc_in_order()
    {
        var kept = KeyBarView.FitItems(KeyBarView.ChangesItems("stage"), 80, out var separator);

        Assert.Equal(new[] { "Space", "c", "x", "z", "Z", "u", "Esc" }, Keys(kept));
        Assert.Equal(1, separator);
    }

    [Fact]
    public void Changes_bar_at_200_columns_keeps_everything_with_two_spaces()
    {
        var all = KeyBarView.ChangesItems("stage");
        var kept = KeyBarView.FitItems(all, 200, out var separator);

        Assert.Equal(all.Select(i => i.Key), Keys(kept));
        Assert.Equal(2, separator);
    }

    [Fact]
    public void Branches_bar_at_40_columns_keeps_merge_undo_and_esc()
    {
        // At 64 columns every Branches key fits, so the narrow case is the one that drops keys.
        var kept = KeyBarView.FitItems(KeyBarView.BranchesItems(), 40, out _);

        Assert.Equal(new[] { "m", "u", "Esc" }, Keys(kept));
    }

    [Fact]
    public void Branches_bar_at_64_columns_keeps_every_key()
    {
        var all = KeyBarView.BranchesItems();
        var kept = KeyBarView.FitItems(all, 64, out var separator);

        Assert.Equal(all.Select(i => i.Key), Keys(kept));
        Assert.Equal(2, separator);
    }

    [Fact]
    public void A_kept_bar_never_outgrows_the_width_it_was_given()
    {
        foreach (var width in new[] { 30, 40, 60, 80, 100 })
        {
            var kept = KeyBarView.FitItems(KeyBarView.ChangesItems("unstage"), width, out var separator);
            var used = kept.Sum(i => (" " + i.Key + " ").Length + i.Label.Length) + separator * (kept.Count - 1) + 2;

            Assert.True(used <= width || kept.Count == 1, $"width {width}: {used} columns for {kept.Count} items");
        }
    }

    [Fact]
    public void Space_label_follows_the_selected_row()
    {
        var kept = KeyBarView.FitItems(KeyBarView.ChangesItems("unstage"), 200, out _);

        Assert.Equal(" unstage", kept.Single(i => i.Key == "Space").Label);
    }
}
