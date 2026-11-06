using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// The exit rule that decides whether a branch change's own exit code stands once the UI has closed. A pause that is still there, and a
/// sequence graftui started that cannot be read, both keep the code; only when neither is left does the change's code go.
/// </summary>
public class ExitRuleTests
{
    [Fact]
    public void Nothing_paused_and_nothing_unreadable_means_the_change_is_gone()
    {
        Assert.True(ExitRule.ChangeGone(null, null));
    }

    [Fact]
    public void A_pause_still_there_at_exit_keeps_the_change_code()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);

        var paused = new SequenceReader(repo.Git).ReadPaused();

        Assert.NotNull(paused);
        Assert.False(ExitRule.ChangeGone(paused, null));
    }

    [Fact]
    public void A_sequence_that_cannot_be_read_at_exit_keeps_the_change_code_even_with_no_pause()
    {
        Assert.False(ExitRule.ChangeGone(null, "cherry-pick"));
        Assert.False(ExitRule.ChangeGone(null, "rebase"));
    }
}
