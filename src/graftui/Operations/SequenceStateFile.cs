using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// What graftui remembers about a rebase or cherry-pick it started, while git holds the sequence. It lives in the git dir as
/// GRAFTUI_SEQUENCE, so it survives a restart while the sequence is paused. Lists are mutable: the operations add to them as steps land.
/// </summary>
internal sealed record SequenceState(int V, SequenceKind Kind, string Branch, string Onto, string OldTip,
    IReadOnlyList<RebaseRow> Rows, IReadOnlyList<string> Picks,
    List<string> Skipped, List<string> Resolved, List<string> Reworded, List<string> Amended, List<string> AlreadyApplied);

/// <summary>
/// Reads and writes <see cref="SequenceState"/>. Load never throws: a missing, unreadable or foreign file is null, and a caller then
/// treats the sequence as one graftui did not start. Save writes to a temporary file and renames it, so a crash never leaves half a file.
/// </summary>
internal static class SequenceStateFile
{
    public const string FileName = "GRAFTUI_SEQUENCE", TodoFile = "GRAFTUI_REBASE_TODO", MessageFile = "GRAFTUI_SEQUENCE_MSG";

    private const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The state in <paramref name="gitDir"/>, or null when there is none or it cannot be used.</summary>
    public static SequenceState? Load(string gitDir)
    {
        try
        {
            var path = Path.Combine(gitDir, FileName);
            if (!File.Exists(path))
                return null;

            var state = JsonSerializer.Deserialize<SequenceState>(File.ReadAllText(path), Options);
            return IsUsable(state) ? state : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
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
    }

    /// <summary>Writes the state. Returns null on success, otherwise what went wrong.</summary>
    public static string? Save(string gitDir, SequenceState s)
    {
        var path = Path.Combine(gitDir, FileName);
        var temp = path + ".tmp";
        try
        {
            var record = s with { V = Version };
            File.WriteAllText(temp, JsonSerializer.Serialize(record, Options) + "\n", new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex.Message;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Removes the state file. Best effort: a leftover file is replaced by the next Save.</summary>
    public static void Delete(string gitDir) => TryDelete(Path.Combine(gitDir, FileName));

    private static bool IsUsable(SequenceState? s) =>
        s is not null
        && s.V == Version
        && s.Branch is not null
        && s.Onto is not null
        && s.OldTip is not null
        && s.Rows is not null
        && s.Rows.All(r => r is not null && r.Hash is not null && r.ShortHash is not null && r.Subject is not null)
        && s.Picks is not null
        && s.Skipped is not null
        && s.Resolved is not null
        && s.Reworded is not null
        && s.Amended is not null
        && s.AlreadyApplied is not null;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover file is overwritten or removed the next time.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
