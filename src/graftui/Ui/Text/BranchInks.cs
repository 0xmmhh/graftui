using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>The ink of a branch lane. A lane outside 0..BranchLanes.Count-1 (None, or a name not in the list) is the plain foreground.</summary>
public static class BranchInks
{
    /// <summary>The ink for a lane: Lane0..Lane5 for lanes 0..5, else Fg.</summary>
    public static Ink Of(int lane)
    {
        if (lane < 0 || lane >= BranchLanes.Count)
            return Ink.Fg;

        return (Ink)((int)Ink.Lane0 + lane);
    }

    /// <summary>The ink of the current branch's line and label in a layout.</summary>
    public static Ink Current(GraphLayout L) => Of(L.CurrentLane);

    /// <summary>The ink of the selected branch's line and label in a layout.</summary>
    public static Ink Selected(GraphLayout L) => Of(L.SelectedLane);
}
