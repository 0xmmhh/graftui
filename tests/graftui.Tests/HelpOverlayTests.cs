using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>
/// The ? help popup: its title and rows for every tab and mode, and the box it draws. Pure text and grids, no terminal.
/// </summary>
public class HelpOverlayTests
{
    private static KeyBarView BranchesBar() => new() { Mode = KeyBarMode.Branches };

    private static KeyBarView ChangesBar(ChangesKeyState state) => new() { Mode = KeyBarMode.Changes, ChangesState = state };

    private static IEnumerable<string> RowKeys(IReadOnlyList<HelpRow> rows) => rows.Select(r => r.Key);

    private static readonly ChangesKeyState StagedSelected = new(RowCount: 3, SelectedSide: ChangeSide.Staged, HasUnstaged: true,
        HasStaged: true, HasHead: true, HasStash: true, HasUndo: true);

    private static readonly ChangesKeyState UnstagedSelected = new(RowCount: 3, SelectedSide: ChangeSide.Unstaged, HasUnstaged: true,
        HasStaged: false, HasHead: true, HasStash: false, HasUndo: false);

    [Fact]
    public void The_branches_popup_lists_every_branch_key_with_its_title()
    {
        var named = HelpOverlay.ForBar(BranchesBar(), "Preview");

        Assert.Equal("Keys — Branches", named.Title);
        Assert.Equal(new[] { "Tab", "↑↓", "m", "s", "n", "R", "D", "r", "i", "u", "v", "Esc", "Ctrl+C" }, RowKeys(named.Rows));
    }

    [Fact]
    public void The_changes_popup_lists_every_change_key_with_its_title()
    {
        var named = HelpOverlay.ForBar(ChangesBar(StagedSelected), "Diff");

        Assert.Equal("Keys — Changes", named.Title);
        Assert.Equal(new[] { "Tab", "↑↓", "Space", "a", "U", "c", "A", "x", "z", "Z", "u", "v", "Esc", "Ctrl+C" },
            RowKeys(named.Rows));
    }

    [Fact]
    public void The_space_row_says_stage_or_unstage_by_the_selected_row()
    {
        var staged = HelpOverlay.ForBar(ChangesBar(StagedSelected), "").Rows.Single(r => r.Key == "Space");
        var unstaged = HelpOverlay.ForBar(ChangesBar(UnstagedSelected), "").Rows.Single(r => r.Key == "Space");
        var none = HelpOverlay.ForBar(ChangesBar(ChangesKeyState.Empty), "").Rows.Single(r => r.Key == "Space");

        Assert.Equal("unstage", staged.Text);
        Assert.Equal("stage", unstaged.Text);
        Assert.Equal("stage / unstage", none.Text);
    }

    [Fact]
    public void A_paused_branch_lists_the_pause_keys_instead()
    {
        var bar = BranchesBar();
        bar.BranchesState = BranchesKeyState.Empty with { Pause = PauseKind.Conflict };

        var named = HelpOverlay.ForBar(bar, "Paused");

        Assert.Equal("Keys — Branches", named.Title);
        Assert.Equal(new[] { "Enter", "S", "X", "Tab", "Esc" }, RowKeys(named.Rows));
    }

    [Fact]
    public void Only_a_conflict_can_be_skipped_and_an_edit_is_amended_first()
    {
        var skip = HelpOverlay.PauseRows(PauseKind.Conflict).Select(r => r.Key);
        var amend = HelpOverlay.PauseRows(PauseKind.Edit);

        Assert.Contains("S", skip);
        Assert.DoesNotContain("S", HelpOverlay.PauseRows(PauseKind.Reword).Select(r => r.Key));
        Assert.Equal("amend & continue", amend[0].Text);
        Assert.Equal("continue", HelpOverlay.PauseRows(PauseKind.Squash)[0].Text);
    }

    [Fact]
    public void The_commit_form_names_save_by_the_focused_field()
    {
        var subject = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Input, BodyFocused = false }, "Commit");
        var body = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Input, BodyFocused = true }, "Commit");

        Assert.Equal("Keys — Commit", subject.Title);
        Assert.Equal(new[] { "Enter", "Tab", "Ctrl+Enter", "Esc" }, RowKeys(subject.Rows));
        Assert.Equal(new[] { "Ctrl+J", "Tab", "Esc" }, RowKeys(body.Rows));
    }

    [Fact]
    public void The_message_box_names_the_enter_action_and_esc_cancels()
    {
        var stash = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Message, MessageEnterLabel = " stash" }, "");
        var create = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Message, MessageEnterLabel = " create" }, "");
        var rename = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Message, MessageEnterLabel = " rename" }, "");

        Assert.Equal("Keys — Stash", stash.Title);
        Assert.Equal("Keys — New branch", create.Title);
        Assert.Equal("Keys — Rename", rename.Title);
        Assert.Equal("stash", stash.Rows[0].Text);
    }

    [Fact]
    public void A_question_uses_its_own_yes_key_and_labels_with_the_art_title()
    {
        var bar = new KeyBarView { Mode = KeyBarMode.Ask, AskKey = "1", AskYesLabel = " yes, drop it", AskNoLabel = " keep" };

        var named = HelpOverlay.ForBar(bar, "Delete branch?");

        Assert.Equal("Keys — Delete branch?", named.Title);
        Assert.Equal(new[] { "1", "n", "Esc" }, RowKeys(named.Rows));
        Assert.Equal("yes, drop it", named.Rows[0].Text);
    }

    [Fact]
    public void The_confirm_and_todo_popups_name_their_screens()
    {
        var confirm = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Confirm }, "");
        var todo = HelpOverlay.ForBar(new KeyBarView { Mode = KeyBarMode.Todo }, "");

        Assert.Equal("Keys — Rebase", confirm.Title);
        Assert.Equal(new[] { "Enter", "Esc" }, RowKeys(confirm.Rows));
        Assert.Equal("Keys — Todo", todo.Title);
        Assert.Contains("Enter", RowKeys(todo.Rows));
        Assert.Contains("d", RowKeys(todo.Rows));
    }

    [Fact]
    public void The_frame_draws_a_box_with_two_columns_on_a_full_art()
    {
        var rows = HelpOverlay.BranchesRows();
        var geo = new ArtGeometry(60, 9, 6, 2, 6, 20);
        var grid = new ArtGrid(geo.Width, geo.Height);

        var frame = HelpOverlay.Frame(geo, grid, rows);
        var text = GridText(frame.Grid);

        Assert.Equal("? closes · Esc closes", frame.Caption!.Text);
        Assert.Contains("merge", text);
        Assert.Contains("rebase onto selected", text);
        Assert.Contains("┌", text);
        Assert.Contains("┐", text);
        Assert.Contains("└", text);
    }

    [Fact]
    public void The_frame_fits_a_short_art_with_a_note_naming_how_many_keys_are_hidden()
    {
        var rows = HelpOverlay.BranchesRows();
        var geo = new ArtGeometry(60, 5, 6, 2, 4, 20);
        var grid = new ArtGrid(geo.Width, geo.Height);

        var frame = HelpOverlay.Frame(geo, grid, rows);

        Assert.Contains("more: widen the window", GridText(frame.Grid));
    }

    [Fact]
    public void The_frame_asks_for_a_wider_window_when_even_one_column_cannot_fit()
    {
        var geo = new ArtGeometry(18, 9, 6, 2, 6, 20);
        var grid = new ArtGrid(geo.Width, geo.Height);

        var frame = HelpOverlay.Frame(geo, grid, HelpOverlay.BranchesRows());

        Assert.Equal("Widen the window to see every key", frame.Caption!.Text);
    }

    private static string GridText(ArtGrid grid)
    {
        var lines = new List<string>();
        for (var y = 0; y < grid.Height; y++)
        {
            var line = "";
            for (var x = 0; x < grid.Width; x++)
                line += grid[x, y].Ch;
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }
}
