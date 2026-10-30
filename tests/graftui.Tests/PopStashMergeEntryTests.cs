using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Z over graftui's own stash-merge entry pops with --index, so staged changes stay staged; when git refuses --index up front the plain
/// pop is used and the outcome says the staged changes came back unstaged. Any other entry is popped plainly, as before.
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
    public void A_stash_merge_entry_whose_index_cannot_apply_pops_plainly_and_says_the_staged_change_came_back_unstaged()
    {
        // The stash staged B over A and then went back to A in the tree. HEAD then moves on f, so `pop --index` cannot apply the
        // staged B ("Try without --index", tree untouched) and the plain pop applies the tree, which is clean.
        using var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Write("f.txt", "B\n");
        repo.Run("add", "f.txt");
        repo.Write("f.txt", "A\n");
        repo.Run("stash", "push", "-m", StashMergeMessage);
        repo.Commit("f.txt", "D\n", "Move f on main");
        var stashed = repo.Run("rev-parse", "stash@{0}");

        var outcome = Pop(repo);

        Assert.Equal(PopKind.Popped, outcome.Kind);
        Assert.True(outcome.StagedCameBackUnstaged);
        Assert.Equal("", repo.Status());
        Assert.Equal("D\n", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.Equal("", repo.Run("stash", "list"));

        // The undo step puts the entry back as it was, so the staged change is recoverable.
        var undo = new UndoOperation(repo.Git);
        var check = undo.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        var undone = undo.Run(new UndoRequest(check.StepRef), check);
        Assert.Equal(UndoOutcomeKind.Undone, undone.Kind);
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
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
