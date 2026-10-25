using graftui.Model;

namespace graftui.Ui.Scenes;

/// <summary>
/// Drives one merge's animation. Before git answers, the scene is predicted from the layout and held at its
/// hold point. When the real outcome arrives, the animation continues from the real family's hold point, so
/// a prediction that was wrong (for example merge.ff=false) still shows the real result.
/// </summary>
public sealed class MergeAnimation(GraphLayout layout)
{
    public const long MergeGrow = 1000, MergeFlash = 650, MergeRetract = 600;
    public const long FfGrow = 1100, FfFlash = 500;
    public const long UtdPulse = 900;
    public const long WaitAfterMs = 5000;

    private enum Family { Merge, FastForward, UpToDate, Failed }

    private Family _fam = Predict(layout.Kind);
    private long _offset;

    public GraphLayout Layout { get; } = layout;

    public MergeOutcome? Outcome { get; private set; }

    /// <summary>Records git's answer. <paramref name="elapsedMs"/> is the time on the animation clock.</summary>
    public void SetOutcome(MergeOutcome o, long elapsedMs)
    {
        Outcome = o;
        var real = FamilyOf(o);
        if (real != _fam)
        {
            // Wrong prediction: jump to the real scene's hold point, then carry on from there.
            _fam = real;
            _offset = elapsedMs - Hold(real);
        }
        else
        {
            _offset = Math.Max(0, elapsedMs - Hold(_fam));
        }
    }

    /// <summary>True once the scene has finished playing after the outcome arrived.</summary>
    public bool IsDone(long elapsedMs) => Outcome is not null && TimeOf(elapsedMs) >= Total();

    public SceneFrame Render(ArtGeometry g, long elapsedMs)
    {
        var te = TimeOf(elapsedMs);
        var waiting = Outcome is null && elapsedMs >= WaitAfterMs;
        return _fam switch
        {
            Family.Merge => MergeScenes.Merge(g, Layout, te, elapsedMs, Outcome, waiting),
            Family.FastForward => MergeScenes.FastForward(g, Layout, te, elapsedMs, Outcome, waiting),
            Family.UpToDate => MergeScenes.UpToDate(g, Layout, te, elapsedMs, Outcome, waiting),
            _ => MergeScenes.Failed(g, Layout, Outcome!),
        };
    }

    /// <summary>Window title: "Merging" until the scene is done, then the result.</summary>
    public string Title(long elapsedMs)
    {
        if (!IsDone(elapsedMs))
            return "Merging";
        return Outcome!.Kind switch
        {
            MergeKind.MergeCommit => "Merged",
            MergeKind.Conflict => "Aborted",
            MergeKind.FastForward => "Fast-forwarded",
            MergeKind.UpToDate => "Up to date",
            _ => "Failed",
        };
    }

    // Time the scene has reached: clamped to the hold point until the outcome arrives, then offset from the clock.
    private long TimeOf(long elapsedMs) =>
        Outcome is null ? Math.Min(elapsedMs, Hold(_fam)) : elapsedMs - _offset;

    private long Total() => _fam switch
    {
        Family.Merge when Outcome?.Kind == MergeKind.Conflict => MergeGrow + MergeFlash + MergeRetract,
        Family.Merge => MergeGrow + MergeFlash,
        Family.FastForward => FfGrow + FfFlash,
        Family.UpToDate => UtdPulse,
        _ => 0,
    };

    private static long Hold(Family f) => f switch
    {
        Family.Merge => MergeGrow,
        Family.FastForward => FfGrow,
        Family.UpToDate => UtdPulse,
        _ => 0,
    };

    private static Family Predict(GraphKind kind) => kind switch
    {
        GraphKind.FastForward => Family.FastForward,
        GraphKind.UpToDate or GraphKind.Same => Family.UpToDate,
        _ => Family.Merge,
    };

    private static Family FamilyOf(MergeOutcome o) => o.Kind switch
    {
        MergeKind.FastForward => Family.FastForward,
        MergeKind.UpToDate => Family.UpToDate,
        MergeKind.MergeCommit or MergeKind.Conflict => Family.Merge,
        _ => Family.Failed,
    };
}
