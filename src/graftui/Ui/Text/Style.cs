namespace graftui.Ui.Text;

/// <summary>
/// Semantic colour roles. The actual colours live in the Views layer (Palette), so this stays testable. Lane0..Lane5 are the
/// branch colours (see <see cref="Model.BranchLanes"/>): a branch's ink is Lane0 + its lane.
/// </summary>
public enum Ink : byte { Fg, Dim, Cur, Inc, Mrg, Ok, Bad, Warn, Hash, Frame, Lane0, Lane1, Lane2, Lane3, Lane4, Lane5 }

public readonly record struct Style(Ink Ink, bool Bold = false);

/// <summary>A run of text in one style, drawn left to right.</summary>
public sealed record Seg(string Text, Style Style);

/// <summary>The one-line description under the art.</summary>
public sealed record Caption(string Text, Style Style);
