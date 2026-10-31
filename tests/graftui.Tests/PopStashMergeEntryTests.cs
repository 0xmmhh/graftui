using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Z over graftui's own stash-merge entry pops with --index, so staged changes stay staged. When git refuses --index up front, the plain
/// pop is used only if it loses no staged content; otherwise Z refuses, changes nothing and keeps the entry. A plain pop says the staged
/// changes came back unstaged. Any other entry is popped plainly, as before.
/// </summary>
public class PopStashMergeEntryTests
{
    private const string StashMergeMessage = "graftui: before merging x into main";

    private static PopOutcome Pop(TempRepo repo)
    {
        var op = new PopOperation(repo.Git);
        var request = new PopRequest(null);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static IReadOnlyList<string> Lines(TempRepo repo) =>
        repo.Status().Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList();

    /// <summary>f.txt is staged as B and then edited to C. The stash keeps both.</summary>
    private static TempRepo StagedThenEdited(string message)
    {
        var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Write("f.txt", "B\n");
        repo.Run("add", "f.txt");
        repo.Write("f.txt", "C\n");
        repo.Run("stash", "push", "-m", message);
        return repo;
    }

    [Fact]
    public void A_stash_merge_entry_pops_with_its_index_so_the_staged_change_stays_staged()
    {
        using var repo = StagedThenEdited(StashMergeMessage);

        var outcome = Pop(repo);

        Assert.Equal(PopKind.Popped, outcome.Kind);
        Assert.False(outcome.StagedCameBackUnstaged);
        Assert.Equal(new[] { "MM f.txt" }, Lines(repo));
        Assert.Equal("f.txt", repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal("C\n", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.Equal("", repo.Run("stash", "list"));
    }

    [Fact]
    public void A_stash_merge_entry_whose_index_cannot_apply_is_refused_when_the_plain_pop_would_lose_the_staged_change()
    {
        // The stash staged B over A and then went back to A in the tree. HEAD then moves on f, so `pop --index` cannot apply the
        // staged B ("Try without --index", tree untouched). The plain pop would apply the tree only and drop B, so Z refuses.
        using var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Write("f.txt", "B\n");
        repo.Run("add", "f.txt");
        repo.Write("f.txt", "A\n");
        repo.Run("stash", "push", "-m", StashMergeMessage);
        repo.Commit("f.txt", "D\n", "Move f on main");
        var stashed = repo.Run("rev-parse", "stash@{0}");
        var statusBefore = repo.Status();

        var outcome = Pop(repo);

        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.Equal(ChangeRefusals.StagedWouldBeLost(["f.txt"]), outcome.Message);
        Assert.False(outcome.StagedCameBackUnstaged);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal("D\n", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
        Assert.False(repo.Git.Run("rev-parse", "-q", "--verify", PopOperation.RescueRef).Success);

        // The failed `pop --index` is in the log as it ran; no plain pop was run.
        Assert.Contains(outcome.Commands, c => c.Args.SequenceEqual(["stash", "pop", "--index"]) && c.ExitCode != 0);
        Assert.DoesNotContain(outcome.Commands, c => c.Args.SequenceEqual(["stash", "pop"]));
    }

    [Fact]
    public void A_stash_merge_entry_whose_index_cannot_apply_pops_plainly_when_the_plain_pop_loses_nothing()
    {
        // f.txt and g.txt are staged, and their working versions equal the staged ones. HEAD then commits f.txt with the staged
        // content, so `pop --index` refuses ("Try without --index", tree untouched). The plain pop loses nothing, so it runs
        // and the staged change of g.txt comes back unstaged.
        using var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Commit("g.txt", "A\n", "Add g");
        repo.Write("f.txt", "B\n");
        repo.Write("g.txt", "G\n");
        repo.Run("add", "f.txt", "g.txt");
        repo.Run("stash", "push", "-m", StashMergeMessage);
        repo.Commit("f.txt", "B\n", "Move f on main");
        var stashed = repo.Run("rev-parse", "stash@{0}");

        var outcome = Pop(repo);

        Assert.Equal(PopKind.Popped, outcome.Kind);
        Assert.True(outcome.StagedCameBackUnstaged);
        Assert.Equal(new[] { " M g.txt" }, Lines(repo));
        Assert.Equal("G\n", File.ReadAllText(Path.Combine(repo.Dir, "g.txt")));
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.Contains(outcome.Commands, c => c.Args.SequenceEqual(["stash", "pop", "--index"]) && c.ExitCode != 0);

        // The undo step puts the entry back as it was, so the staged change is recoverable.
        var undo = new UndoOperation(repo.Git);
        var check = undo.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        var undone = undo.Run(new UndoRequest(check.StepRef), check);
        Assert.Equal(UndoOutcomeKind.Undone, undone.Kind);
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
    }

    [Fact]
    public void A_refused_pop_names_up_to_three_staged_files_that_would_be_lost_and_counts_the_rest()
    {
        // Four staged files differ from their working versions, and HEAD moves a.txt, so `pop --index` refuses.
        using var repo = TempRepo.Create();
        string[] files = ["a.txt", "b.txt", "c.txt", "d.txt"];
        foreach (var f in files)
            repo.Commit(f, "A\n", "Add " + f);
        foreach (var f in files)
        {
            repo.Write(f, "B\n");
            repo.Run("add", f);
            repo.Write(f, "A\n");
        }
        repo.Run("stash", "push", "-m", StashMergeMessage);
        repo.Commit("a.txt", "D\n", "Move a on main");

        var outcome = Pop(repo);

        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.StartsWith("your staged a.txt, b.txt, c.txt +1 more can't be put back", outcome.Message);
        Assert.Contains(StashMergeMessage, repo.Run("stash", "list"));
    }

    [Fact]
    public void Any_other_stash_entry_pops_plainly_so_staged_changes_come_back_unstaged()
    {
        using var repo = StagedThenEdited("my work");

        var outcome = Pop(repo);

        Assert.Equal(PopKind.Popped, outcome.Kind);
        Assert.False(outcome.StagedCameBackUnstaged);
        Assert.Equal(new[] { " M f.txt" }, Lines(repo));
        Assert.Equal("", repo.Run("diff", "--cached", "--name-only"));
    }
}
