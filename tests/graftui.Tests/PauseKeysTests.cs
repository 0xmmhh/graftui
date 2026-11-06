using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The pause's key table: Enter continues each kind of pause, S skips, and X and every other key have no action here.</summary>
public class PauseKeysTests
{
    [Theory]
    [InlineData(PauseKind.Conflict, ResumeAction.Continue)]
    [InlineData(PauseKind.Edit, ResumeAction.AmendAndContinue)]
    [InlineData(PauseKind.Reword, ResumeAction.Message)]
    [InlineData(PauseKind.Squash, ResumeAction.Message)]
    public void Enter_continues_each_kind_of_pause(PauseKind kind, ResumeAction expected) =>
        Assert.Equal<ResumeAction?>(expected, PauseKeys.ActionFor(kind, '\0', enter: true));

    [Theory]
    [InlineData(PauseKind.Conflict)]
    [InlineData(PauseKind.Edit)]
    [InlineData(PauseKind.Reword)]
    [InlineData(PauseKind.Squash)]
    public void S_skips_and_X_and_the_other_keys_have_no_action(PauseKind kind)
    {
        Assert.Equal<ResumeAction?>(ResumeAction.Skip, PauseKeys.ActionFor(kind, 'S', enter: false));
        Assert.Null(PauseKeys.ActionFor(kind, 'X', enter: false));
        Assert.Null(PauseKeys.ActionFor(kind, 's', enter: false));
        Assert.Null(PauseKeys.ActionFor(kind, '\0', enter: false));
    }
}
