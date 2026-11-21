using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// g's frames on the lane graph. G is the sandbox graph, built in memory. Each frame is compared with the card's own graph (the done
/// state) or with the preview it starts from, cell by cell, at fixed times. The grid is 86 × 27 unless a test says otherwise.
/// </summary>
public sealed class LaneMergeAnimationTests
{
    private static readonly LaneGraph G = SandboxLaneGraph.Build();

    private const string Hash = "1234567890abcdef";
    private const string Merge = "feature/merge-commit";
    private const string FastForward = "feature/fast-forward";
    private const string Conflict = "feature/conflict";

    private static GraphLayout Layout(string selected) => Layouts.Diverged() with { Selected = selected };

    // The same layout with one commit that the branch brings, so the sheet has an incoming list.
    private static GraphLayout WithIncoming(string selected) =>
        Layout(selected) with { SelectedNew = new List<CommitInfo> { Layouts.C("acf4dee000", subject: "Add other file") } };

    private static MergeCard MergedCard() =>
        new(DigestKind.Merged, Merge, "main", Hash, "be0126eabcdef0123456789", 1, [], "merge commit created", null, true,
            CanPlay: true, CanUndo: true);

    private static MergeCard FastForwardCard() =>
        new(DigestKind.FastForwarded, FastForward, "main", "8f0068a1234567890abcdef", "be0126eabcdef0123456789", 1, [], "fast-forward",
            null, true, CanPlay: true, CanUndo: true);

    private static MergeCard ConflictCard() =>
        new(DigestKind.Stopped, Conflict, "main", null, "be0126eabcdef0123456789", 1, ["app.txt"], "merge aborted",
            "git merge --abort", true, CanPlay: true, CanUndo: false);

    private static LaneMergeAnimation Anim(MergeCard card, string selected) =>
        new(new DigestSheetInput(Layout(selected), G, card));

    private static ArtGrid F(LaneMergeAnimation anim, long t) => anim.GraphAt(t, 86, 27, Glyphs.Unicode).Grid;

    // The card's own graph at 86 × 27: the done state of the kind, drawn the way the card draws it.
    private static ArtGrid Done(GhostKind kind, string selected, bool verified = true)
    {
        var p = PreviewSheets.FitGraph(G, selected, kind, 86, 27, Hash, verified);
        return LaneScene.Render(p, Glyphs.Unicode, Hash);
    }

    // The cells that differ between two grids of the same size, as "x,y", in reading order.
    private static List<string> Differences(ArtGrid a, ArtGrid b)
    {
        var list = new List<string>();
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (!a[x, y].Equals(b[x, y]))
                    list.Add(x + "," + y);
            }
        }

        return list;
    }

    // n characters of a row, from x.
    private static string Text(ArtGrid g, int x, int y, int n)
    {
        var s = "";
        for (var i = 0; i < n; i++)
            s += g[x + i, y].Ch;
        return s;
    }

    private static Ink MergeInk => BranchInks.Of(G.Lanes[G.LaneOf(Merge)].ColorLane);

    [Fact]
    public void Phase_ease_steps_and_pulse()
    {
        Assert.Equal(0.0, LaneMergeAnimation.Phase(-5, 0, 10));
        Assert.Equal(1.0, LaneMergeAnimation.Phase(20, 0, 10));
        Assert.Equal(0.5, LaneMergeAnimation.Phase(5, 0, 10));
        Assert.Equal(0.5, LaneMergeAnimation.Ease(0.5));
        Assert.Equal(23, LaneMergeAnimation.Steps(0.5, 46));
        Assert.Equal(16, LaneMergeAnimation.Steps(0.5, 31));
        Assert.Equal(0, LaneMergeAnimation.PulseIndex(0, 700, 3));
        Assert.Equal(1, LaneMergeAnimation.PulseIndex(350, 700, 3));
        Assert.Equal(2, LaneMergeAnimation.PulseIndex(699, 700, 3));
        Assert.Equal(-1, LaneMergeAnimation.PulseIndex(700, 700, 3));
        Assert.Equal(-1, LaneMergeAnimation.PulseIndex(0, 700, 0));
    }

    [Fact]
    public void Total_and_CanPlay_by_kind()
    {
        Assert.Equal(2600L, Anim(MergedCard(), Merge).Total);
        Assert.Equal(2300L, Anim(FastForwardCard(), FastForward).Total);
        Assert.Equal(2800L, Anim(ConflictCard(), Conflict).Total);

        var upToDate = new MergeCard(DigestKind.UpToDate, Merge, "main", null, null, 0, [], "already up to date", null, true,
            CanPlay: false, CanUndo: false);
        Assert.Equal(0L, Anim(upToDate, Merge).Total);
        Assert.False(LaneMergeAnimation.CanPlay(upToDate));
        Assert.True(LaneMergeAnimation.CanPlay(MergedCard()));
    }

    // At 0 the tip's dot is a bright pulse and its hash is written again on a selection background; nothing else changes.
    [Fact]
    public void Merge_at_0_pulses_the_tip()
    {
        var frame = F(Anim(MergedCard(), Merge), 0);
        var preview = Done(GhostKind.Merge, Merge);

        Assert.Equal('◉', frame[31, 16].Ch);
        Assert.Equal(new Style(Ink.Fg, true), frame[31, 16].Style);
        Assert.Equal("acf4dee", Text(frame, 28, 15, 7));
        for (var x = 28; x <= 34; x++)
        {
            Assert.Equal(new Style(Ink.Fg, true), frame[x, 15].Style);
            Assert.Equal(CellBg.Sel, frame[x, 15].Bg);
        }

        Assert.Equal(8, Differences(frame, preview).Count);
    }

    // At 1100 the trail is half way: the merge line runs to the ghost's column with a dot at its head, the base's dashes turn solid
    // to the same point, and the ghost is still the preview's.
    [Fact]
    public void Merge_at_1100_runs_half_way()
    {
        var frame = F(Anim(MergedCard(), Merge), 1100);

        Assert.Equal(new string('─', 22), Text(frame, 32, 16, 22));
        Assert.Equal('●', frame[54, 16].Ch);
        Assert.Equal(new Style(Ink.Fg, true), frame[54, 16].Style);
        Assert.Equal(new string('┄', 8), Text(frame, 55, 16, 8));

        Assert.Equal(new string('─', 16), Text(frame, 32, 1, 16));
        Assert.Equal(new string('┄', 15), Text(frame, 48, 1, 15));
        for (var y = 2; y <= 15; y++)
            Assert.Equal('┆', frame[63, y].Ch);

        Assert.Equal('◌', frame[63, 1].Ch);
        Assert.Equal("new merge commit", Text(frame, 65, 1, 16));
        Assert.Equal(MergeInk, frame[40, 16].Style.Ink);
    }

    // At 1700 the ghost is half filled, its label has typed ten of its twenty characters, and the new hash and the chip are not drawn yet.
    [Fact]
    public void Merge_at_1700_fills_the_ghost()
    {
        var frame = F(Anim(MergedCard(), Merge), 1700);
        var done = Done(GhostKind.Merged, Merge);

        Assert.Equal('◍', frame[63, 1].Ch);
        Assert.Equal(new Style(Ink.Mrg), frame[63, 1].Style);
        Assert.Equal("merge comm", Text(frame, 65, 1, 10));
        Assert.Equal(' ', frame[75, 1].Ch);
        for (var x = 60; x <= 66; x++)
            Assert.Equal(' ', frame[x, 0].Ch);
        for (var x = 65; x <= 70; x++)
            Assert.Equal(CellBg.None, frame[x, 2].Bg);

        // Only the ghost, its label, the hash above it and the chip under it differ from the card.
        foreach (var cell in Differences(frame, done))
        {
            var parts = cell.Split(',');
            var x = int.Parse(parts[0]);
            var y = int.Parse(parts[1]);
            var inGhost = x == 63 && y == 1;
            var inLabel = y == 1 && x >= 65;
            var inHash = y == 0 && x >= 60 && x <= 66;
            var inChip = y == 2 && x >= 65 && x <= 70;
            Assert.True(inGhost || inLabel || inHash || inChip, "unexpected difference at " + cell);
        }
    }

    // At 2100 the label is typed, the hash is back, and the main chip is half typed (its first three cells).
    [Fact]
    public void Merge_at_2100_types_the_chip()
    {
        var frame = F(Anim(MergedCard(), Merge), 2100);

        Assert.Equal("1234567", Text(frame, 60, 0, 7));
        Assert.Equal("merge commit 1234567", Text(frame, 65, 1, 20));
        Assert.Equal(" ma", Text(frame, 65, 2, 3));
        for (var x = 65; x <= 67; x++)
            Assert.Equal(CellBg.Chip, frame[x, 2].Bg);
        for (var x = 68; x <= 70; x++)
        {
            Assert.Equal(' ', frame[x, 2].Ch);
            Assert.Equal(CellBg.None, frame[x, 2].Bg);
        }
    }

    // From 2300 on, and at the end, the frame is the card's own graph.
    [Fact]
    public void Merge_from_2300_is_the_done_graph()
    {
        var anim = Anim(MergedCard(), Merge);
        var done = Done(GhostKind.Merged, Merge);

        Assert.Empty(Differences(F(anim, 2300), done));
        Assert.Empty(Differences(F(anim, 2599), done));
        Assert.Empty(Differences(F(anim, 2600), done));
    }

    // The fast-forward's pulse is on the branch's commit, with its hash above it.
    [Fact]
    public void Ff_at_300_pulses()
    {
        var frame = F(Anim(FastForwardCard(), FastForward), 300);

        Assert.Equal('◉', frame[39, 4].Ch);
        Assert.Equal("8f0068a", Text(frame, 36, 3, 7));
    }

    // Halfway up, the branch's lane sits on row 3 between the base's row and its own, drawn with no dashes, in its own colour.
    [Fact]
    public void Ff_at_1050_is_half_lifted()
    {
        var frame = F(Anim(FastForwardCard(), FastForward), 1050);

        Assert.Equal(Sp(23) + "│" + Sp(7) + "├───────●", Row(frame, 3));
        Assert.Equal("8f0068a", Text(frame, 36, 2, 7));
        Assert.Equal(" fast-forward          │       │", Row(frame, 4));
        Assert.False(RowHas(frame, '┄'));
        Assert.False(RowHas(frame, '⇢'));
        Assert.Equal(BranchInks.Of(G.Lanes[G.LaneOf(FastForward)].ColorLane), frame[39, 3].Style.Ink);
    }

    // At 1700 the base's chip is half typed after the branch's tip, on the base row.
    [Fact]
    public void Ff_at_1700_types_the_chip()
    {
        var frame = F(Anim(FastForwardCard(), FastForward), 1700);

        Assert.Equal(" ma", Text(frame, 41, 1, 3));
        for (var x = 41; x <= 43; x++)
            Assert.Equal(CellBg.Chip, frame[x, 1].Bg);
        for (var x = 44; x <= 46; x++)
            Assert.Equal(CellBg.None, frame[x, 1].Bg);
    }

    // From 1900 on the frame is the fast-forward's done graph (the golden of LaneSceneTests.Golden_fast_forwarded_wide, at 86 × 27).
    [Fact]
    public void Ff_from_1900_is_the_done_graph()
    {
        var anim = Anim(FastForwardCard(), FastForward);
        var done = Done(GhostKind.FastForwarded, FastForward);

        Assert.Empty(Differences(F(anim, 1900), done));
        Assert.Empty(Differences(F(anim, 2200), done));
    }

    // At 900 the trail runs red along the conflict's row, with the dot at its head, and the ghost says "no commit".
    [Fact]
    public void Conflict_at_900_runs_red()
    {
        var frame = F(Anim(ConflictCard(), Conflict), 900);

        Assert.Equal(new string('─', 21), Text(frame, 32, 13, 21));
        Assert.Equal(new Style(Ink.Bad), frame[32, 13].Style);
        Assert.Equal('●', frame[53, 13].Ch);
        Assert.Equal(new Style(Ink.Fg, true), frame[53, 13].Style);
        Assert.Equal("no commit", Text(frame, 65, 1, 9));
    }

    // The ✕ blinks with the conflicting files: on at 1350, off at 1450 (a plain blink, no colour change), the trail solid in both.
    [Fact]
    public void Conflict_flash_on_and_off()
    {
        var anim = Anim(ConflictCard(), Conflict);
        var on = F(anim, 1350);
        var off = F(anim, 1450);

        Assert.Equal('✕', on[63, 1].Ch);
        Assert.Equal(new Style(Ink.Bad, true), on[63, 1].Style);
        Assert.Equal(' ', off[63, 1].Ch);
        Assert.Equal("app.txt conflicts", Text(on, 65, 1, 17));
        Assert.Equal("app.txt conflicts", Text(off, 65, 1, 17));

        foreach (var grid in new[] { on, off })
        {
            Assert.Equal(new string('─', 31), Text(grid, 32, 13, 31));
            Assert.Equal('╯', grid[63, 13].Ch);
        }
    }

    // The trail retreats while the label says what put the repo back: at 2300 the same as 900 with that label; at 2700 no solid trail.
    [Fact]
    public void Conflict_retreats()
    {
        var anim = Anim(ConflictCard(), Conflict);
        var at900 = F(anim, 900);
        var at2300 = F(anim, 2300);
        var at2700 = F(anim, 2700);

        Assert.Equal("git merge --abort", Text(at2300, 65, 1, 17));
        foreach (var cell in Differences(at2300, at900))
        {
            var parts = cell.Split(',');
            Assert.Equal("1", parts[1]);
            Assert.True(int.Parse(parts[0]) >= 65, "unexpected difference at " + cell);
        }

        Assert.Equal("git merge --abort", Text(at2700, 65, 1, 17));
        Assert.Equal('┄', at2700[40, 13].Ch);
    }

    // From 2800 on the frame is the stopped card's graph.
    [Fact]
    public void Conflict_from_2800_is_stopped()
    {
        var anim = Anim(ConflictCard(), Conflict);
        var done = Done(GhostKind.Stopped, Conflict);

        Assert.Empty(Differences(F(anim, 2800), done));
        Assert.Empty(Differences(F(anim, 3000), done));
    }

    // Each commit of the picks branch pulses in turn, oldest (leftmost) first: 089eae5, then 52b7690, then f6d6964.
    [Fact]
    public void Picks_pulses_each_commit()
    {
        var anim = Anim(MergedCard(), "feature/picks");
        var p = PreviewSheets.FitGraph(G, "feature/picks", GhostKind.Merge, 86, 27, Hash, true);
        var lane = G.Lanes[G.LaneOf("feature/picks")];
        Assert.Equal("089eae5", lane.Commits[0].ShortHash);
        Assert.Equal("52b7690", lane.Commits[1].ShortHash);
        Assert.Equal("f6d6964", lane.Commits[2].ShortHash);

        var y = p.YOf(G.LaneOf("feature/picks"));
        Assert.Equal('◉', F(anim, 0)[p.XOf(lane.Commits[0].Column), y].Ch);
        Assert.Equal('◉', F(anim, 350)[p.XOf(lane.Commits[1].Column), y].Ch);
        Assert.Equal('◉', F(anim, 699)[p.XOf(lane.Commits[2].Column), y].Ch);
    }

    // At 80 × 24 (a 47-column grid, 7 rows) there is no hash row and no chip: the frames still draw, with no chip cells.
    [Fact]
    public void Small_pane_plays_without_hashes()
    {
        var anim = Anim(MergedCard(), Merge);
        foreach (var t in new long[] { 0, 1100, 1700, 2100 })
        {
            var frame = anim.GraphAt(t, 47, 7, Glyphs.Unicode).Grid;
            Assert.Equal(47, frame.Width);
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                    Assert.NotEqual(CellBg.Chip, frame[x, y].Bg);
            }
        }
    }

    // A graph with no lanes has no frames: every time gives an empty grid.
    [Fact]
    public void Empty_graph_has_no_frames()
    {
        var anim = new LaneMergeAnimation(new DigestSheetInput(Layout(Merge), LaneGraph.Empty, MergedCard()));
        foreach (var t in new long[] { 0, 1100, 2100, 2700 })
            Assert.Equal(0, anim.GraphAt(t, 86, 27, Glyphs.Unicode).Grid.Height);
    }

    // Every size and time draws without an exception, for the three kinds.
    [Fact]
    public void Every_frame_draws()
    {
        var anims = new[]
        {
            Anim(MergedCard(), Merge),
            Anim(FastForwardCard(), FastForward),
            Anim(ConflictCard(), Conflict),
        };
        var sizes = new[] { (86, 27), (47, 7), (31, 8), (12, 1), (11, 5) };
        foreach (var anim in anims)
        {
            for (var t = 0L; t <= anim.Total + 100; t += 50)
            {
                foreach (var size in sizes)
                {
                    var grid = anim.GraphAt(t, size.Item1, size.Item2, Glyphs.Unicode).Grid;

                    // Narrower than a graph can be drawn (12 columns), the placement is empty and the grid is 0 wide.
                    Assert.True(grid.Width == size.Item1 || grid.Width == 0);
                }
            }
        }
    }

    // At 1500 the trail is still full, so the frame is the same as at 1499; the ghost starts to fill only from 1501 on.
    [Fact]
    public void Merge_at_1500_is_still_the_full_trail()
    {
        var anim = Anim(MergedCard(), Merge);
        Assert.Empty(Differences(F(anim, 1499), F(anim, 1500)));
        Assert.NotEmpty(Differences(F(anim, 1500), F(anim, 1501)));
    }

    // A failed or refused card has no play: no kind, no total.
    [Fact]
    public void A_failed_or_refused_card_has_no_play()
    {
        var failed = new MergeCard(DigestKind.Failed, Merge, "main", null, null, 0, [], "boom", null, true, CanPlay: false, CanUndo: false);
        var refused = DigestText.Refused(Merge, "main", "HEAD is detached");

        Assert.False(LaneMergeAnimation.CanPlay(failed));
        Assert.False(LaneMergeAnimation.CanPlay(refused));
        Assert.Equal(0L, Anim(failed, Merge).Total);
        Assert.Equal(0L, Anim(refused, Merge).Total);
    }

    // The play's sheet from its total on is the card's own sheet, line for line and cell for cell, for each kind.
    [Fact]
    public void Sheet_ends_on_the_card()
    {
        var plays = new[] { (MergedCard(), Merge), (FastForwardCard(), FastForward), (ConflictCard(), Conflict) };
        foreach (var play in plays)
        {
            var input = new DigestSheetInput(WithIncoming(play.Item2), G, play.Item1);
            var anim = new LaneMergeAnimation(input);
            AssertSameSheet(PreviewSheets.BuildDigest(input, 85, 30, Glyphs.Unicode), anim.Sheet(anim.Total, 85, 30, Glyphs.Unicode));
        }
    }

    // At 0 the play's row 0 is the spinner and the first step, row 1 is the command, and the list keeps its usual title.
    [Fact]
    public void Sheet_at_0_has_the_running_line_the_command_and_the_list()
    {
        var anim = new LaneMergeAnimation(new DigestSheetInput(WithIncoming(Merge), G, MergedCard()));
        var sheet = anim.Sheet(0, 85, 30, Glyphs.Unicode);

        Assert.Equal("⠋ reading 1 commit from feature/merge-commit", SheetRow(sheet, 0));
        Assert.Equal("$ git merge feature/merge-commit", SheetRow(sheet, 1));
        Assert.Contains(sheet.Lines, line => LineText(line) == PreviewText.IncomingTitle);
    }

    // With three rows only the head lines are drawn: no graph and no list.
    [Fact]
    public void A_sheet_of_three_rows_has_only_the_head()
    {
        var sheet = Anim(MergedCard(), Merge).Sheet(0, 85, 3, Glyphs.Unicode);

        Assert.All(sheet.Lines, line => Assert.True(line.Row <= 2));
        Assert.Equal(0, sheet.Graph.Height);
    }

    // Every size and time gives a sheet, for the three kinds; the graph is always one column wider than the text.
    [Fact]
    public void Every_sheet_draws()
    {
        var anims = new[]
        {
            Anim(MergedCard(), Merge),
            Anim(FastForwardCard(), FastForward),
            Anim(ConflictCard(), Conflict),
        };
        var sizes = new[] { (85, 30), (46, 7), (12, 4), (85, 3) };
        foreach (var anim in anims)
        {
            for (var t = 0L; t <= anim.Total + 100; t += 50)
            {
                foreach (var size in sizes)
                {
                    var sheet = anim.Sheet(t, size.Item1, size.Item2, Glyphs.Unicode);
                    Assert.Equal(size.Item1 + 1, sheet.Graph.Width);
                }
            }
        }
    }

    // Two sheets are the same: the same lines with the same segments, and the same grid cell for cell.
    private static void AssertSameSheet(PreviewSheet expected, PreviewSheet actual)
    {
        Assert.Equal(expected.GraphRow, actual.GraphRow);
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);
        for (var i = 0; i < expected.Lines.Count; i++)
        {
            Assert.Equal(expected.Lines[i].Row, actual.Lines[i].Row);
            Assert.Equal(expected.Lines[i].Segs, actual.Lines[i].Segs);
        }

        Assert.Equal(expected.Graph.Width, actual.Graph.Width);
        Assert.Equal(expected.Graph.Height, actual.Graph.Height);
        Assert.Empty(Differences(expected.Graph, actual.Graph));
    }

    // The text of one row of a sheet, its segments joined.
    private static string SheetRow(PreviewSheet sheet, int row)
    {
        foreach (var line in sheet.Lines)
        {
            if (line.Row == row)
                return LineText(line);
        }

        return "";
    }

    // The text of a sheet line, its segments joined.
    private static string LineText(SheetLine line)
    {
        var text = "";
        foreach (var seg in line.Segs)
            text += seg.Text;
        return text;
    }

    // The spaces before a label or a graph, so a golden row can say how wide a gap is.
    private static string Sp(int n) => new string(' ', n);

    // One row of a grid, trailing blanks cut.
    private static string Row(ArtGrid g, int y)
    {
        var s = "";
        for (var x = 0; x < g.Width; x++)
            s += g[x, y].Ch;
        return s.TrimEnd();
    }

    private static bool RowHas(ArtGrid g, char ch)
    {
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
            {
                if (g[x, y].Ch == ch)
                    return true;
            }
        }

        return false;
    }
}
