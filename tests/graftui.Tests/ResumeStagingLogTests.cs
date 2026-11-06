using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// Continuing a paused conflict stages the resolved file first. That staging is the first command of the step, so it is the line the
/// Commands pane jumps to, and its simplified line reads as the user would type it.
/// </summary>
public class ResumeStagingLogTests
{
    [Fact]
    public void Continuing_a_conflict_stages_the_file_first_and_the_log_starts_at_that_staging()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);
        repo.Write("app.txt", "line 1 (resolved)\n");

        var resume = new ResumeSequenceOperation(repo.Git);
        var resumeRequest = new ResumeRequest(ResumeAction.Continue);
        var outcome = resume.Run(resumeRequest, resume.Check(resumeRequest));

        var add = outcome.Commands.First(c => c.Args.Count > 0 && c.Args[0] == "add");
        Assert.Equal("git add app.txt", CommandDisplay.Simplify(add, CommandHints.None)!.Text);
        var anchorOn = BranchAnimator.AnchorFor(outcome.Commands);
        Assert.Same(add, outcome.Commands.First(anchorOn));
    }
}
