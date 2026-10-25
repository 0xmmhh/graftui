namespace graftui.Ui.Text;

/// <summary>Semantic colour roles. The actual colours live in the Views layer (Palette), so this stays testable.</summary>
public enum Ink : byte { Fg, Dim, Cur, Inc, Mrg, Ok, Bad, Warn, Hash, Frame }

public readonly record struct Style(Ink Ink, bool Bold = false);

/// <summary>A run of text in one style, drawn left to right.</summary>
public sealed record Seg(string Text, Style Style);

/// <summary>The one-line description under the art.</summary>
public sealed record Caption(string Text, Style Style);
