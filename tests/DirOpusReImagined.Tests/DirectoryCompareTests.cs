using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class DirectoryCompareTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly string _l, _r;

    public DirectoryCompareTests()
    {
        _l = _t.Dir("left");
        _r = _t.Dir("right");
    }

    public void Dispose() => _t.Dispose();

    private void L(string rel, string content, DateTime t) => _t.File("left/" + rel, content, t);
    private void R(string rel, string content, DateTime t) => _t.File("right/" + rel, content, t);

    [Fact]
    public void Classifies_unique_newer_older_same_and_different()
    {
        L("onlyLeft.txt", "x", Times.Base);
        R("onlyRight.txt", "x", Times.Base);
        L("newer.txt", "abc", Times.Later); R("newer.txt", "abc", Times.Base);
        L("older.txt", "abc", Times.Earlier); R("older.txt", "abc", Times.Base);
        L("same.txt", "abc", Times.Base); R("same.txt", "abc", Times.Base);
        L("diffsize.txt", "abc", Times.Base); R("diffsize.txt", "abcdef", Times.Base);

        var res = DirectoryComparer.Compare(_l, _r);

        Assert.Equal(RowCompareState.Unique, res.Left["onlyLeft.txt"]);
        Assert.False(res.Right.ContainsKey("onlyLeft.txt"));
        Assert.Equal(RowCompareState.Unique, res.Right["onlyRight.txt"]);
        Assert.False(res.Left.ContainsKey("onlyRight.txt"));

        Assert.Equal(RowCompareState.Newer, res.Left["newer.txt"]);
        Assert.Equal(RowCompareState.Older, res.Right["newer.txt"]);
        Assert.Equal(RowCompareState.Older, res.Left["older.txt"]);
        Assert.Equal(RowCompareState.Newer, res.Right["older.txt"]);
        Assert.Equal(RowCompareState.Same, res.Left["same.txt"]);
        Assert.Equal(RowCompareState.Same, res.Right["same.txt"]);
        Assert.Equal(RowCompareState.Different, res.Left["diffsize.txt"]);
        Assert.Equal(RowCompareState.Different, res.Right["diffsize.txt"]);
    }

    [Fact]
    public void Timestamps_within_the_two_second_tolerance_count_as_equal()
    {
        L("a.txt", "abc", Times.Base.AddSeconds(1)); R("a.txt", "abc", Times.Base);
        var res = DirectoryComparer.Compare(_l, _r);
        Assert.Equal(RowCompareState.Same, res.Left["a.txt"]);
    }

    [Fact]
    public void File_on_one_side_and_folder_on_the_other_is_different()
    {
        L("thing", "abc", Times.Base);
        _t.Dir("right/thing");
        var res = DirectoryComparer.Compare(_l, _r);
        Assert.Equal(RowCompareState.Different, res.Left["thing"]);
        Assert.Equal(RowCompareState.Different, res.Right["thing"]);
    }

    [Fact]
    public void Shallow_compare_marks_shared_folders_same_even_when_contents_differ()
    {
        L("sub/a.txt", "abc", Times.Base);
        R("sub/a.txt", "abcdef", Times.Base);
        var res = DirectoryComparer.Compare(_l, _r, recursive: false);
        Assert.Equal(RowCompareState.Same, res.Left["sub"]);
        Assert.Equal(RowCompareState.Same, res.Right["sub"]);
    }

    [Fact]
    public void Recursive_compare_marks_folder_different_when_a_nested_file_differs()
    {
        L("sub/deep/a.txt", "abc", Times.Base);
        R("sub/deep/a.txt", "abc", Times.Later);
        L("same/b.txt", "b", Times.Base);
        R("same/b.txt", "b", Times.Base);
        var res = DirectoryComparer.Compare(_l, _r, recursive: true);
        Assert.Equal(RowCompareState.Different, res.Left["sub"]);
        Assert.Equal(RowCompareState.Different, res.Right["sub"]);
        Assert.Equal(RowCompareState.Same, res.Left["same"]);
    }

    [Fact]
    public void Recursive_compare_marks_folder_different_when_one_side_has_an_extra_file()
    {
        L("sub/a.txt", "abc", Times.Base);
        R("sub/a.txt", "abc", Times.Base);
        R("sub/extra.txt", "x", Times.Base);
        var res = DirectoryComparer.Compare(_l, _r, recursive: true);
        Assert.Equal(RowCompareState.Different, res.Left["sub"]);
    }

    [Fact]
    public void Content_mode_detects_same_size_same_time_edits()
    {
        L("a.txt", "abc", Times.Base); R("a.txt", "xyz", Times.Base);
        Assert.Equal(RowCompareState.Same, DirectoryComparer.Compare(_l, _r, content: false).Left["a.txt"]);
        Assert.Equal(RowCompareState.Different, DirectoryComparer.Compare(_l, _r, content: true).Left["a.txt"]);
    }

    [Fact]
    public void Content_mode_treats_identical_content_as_same_regardless_of_time()
    {
        L("a.txt", "abc", Times.Later); R("a.txt", "abc", Times.Base);
        Assert.Equal(RowCompareState.Newer, DirectoryComparer.Compare(_l, _r, content: false).Left["a.txt"]);
        var res = DirectoryComparer.Compare(_l, _r, content: true);
        Assert.Equal(RowCompareState.Same, res.Left["a.txt"]);
        Assert.Equal(RowCompareState.Same, res.Right["a.txt"]);
    }

    [Fact]
    public void Recursive_content_mode_catches_nested_same_size_edits()
    {
        L("sub/a.txt", "abc", Times.Base); R("sub/a.txt", "xyz", Times.Base);
        Assert.Equal(RowCompareState.Same, DirectoryComparer.Compare(_l, _r, recursive: true).Left["sub"]);
        Assert.Equal(RowCompareState.Different,
            DirectoryComparer.Compare(_l, _r, recursive: true, content: true).Left["sub"]);
    }

    [Fact]
    public void Compare_honours_cancellation()
    {
        L("a.txt", "abc", Times.Base);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DirectoryComparer.Compare(_l, _r, ct: cts.Token));
    }

    // ---------------- One-way sync plan ----------------

    private static string P(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void PlanSync_copies_new_newer_and_changed_files_and_lists_dest_only_items_for_deletion()
    {
        L("new.txt", "n", Times.Base);
        L("newer.txt", "abc", Times.Later); R("newer.txt", "abc", Times.Base);
        L("older.txt", "abc", Times.Earlier); R("older.txt", "abc", Times.Base);
        L("same.txt", "abc", Times.Base); R("same.txt", "abc", Times.Base);
        L("resized.txt", "abc", Times.Base); R("resized.txt", "abcdef", Times.Base);
        R("extra.txt", "x", Times.Base);
        _t.Dir("right/extraDir");

        var plan = DirectoryComparer.PlanSync(_l, _r);

        var copied = plan.Copies.Select(c => Path.GetFileName(c.Source)).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "new.txt", "newer.txt", "resized.txt" }, copied);
        Assert.All(plan.Copies, c =>
        {
            Assert.Equal(_r, c.TargetFolder);
            Assert.Equal(P(_r, Path.GetFileName(c.Source)), c.TargetPath);
            Assert.False(c.IsDirectory);
        });

        Assert.Equal(2, plan.Deletes.Count);
        Assert.Contains((P(_r, "extra.txt"), false), plan.Deletes);
        Assert.Contains((P(_r, "extraDir"), true), plan.Deletes);
    }

    [Fact]
    public void PlanSync_copies_a_source_only_folder_whole_and_recurses_into_shared_folders()
    {
        L("newDir/inner.txt", "x", Times.Base);
        L("shared/deep/changed.txt", "abc", Times.Later);
        R("shared/deep/changed.txt", "abc", Times.Base);
        R("shared/destOnly.txt", "x", Times.Base);

        var plan = DirectoryComparer.PlanSync(_l, _r);

        Assert.Contains(new TransferItem(P(_l, "newDir"), _r, P(_r, "newDir"), true), plan.Copies);
        Assert.Contains(new TransferItem(P(_l, "shared", "deep", "changed.txt"), P(_r, "shared", "deep"),
            P(_r, "shared", "deep", "changed.txt"), false), plan.Copies);
        Assert.Equal(2, plan.Copies.Count);
        Assert.Equal(new[] { (P(_r, "shared", "destOnly.txt"), false) }, plan.Deletes);
    }

    [Fact]
    public void PlanSync_skips_type_mismatches()
    {
        L("thing", "abc", Times.Base);
        _t.Dir("right/thing");
        var plan = DirectoryComparer.PlanSync(_l, _r);
        Assert.Empty(plan.Copies);
        Assert.Empty(plan.Deletes);
    }

    [Fact]
    public void PlanSync_content_mode_skips_identical_files_with_newer_timestamps()
    {
        L("same.txt", "abc", Times.Later); R("same.txt", "abc", Times.Base);
        L("edited.txt", "abc", Times.Base); R("edited.txt", "xyz", Times.Base);
        L("olderEdited.txt", "abc", Times.Earlier); R("olderEdited.txt", "xyz", Times.Base);

        Assert.Contains(DirectoryComparer.PlanSync(_l, _r).Copies, c => c.Source.EndsWith("same.txt"));

        var plan = DirectoryComparer.PlanSync(_l, _r, content: true);
        var copied = plan.Copies.Select(c => Path.GetFileName(c.Source)).ToArray();
        Assert.Equal(new[] { "edited.txt" }, copied); // never overwrites a newer destination
    }

    // ---------------- Two-way plan ----------------

    [Fact]
    public void PlanTwoWay_newer_wins_in_both_directions_and_leaves_same_time_conflicts_alone()
    {
        L("leftNewer.txt", "abc", Times.Later); R("leftNewer.txt", "abc", Times.Base);
        L("rightNewer.txt", "abc", Times.Base); R("rightNewer.txt", "abc", Times.Later);
        L("conflict.txt", "abc", Times.Base); R("conflict.txt", "abcdef", Times.Base);
        L("onlyLeft.txt", "x", Times.Base);
        R("onlyRightDir/f.txt", "x", Times.Base);
        L("shared/inner.txt", "abc", Times.Base); R("shared/inner.txt", "abc", Times.Later);

        var plan = DirectoryComparer.PlanTwoWay(_l, _r, content: false);

        Assert.Equal(2, plan.ToRight);
        Assert.Equal(3, plan.ToLeft);
        Assert.Equal(5, plan.Copies.Count);
        Assert.Contains(new TransferItem(P(_l, "leftNewer.txt"), _r, P(_r, "leftNewer.txt"), false), plan.Copies);
        Assert.Contains(new TransferItem(P(_l, "onlyLeft.txt"), _r, P(_r, "onlyLeft.txt"), false), plan.Copies);
        Assert.Contains(new TransferItem(P(_r, "rightNewer.txt"), _l, P(_l, "rightNewer.txt"), false), plan.Copies);
        Assert.Contains(new TransferItem(P(_r, "onlyRightDir"), _l, P(_l, "onlyRightDir"), true), plan.Copies);
        Assert.Contains(new TransferItem(P(_r, "shared", "inner.txt"), P(_l, "shared"), P(_l, "shared", "inner.txt"), false),
            plan.Copies);
        Assert.DoesNotContain(plan.Copies, c => c.Source.EndsWith("conflict.txt"));
    }

    [Fact]
    public void PlanTwoWay_content_mode_skips_identical_files()
    {
        L("a.txt", "abc", Times.Later); R("a.txt", "abc", Times.Base);
        Assert.Single(DirectoryComparer.PlanTwoWay(_l, _r, content: false).Copies);
        Assert.Empty(DirectoryComparer.PlanTwoWay(_l, _r, content: true).Copies);
    }

    [Fact]
    public void PlanSync_keeps_names_that_differ_only_by_case_on_case_sensitive_filesystems()
    {
        L("a.txt", "lower", Times.Base);
        L("A.txt", "upper", Times.Base);
        if (Directory.GetFiles(_l).Length < 2) return; // case-insensitive fs (Windows/macOS): not applicable

        var plan = DirectoryComparer.PlanSync(_l, _r);
        Assert.Equal(new[] { "A.txt", "a.txt" },
            plan.Copies.Select(c => Path.GetFileName(c.Source)).OrderBy(n => n, StringComparer.Ordinal));
    }
}
