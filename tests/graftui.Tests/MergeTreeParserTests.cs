using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>
/// merge-tree's `-z` output, copied from what git 2.56 printed. Each token ends with a NUL, as git writes it.
/// </summary>
public class MergeTreeParserTests
{
    private const string Tree = "1111111111111111111111111111111111111111";
    private const string Ours = "2222222222222222222222222222222222222222";
    private const string Theirs = "3333333333333333333333333333333333333333";
    private const string Base = "4444444444444444444444444444444444444444";

    [Fact]
    public void A_clean_merge_has_the_tree_and_no_files()
    {
        var files = MergeTreeParser.ParseConflicts(Z(Tree));

        Assert.NotNull(files);
        Assert.Empty(files);
        Assert.True(MergeTreeParser.HasTree(Z(Tree)));
    }

    [Fact]
    public void A_content_conflict_lists_the_file_as_content()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tapp.txt", $"100644 {Ours} 2\tapp.txt", $"100644 {Theirs} 3\tapp.txt",
            "", "1", "app.txt", "Auto-merging", "Auto-merging app.txt\n",
            "1", "app.txt", "CONFLICT (contents)", "CONFLICT (content): Merge conflict in app.txt\n");

        var files = MergeTreeParser.ParseConflicts(stdout);

        AssertFiles(files, ("app.txt", ForecastConflict.Content));
    }

    [Fact]
    public void An_add_add_conflict_has_stages_2_and_3_only_and_is_both_added()
    {
        var stdout = Z(Tree,
            $"100644 {Ours} 2\tboth.txt", $"100644 {Theirs} 3\tboth.txt",
            "", "1", "both.txt", "CONFLICT (contents)", "CONFLICT (add/add): Merge conflict in both.txt\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout), ("both.txt", ForecastConflict.BothAdded));
    }

    [Fact]
    public void A_modify_delete_conflict_deleted_on_the_current_side_has_stages_1_and_3()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tdel.txt", $"100644 {Theirs} 3\tdel.txt",
            "", "1", "del.txt", "CONFLICT (modify/delete)", "CONFLICT (modify/delete): del.txt deleted in HEAD\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout), ("del.txt", ForecastConflict.DeletedCurrent));
    }

    [Fact]
    public void A_modify_delete_conflict_deleted_on_the_selected_side_has_stages_1_and_2()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tdel.txt", $"100644 {Ours} 2\tdel.txt",
            "", "1", "del.txt", "CONFLICT (modify/delete)", "CONFLICT (modify/delete): del.txt deleted in feature\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout), ("del.txt", ForecastConflict.DeletedSelected));
    }

    [Fact]
    public void A_binary_conflict_has_both_binary_and_contents_types_and_is_binary()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tbin.dat", $"100644 {Ours} 2\tbin.dat", $"100644 {Theirs} 3\tbin.dat",
            "", "1", "bin.dat", "CONFLICT (binary)", "CONFLICT (binary): Merge conflict in bin.dat\n",
            "1", "bin.dat", "CONFLICT (contents)", "CONFLICT (content): Merge conflict in bin.dat\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout), ("bin.dat", ForecastConflict.Binary));
    }

    [Fact]
    public void A_rename_rename_conflict_lists_all_three_paths_as_renamed_in_git_order()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tren.txt", $"100644 {Ours} 2\tmain-name.txt", $"100644 {Theirs} 3\tother-name.txt",
            "", "3", "ren.txt", "main-name.txt", "other-name.txt", "CONFLICT (rename/rename)",
            "CONFLICT (rename/rename): ren.txt renamed to main-name.txt in HEAD and to other-name.txt in feature\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout),
            ("ren.txt", ForecastConflict.Rename),
            ("main-name.txt", ForecastConflict.Rename),
            ("other-name.txt", ForecastConflict.Rename));
    }

    [Fact]
    public void A_directory_rename_conflict_lists_both_paths_as_other()
    {
        // Copied from git 2.56: main added dir/c.txt while feature renamed dir/ to newdir/.
        var stdout = Z("fa7e3ddda5416f6d99370cabebd4965ea37d0524",
            "100644 f2ad6c76f0115a6ba5b00456a849810e7ec0af20 2\tnewdir/c.txt",
            "",
            "2", "newdir/c.txt", "dir/c.txt", "CONFLICT (directory rename suggested)",
            "CONFLICT (file location): dir/c.txt added in main inside a directory that was renamed in feature, suggesting it should perhaps be moved to newdir/c.txt.\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout),
            ("newdir/c.txt", ForecastConflict.Other),
            ("dir/c.txt", ForecastConflict.Other));
    }

    [Fact]
    public void Conflict_types_are_read_with_or_without_the_space_after_conflict()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tbin.dat", $"100644 {Ours} 2\tbin.dat", $"100644 {Theirs} 3\tbin.dat",
            "", "1", "bin.dat", "CONFLICT(binary)", "CONFLICT(binary): Merge conflict in bin.dat\n",
            "3", "ren.txt", "main-name.txt", "other-name.txt", "CONFLICT(rename/rename)", "msg\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout),
            ("bin.dat", ForecastConflict.Binary),
            ("ren.txt", ForecastConflict.Rename),
            ("main-name.txt", ForecastConflict.Rename),
            ("other-name.txt", ForecastConflict.Rename));
    }

    [Theory]
    [InlineData("CONFLICT (contents)", "CONFLICT(contents)")]
    [InlineData("CONFLICT(contents)", "CONFLICT(contents)")]
    [InlineData("Auto-merging", "Auto-merging")]
    public void The_space_after_conflict_is_dropped(string type, string expected)
    {
        Assert.Equal(expected, MergeTreeParser.NormalizeType(type));
    }

    [Fact]
    public void A_conflict_with_no_file_records_is_an_empty_list_not_null()
    {
        var files = MergeTreeParser.ParseConflicts(Z(Tree, ""));

        Assert.NotNull(files);
        Assert.Empty(files);
    }

    [Fact]
    public void A_path_that_only_merged_automatically_is_not_listed()
    {
        var stdout = Z(Tree,
            $"100644 {Base} 1\tapp.txt", $"100644 {Ours} 2\tapp.txt", $"100644 {Theirs} 3\tapp.txt",
            "", "2", "app.txt", "other.txt", "Auto-merging", "Auto-merging app.txt\n",
            "1", "app.txt", "CONFLICT (contents)", "CONFLICT (content): Merge conflict in app.txt\n");

        AssertFiles(MergeTreeParser.ParseConflicts(stdout), ("app.txt", ForecastConflict.Content));
    }

    [Fact]
    public void Empty_output_is_not_merge_tree_output()
    {
        Assert.Null(MergeTreeParser.ParseConflicts(""));
        Assert.False(MergeTreeParser.HasTree(""));
    }

    [Fact]
    public void A_first_token_that_is_not_a_tree_oid_is_not_merge_tree_output()
    {
        Assert.Null(MergeTreeParser.ParseConflicts(Z("nosuch")));
        Assert.Null(MergeTreeParser.ParseConflicts(Z("1111")));
        Assert.False(MergeTreeParser.HasTree(Z("merge-tree: nosuch")));
    }

    [Fact]
    public void A_file_record_that_does_not_parse_makes_the_whole_output_unknown()
    {
        var stdout = Z(Tree, "100644 1\tapp.txt");

        Assert.Null(MergeTreeParser.ParseConflicts(stdout));
    }

    [Fact]
    public void An_oid_needs_40_or_64_lowercase_hex_digits()
    {
        Assert.True(MergeTreeParser.IsOid(Tree));
        Assert.True(MergeTreeParser.IsOid(new string('a', 64)));
        Assert.False(MergeTreeParser.IsOid(new string('a', 39)));
        Assert.False(MergeTreeParser.IsOid(new string('A', 40)));
        Assert.False(MergeTreeParser.IsOid(new string('g', 40)));
    }

    // Each token followed by a NUL, the way git writes `-z` output.
    private static string Z(params string[] tokens) => string.Concat(tokens.Select(t => t + "\0"));

    private static void AssertFiles(IReadOnlyList<ForecastFile>? files, params (string Path, ForecastConflict Kind)[] expected)
    {
        Assert.NotNull(files);
        Assert.Equal(expected.ToList(), files.Select(file => (file.Path, file.Kind)).ToList());
    }
}
