namespace graftui.Ui.Text;

public enum LogKind { Command, Output, Message }

/// <summary>
/// One line of the Commands pane. ExitCode is only meaningful for commands. Simple marks a command line of the simplified list
/// (a successful one is drawn with a tick). Failed marks a command that failed: it wraps and is drawn in the failure colour.
/// </summary>
public sealed record LogLine(LogKind Kind, string Text, int ExitCode = 0, Style Style = default, bool Simple = false, bool Failed = false);

/// <summary>
/// What one git invocation (or one message) adds to the Commands pane: its lines in the exact list and in the simplified list.
/// An empty simplified list means the simplified pane leaves the entry out.
/// </summary>
public sealed record LogEntry(IReadOnlyList<LogLine> Exact, IReadOnlyList<LogLine> Simple);
