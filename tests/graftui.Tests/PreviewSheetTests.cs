using System.Text;
using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The preview sheet: the head lines, the lane graph and the incoming list, drawn into a text dump. G is the sandbox graph built in
/// memory. The goldens are the 2b lane goldens, copied: the sheet's graph is the same drawing at the sheet's sizes.
/// </summary>
public sealed class PreviewSheetTests
{
    private static readonly LaneGraph G = SandboxLaneGraph.Build();

    // feature/merge-commit, one commit ahead of main, with its one incoming commit.
    private static GraphLayout MergeCommit() => Layouts.Diverged() with
    {
        Selected = "feature/merge-commit",
        SelectedTip = "acf4dee",
        SelectedNew = new List<CommitInfo> { Layouts.C("acf4dee000", subject: "Add other file") },
    };

    private static MergeForecast Clean() => new(ForecastKind.Clean, [], null, []);

    private static PreviewSheetInput Input(GraphLayout layout, MergeForecast? forecast) =>
        new(layout, forecast, G, true);

    private static PreviewSheet Build(PreviewSheetInput input, int textWidth, int rows) =>
        PreviewSheets.Build(input, textWidth, rows, Glyphs.Unicode);

    // The rows joined with newlines, so a golden can be written one row per line.
    private static string Rows(params string[] rows) => string.Join("\n", rows);

    // The sheet as text, the way the pane draws it: the grid is cw + 1 wide (column 0 is the pad column), a line's text starts at
    // column 1, cut to the text width. Rows are trimmed at the end and joined with newlines.
    private static string Dump(PreviewSheet s, int cw, int rows)
    {
        var width = cw + 1;
        var chars = new char[width, rows];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
                chars[x, y] = ' ';
        }

        foreach (var line in s.Lines)
        {
            if (line.Row >= rows)
                continue;

            var text = TextFit.Fit(string.Concat(line.Segs.Select(seg => seg.Text)), cw);
            for (var i = 0; i < text.Length; i++)
                chars[1 + i, line.Row] = text[i];
        }

        for (var y = 0; y < s.Graph.Height; y++)
        {
            var row = s.GraphRow + y;
            if (row >= rows)
                continue;

            for (var x = 0; x < s.Graph.Width && x < width; x++)
                chars[x, row] = s.Graph[x, y].Ch;
        }

        var result = new List<string>();
        for (var y = 0; y < rows; y++)
        {
            var row = new StringBuilder();
            for (var x = 0; x < width; x++)
                row.Append(chars[x, y]);
            result.Add(row.ToString().TrimEnd());
        }

        return Rows(result.ToArray());
    }

    // The graph's characters as one string, row by row (for "no ghost here" checks).
    private static string GraphText(PreviewSheet s)
    {
        var text = new StringBuilder();
        for (var y = 0; y < s.Graph.Height; y++)
        {
            for (var x = 0; x < s.Graph.Width; x++)
                text.Append(s.Graph[x, y].Ch);
        }

        return text.ToString();
    }

    private static readonly string[] Golden80Rows =
    [
        " main          ●────●────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄◌",
        " fast-forward       │    ├────●              ┆",
        " ff-to-merge        │    ╰────●─────────●    ┆",
        " side-for-ffm       │         ╰────●────╯    ┆",
        " conflict           ├────●                   ┆",
        " merge-commit       ├────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯",
        " picks              ╰────●────●────●",
    ];

    private static readonly string[] GoldenWideRows =
    [
        "            34e4a2e 8c5ceee be0126e",
        " main          ●───────●───────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄◌ new merge commit",
        "                       │       │                               ┆",
        "                       │       │    8f0068a                    ┆",
        " fast-forward          │       ├───────●                       ┆",
        "                       │       │                               ┆",
        "                       │       │    2b99ab2         810cc55    ┆",
        " ff-to-merge           │       ╰───────●───────────────●       ┆",
        "                       │               │               │       ┆",
        "                       │               │    df42801    │       ┆",
        " side-for-ffm          │               ╰───────●───────╯       ┆",
        "                       │                                       ┆",
        "                       │    30cc9f2                            ┆",
        " conflict              ├───────●                               ┆",
        "                       │                                       ┆",
        "                       │    acf4dee                            ┆",
        " merge-commit          ├───────●┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯",
        "                       │",
        "                       │    089eae5 52b7690 f6d6964",
        " picks                 ╰───────●───────●───────●",
    ];

    [Fact]
    public void Wide_sheet_has_the_forecast_command_graph_and_incoming_list()
    {
        var sheet = Build(Input(MergeCommit(), Clean()), 86, 35);

        // Head rows (forecast, command, blank), then the 2b wide golden from row 3, a blank, the title and the one commit.
        var head = new[]
        {
            " ✔ merges cleanly · creates a merge commit on main",
            " $ git merge feature/merge-commit",
            "",
        };
        var tail = new[]
        {
            "",
            " commits that would come in (newest first)",
            " acf4dee  Add other file",
        };
        var expected = Rows(head.Concat(GoldenWideRows).Concat(tail).ToArray()) + new string('\n', 9);

        Assert.Equal(expected, Dump(sheet, 86, 35));
        Assert.Equal(3, sheet.GraphRow);
        Assert.Equal(87, sheet.Graph.Width);
    }

    [Fact]
    public void The_sheet_of_an_80_column_screen_uses_the_closer_rows()
    {
        var sheet = Build(Input(MergeCommit(), Clean()), 46, 19);

        var head = new[]
        {
            " ✔ merges cleanly · creates a merge commit on …",
            " $ git merge feature/merge-commit",
            "",
        };
        var tail = new[]
        {
            "",
            " commits that would come in (newest first)",
            " acf4dee  Add other file",
        };
        var expected = Rows(head.Concat(Golden80Rows).Concat(tail).ToArray()) + new string('\n', 6);

        Assert.Equal(expected, Dump(sheet, 46, 19));
    }

    [Fact]
    public void The_graph_gives_up_spacing_for_the_list()
    {
        const string Branch = "feature/merge-commit";

        Assert.Equal(1, PreviewSheets.FitGraph(G, Branch, GhostKind.Merge, 47, 16).RowGap);
        Assert.Equal(3, PreviewSheets.FitGraph(G, Branch, GhostKind.Merge, 87, 32).RowGap);
    }

    [Fact]
    public void The_graph_keeps_its_rows_over_the_list()
    {
        const string Branch = "feature/merge-commit";

        // Nine rows: the full spacing needs seven rows at gap 1, leaving two, under the list's three. The closer spacing would keep
        // only three lanes at four rows, so the full placement is kept.
        var placement = PreviewSheets.FitGraph(G, Branch, GhostKind.Merge, 87, 9);
        Assert.Equal(7, placement.Rows.Count);
        Assert.Equal(7, placement.Height);
        Assert.Equal(1, placement.RowGap);
        Assert.Equal(3, LaneFit.Fit(G, Branch, GhostKind.Merge, 87, 4).Rows.Count);

        var sheet = Build(Input(MergeCommit(), Clean()), 86, 12);
        Assert.Equal(2, sheet.Lines.Count);
    }

    [Fact]
    public void The_layout_does_not_move_with_the_selection()
    {
        var sizes = new[] { (Width: 86, Rows: 35), (Width: 46, Rows: 19), (Width: 36, Rows: 17) };
        foreach (var size in sizes)
        {
            var budget = size.Rows - PreviewSheets.HeadRows;
            var reference = PreviewSheets.FitGraph(G, "main", GhostKind.None, size.Width + 1, budget);

            foreach (var branch in SandboxLaneGraph.Branches)
            {
                foreach (var ghost in new[] { GhostKind.None, GhostKind.Merge })
                {
                    var placement = PreviewSheets.FitGraph(G, branch.Name, ghost, size.Width + 1, budget);
                    Assert.Equal(reference.RowGap, placement.RowGap);
                    Assert.Equal(reference.Step, placement.Step);
                    Assert.Equal(reference.RowY, placement.RowY);
                }
            }
        }
    }

    [Fact]
    public void The_current_branch_has_no_command_no_ghost_and_no_list()
    {
        var layout = MergeCommit() with { Selected = "main", Kind = GraphKind.Same };
        var sheet = Build(Input(layout, Clean()), 86, 35);

        Assert.Single(sheet.Lines);
        Assert.Equal(0, sheet.Lines[0].Row);
        Assert.Equal("main is checked out: pick a branch on the left to preview its merge.", string.Concat(sheet.Lines[0].Segs.Select(s => s.Text)));
        var graph = GraphText(sheet);
        Assert.DoesNotContain('◌', graph);
        Assert.DoesNotContain('⇢', graph);
        Assert.DoesNotContain('✕', graph);
        Assert.DoesNotContain(sheet.Lines, line => line.Segs.Any(seg => seg.Text == PreviewText.IncomingTitle));
    }

    [Fact]
    public void Many_commits_end_with_a_count_of_the_rest()
    {
        var commits = new List<CommitInfo>();
        for (var i = 0; i < 20; i++)
            commits.Add(Layouts.C("c" + i.ToString("00000000"), subject: "commit " + i));
        var layout = MergeCommit() with { Ahead = 25, SelectedNew = commits };

        var sheet = Build(Input(layout, Clean()), 46, 19);
        var last = sheet.Lines[sheet.Lines.Count - 1];

        // Eight rows for the list: the title, six commits and "+19 more" (25 commits, six shown).
        Assert.Equal(18, last.Row);
        Assert.Equal("+19 more", string.Concat(last.Segs.Select(s => s.Text)));
        Assert.Equal(Ink.Dim, last.Segs[0].Style.Ink);
    }

    [Fact]
    public void A_capped_commit_list_says_how_many_are_not_shown_even_when_all_of_it_fits()
    {
        // git listed 50 of the 51 commits. A tall list has room for all 50, but the last row still says one more is left out.
        var commits = new List<CommitInfo>();
        for (var i = 0; i < 50; i++)
            commits.Add(Layouts.C("c" + i.ToString("00000000"), subject: "commit " + i));
        var layout = MergeCommit() with { Ahead = 51, SelectedNew = commits };

        var lines = PreviewSheets.Incoming(layout, 0, 60);
        var last = lines[lines.Count - 1];

        Assert.Equal(52, lines.Count);
        Assert.Equal(51, last.Row);
        Assert.Equal("+1 more", string.Concat(last.Segs.Select(s => s.Text)));
    }

    [Fact]
    public void A_too_short_pane_has_no_graph_and_no_rows_for_the_rest()
    {
        var short3 = Build(Input(MergeCommit(), Clean()), 86, 3);
        Assert.Equal(0, short3.Graph.Height);
        Assert.Equal(2, short3.Lines.Count);

        var none = Build(Input(MergeCommit(), Clean()), 86, 0);
        Assert.Empty(none.Lines);
        Assert.Equal(0, none.Graph.Height);
    }

    [Fact]
    public void A_fast_forward_sheet_says_how_many_commits_come_in_and_that_main_moves_there()
    {
        var layout = MergeCommit() with { Selected = "feature/ff-to-merge", Kind = GraphKind.FastForward, Ahead = 3 };
        var forecast = new MergeForecast(ForecastKind.FastForward, [], null, []);
        var sheet = Build(Input(layout, forecast), 86, 35);
        var rows = Dump(sheet, 86, 35).Split('\n');

        Assert.Equal("⇢ fast-forwards · 3 commits, no merge commit", string.Concat(sheet.Lines[0].Segs.Select(s => s.Text)));
        Assert.Equal(GhostKind.FastForward, PreviewText.Ghost(layout, forecast));
        Assert.Contains(rows, row => row.StartsWith(" main") && row.EndsWith("⇢ main moves here"));
    }

    [Fact]
    public void A_conflict_sheet_says_which_file_and_that_main_is_unchanged()
    {
        var layout = MergeCommit() with { Selected = "feature/conflict", SelectedTip = "30cc9f2", SelectedNew = new List<CommitInfo>() };
        var forecast = new MergeForecast(ForecastKind.Conflict, [new ForecastFile("app.txt", ForecastConflict.Content)], null, []);
        var sheet = Build(Input(layout, forecast), 86, 35);
        var rows = Dump(sheet, 86, 35).Split('\n');

        Assert.Equal("✕ would conflict in app.txt · the merge would be aborted, main unchanged", string.Concat(sheet.Lines[0].Segs.Select(s => s.Text)));
        Assert.Contains(rows, row => row.StartsWith(" main") && row.EndsWith("✕ no commit"));
    }
}
