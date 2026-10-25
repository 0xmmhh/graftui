namespace graftui.Ui.Text;

public enum LogKind { Command, Output, Message }

/// <summary>One line of the Commands pane. ExitCode is only meaningful for commands.</summary>
public sealed record LogLine(LogKind Kind, string Text, int ExitCode = 0, Style Style = default);
