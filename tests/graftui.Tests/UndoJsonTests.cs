using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

public class UndoJsonTests
{
    private const string HashA = "1111111111111111111111111111111111111111";
    private const string HashB = "2222222222222222222222222222222222222222";

    /// <summary>One record per kind, each with the fields that kind uses.</summary>
    private static UndoRecord Sample(UndoKind kind) => kind switch
    {
        UndoKind.Commit => new(kind, "commit", [new RefChange("refs/heads/main", "", HashA)], Branch: "main", Head: HashA),
        UndoKind.Amend => new(kind, "amend", [new RefChange("refs/heads/main", HashA, HashB)], Branch: "main", Head: HashB),
        UndoKind.Merge => new(kind, "merge of feature/x", [new RefChange("refs/heads/main", HashA, HashB)], Branch: "main", Head: HashB),
        UndoKind.Discard => new(kind, "discard of app.txt", [], Path: "app.txt", Backup: HashA, StashCommit: HashA,
            StashBelow: "", IndexBefore: [new IndexEntry("app.txt", "100644", HashB)], Head: HashB),
        UndoKind.Stash => new(kind, "stash", [], StashCommit: HashA, StashBelow: "", Head: HashB),
        UndoKind.Pop => new(kind, "stash pop", [], StashCommit: HashA, StashMessage: "On main: wip", StashBelow: "",
            StateBefore: HashA, StateAfter: HashB, Head: HashB),
        _ => new(kind, "unstage of app.txt", [], IndexBefore: [new IndexEntry("app.txt", "100644", HashA)],
            IndexAfter: [new IndexEntry("app.txt", "", "")]),
    };

    /// <summary>Two records are the same when their stored messages are the same, which covers every field.</summary>
    private static void AssertSameRecord(UndoRecord expected, UndoRecord actual) =>
        Assert.Equal(UndoJson.Message(expected), UndoJson.Message(actual));

    [Theory]
    [InlineData(UndoKind.Commit)]
    [InlineData(UndoKind.Amend)]
    [InlineData(UndoKind.Merge)]
    [InlineData(UndoKind.Discard)]
    [InlineData(UndoKind.Stash)]
    [InlineData(UndoKind.Pop)]
    [InlineData(UndoKind.Unstage)]
    public void Every_kind_round_trips(UndoKind kind)
    {
        var record = Sample(kind);

        var parsed = UndoJson.Parse(UndoJson.Message(record));

        Assert.NotNull(parsed);
        Assert.Equal(kind, parsed.Kind);
        AssertSameRecord(record, parsed);
    }

    [Fact]
    public void Paths_with_a_space_a_comma_a_newline_and_non_ascii_round_trip()
    {
        const string path = "naïve, dir\nfile é.txt";
        var record = new UndoRecord(UndoKind.Unstage, "unstage of 1 file", [],
            IndexBefore: [new IndexEntry(path, "100644", HashA)],
            IndexAfter: [new IndexEntry(path, "", "")]);

        var parsed = UndoJson.Parse(UndoJson.Message(record));

        Assert.NotNull(parsed);
        var entry = Assert.Single(parsed.IndexBefore!);
        Assert.Equal(path, entry.Path);
        Assert.Equal(HashA, entry.Blob);
        Assert.Equal("", parsed.IndexAfter![0].Mode);
    }

    [Fact]
    public void Message_is_a_headline_then_one_camelCase_json_line_with_no_operation_member()
    {
        var message = UndoJson.Message(Sample(UndoKind.Merge));

        var lines = message.Split('\n');
        Assert.Equal("graftui undo: merge of feature/x", lines[0]);
        Assert.Equal("", lines[1]);
        var json = lines[2];
        Assert.StartsWith("{\"v\":1,\"step\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":", json, StringComparison.Ordinal);
        Assert.Contains("\"changes\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"what\":\"merge of feature/x\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"operation\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Kind\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData("garbage")]
    [InlineData("graftui undo: commit\n\nnot json at all")]
    [InlineData("graftui undo: commit\n\n{\"v\":1,\"step\":")]
    public void Garbage_gives_null(string message) =>
        Assert.Null(UndoJson.Parse(message));

    [Fact]
    public void An_unknown_version_gives_null()
    {
        var message = UndoJson.Message(Sample(UndoKind.Commit)).Replace("{\"v\":1,", "{\"v\":2,", StringComparison.Ordinal);

        Assert.Null(UndoJson.Parse(message));
    }

    [Fact]
    public void A_missing_step_gives_null() =>
        Assert.Null(UndoJson.Parse("graftui undo: x\n\n{\"v\":1}"));

    [Fact]
    public void Null_changes_gives_null() =>
        Assert.Null(UndoJson.Parse("graftui undo: commit\n\n{\"v\":1,\"step\":{\"kind\":\"Commit\",\"what\":\"commit\",\"changes\":null}}"));

    [Fact]
    public void Null_what_gives_null() =>
        Assert.Null(UndoJson.Parse("graftui undo: commit\n\n{\"v\":1,\"step\":{\"kind\":\"Commit\",\"what\":null,\"changes\":[]}}"));

    [Fact]
    public void A_null_ref_change_or_a_null_string_in_one_gives_null()
    {
        const string nullElement = "{\"v\":1,\"step\":{\"kind\":\"Commit\",\"what\":\"commit\",\"changes\":[null]}}";
        const string nullField = "{\"v\":1,\"step\":{\"kind\":\"Commit\",\"what\":\"commit\",\"changes\":[{\"ref\":\"refs/heads/main\",\"before\":null,\"after\":\"abc\"}]}}";

        Assert.Null(UndoJson.Parse("graftui undo: commit\n\n" + nullElement));
        Assert.Null(UndoJson.Parse("graftui undo: commit\n\n" + nullField));
    }

    [Fact]
    public void A_null_element_inside_index_before_gives_null()
    {
        const string json = "{\"v\":1,\"step\":{\"kind\":\"Unstage\",\"what\":\"unstage\",\"changes\":[],\"indexBefore\":[null]}}";

        Assert.Null(UndoJson.Parse("graftui undo: unstage\n\n" + json));
    }

    [Fact]
    public void An_unknown_kind_gives_null() =>
        Assert.Null(UndoJson.Parse("graftui undo: x\n\n{\"v\":1,\"step\":{\"kind\":\"Explode\",\"what\":\"x\",\"changes\":[]}}"));
}
