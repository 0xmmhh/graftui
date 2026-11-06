using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The committer date on a rebase row. The engine fills it from each commit, the plan check compares only hashes, and the state file
/// neither writes it nor needs it: a state file from before the dates still loads, with rows that have no date.
/// </summary>
public class RebaseRowDateTests
{
    [Fact]
    public void The_engine_rows_carry_each_commits_committer_date()
    {
        using var repo = SequenceRepos.Feature4();

        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));

        Assert.True(check.CanRun, check.Refusal);
        var expected = repo.Run("log", "--reverse", "--format=%ct", "main..feature").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, check.Rows.Select(r => r.Date!.Value.ToUnixTimeSeconds().ToString()));
        Assert.Equal(new[] { "Add login form", "Fix typo in form", "Add password check", "WIP debug logging" }, check.Rows.Select(r => r.Subject));
    }

    [Fact]
    public void A_plan_without_dates_is_accepted_and_the_plan_keeps_the_dates_it_was_given()
    {
        using var repo = SequenceRepos.Feature4();
        var op = new RebaseOperation(repo.Git);
        var dated = op.Check(new RebaseRequest("main")).Rows;
        var undated = dated.Select(r => r with { Date = null }).ToList();

        var check = op.Check(new RebaseRequest("main", undated));

        Assert.True(check.CanRun, check.Refusal);
        Assert.All(check.Rows, r => Assert.Null(r.Date));
    }

    [Fact]
    public void The_state_file_does_not_hold_the_dates()
    {
        using var repo = TempRepo.Create();
        var gitDir = Path.Combine(repo.Dir, ".git");
        var row = new RebaseRow("a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0", "a1b2c3d", "Add login form", RebaseAction.Pick,
            DateTimeOffset.FromUnixTimeSeconds(1700000000));
        var state = new SequenceState(1, SequenceKind.Rebase, "feature", "main", "abc1234", [row], [], [], [], [], [], []);

        Assert.Null(SequenceStateFile.Save(gitDir, state));

        var text = File.ReadAllText(Path.Combine(gitDir, SequenceStateFile.FileName));
        Assert.DoesNotContain("\"date\"", text, StringComparison.Ordinal);
        var loaded = SequenceStateFile.Load(gitDir);
        Assert.NotNull(loaded);
        Assert.Null(loaded.Rows[0].Date);
        SequenceStateFile.Delete(gitDir);
    }

    [Fact]
    public void A_state_file_from_before_the_dates_loads_and_its_rows_have_no_date()
    {
        using var repo = TempRepo.Create();
        var gitDir = Path.Combine(repo.Dir, ".git");
        const string oldFile = """
{"v":1,"kind":"Rebase","branch":"feature","onto":"main","oldTip":"abc1234","rows":[{"hash":"a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0","shortHash":"a1b2c3d","subject":"Add login form","action":"Pick"},{"hash":"b1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0","shortHash":"b1b2c3d","subject":"Fix typo","action":"Fixup","date":1700000000}],"picks":[],"skipped":[],"resolved":[],"reworded":[],"amended":[],"alreadyApplied":[]}
""";
        File.WriteAllText(Path.Combine(gitDir, SequenceStateFile.FileName), oldFile);

        var loaded = SequenceStateFile.Load(gitDir);

        Assert.NotNull(loaded);
        Assert.Equal(new[] { RebaseAction.Pick, RebaseAction.Fixup }, loaded.Rows.Select(r => r.Action));
        Assert.All(loaded.Rows, r => Assert.Null(r.Date));
        SequenceStateFile.Delete(gitDir);
    }
}
