using System.Text.Json;
using System.Text.Json.Serialization;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Stores an <see cref="UndoRecord"/> in a commit message and reads it back.
/// The message is a headline, a blank line, then the record as one line of JSON: {"v":1,"step":{...}}.
/// Parse never throws; it returns null for anything it cannot use.
/// </summary>
internal static class UndoJson
{
    private const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record UndoFile(int V, UndoRecord Step);

    public static string Message(UndoRecord r)
    {
        // A path can contain a newline, so the headline is kept to one line. Only the JSON line is read back.
        var headline = "graftui undo: " + r.What.ReplaceLineEndings(" ");
        var json = JsonSerializer.Serialize(new UndoFile(Version, r), Options);
        return headline + "\n\n" + json + "\n";
    }

    public static UndoRecord? Parse(string commitMessage)
    {
        // JSON writes newlines inside strings as \n, so the record is always on the last non-empty line.
        var json = commitMessage.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0);
        if (json is null)
            return null;

        UndoFile? file;
        try
        {
            file = JsonSerializer.Deserialize<UndoFile>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (file is null || file.V != Version || file.Step is null)
            return null;

        // System.Text.Json does not enforce nullable reference types, so a null where a string or list must be is checked here.
        var step = file.Step;
        if (step.What is null || step.Changes is null)
            return null;
        if (step.Changes.Any(c => c is null || c.Ref is null || c.Before is null || c.After is null))
            return null;
        if (!EntriesAreComplete(step.IndexBefore) || !EntriesAreComplete(step.IndexAfter))
            return null;
        return step;
    }

    private static bool EntriesAreComplete(IReadOnlyList<IndexEntry>? entries) =>
        entries is null || entries.All(e => e is not null && e.Path is not null && e.Mode is not null && e.Blob is not null);
}
