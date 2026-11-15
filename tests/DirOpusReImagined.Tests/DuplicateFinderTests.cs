using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class DuplicateFinderTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private List<DuplicateGroup> Find(params string[] roots)
        => DuplicateFinder.Find(DuplicateFinder.CollectFiles(roots.Select(_t.Combine), includeHidden: true, recursive: true), out _);

    [Fact]
    public void Identical_files_are_grouped_wherever_they_are()
    {
        _t.File("a/one.txt", "same content");
        _t.File("a/sub/two.txt", "same content");
        _t.File("a/other.txt", "different!!!");   // same size, different contents
        _t.File("a/unique.txt", "x");

        var g = Assert.Single(Find("a"));
        Assert.Equal(new[] { "one.txt", "two.txt" }, g.Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(12, g.Size);
        Assert.Equal(12, g.Wasted);
    }

    [Fact]
    public void Large_files_that_differ_only_after_the_first_64KB_are_not_duplicates()
    {
        var bytes = new byte[DuplicateFinder.PartialBytes + 1000];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(_t.File("big/a.bin", ""), bytes);
        File.WriteAllBytes(_t.File("big/b.bin", ""), bytes);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(_t.File("big/c.bin", ""), bytes);

        var g = Assert.Single(Find("big"));
        Assert.Equal(new[] { "a.bin", "b.bin" }, g.Files.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void Empty_files_are_left_out()
    {
        _t.File("e/a", "");
        _t.File("e/b", "");
        Assert.Empty(Find("e"));
    }

    [Fact]
    public void Overlapping_roots_list_a_file_once()
    {
        _t.File("r/x.txt", "dup");
        _t.File("r/inner/y.txt", "dup");
        var g = Assert.Single(Find("r", "r/inner"));
        Assert.Equal(2, g.Files.Count);
    }

    [Fact]
    public void Across_two_folders()
    {
        _t.File("left/photo.jpg", "jpeg bytes");
        _t.File("right/copy of photo.jpg", "jpeg bytes");
        Assert.Single(Find("left", "right"));
    }

    [Fact]
    public void Not_recursive_looks_only_at_the_folder_itself()
    {
        _t.File("n/a.txt", "dup");
        _t.File("n/sub/b.txt", "dup");
        var files = DuplicateFinder.CollectFiles(new[] { _t.Combine("n") }, true, recursive: false);
        Assert.Empty(DuplicateFinder.Find(files, out _));
    }

    [Fact]
    public void The_most_wasteful_groups_come_first()
    {
        _t.File("w/s1", "ab");
        _t.File("w/s2", "ab");
        _t.File("w/l1", "abcdefgh");
        _t.File("w/l2", "abcdefgh");
        Assert.Equal(new long[] { 8, 2 }, Find("w").Select(g => g.Size));
    }

    [Fact]
    public void Cancelling_stops_the_search()
    {
        _t.File("c/a", "dup");
        _t.File("c/b", "dup");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var files = DuplicateFinder.CollectFiles(new[] { _t.Combine("c") }, true, true);
        Assert.ThrowsAny<OperationCanceledException>(() => DuplicateFinder.Find(files, out _, ct: cts.Token));
    }

    private static FileEntry F(string path, int ageDays)
        => new(path, Path.GetFileName(path), false, 1, new DateTime(2026, 1, 1).AddDays(-ageDays), FileEntryFlags.None);

    [Theory]
    [InlineData(KeepRule.Oldest, "/b/old")]
    [InlineData(KeepRule.Newest, "/a/new")]
    [InlineData(KeepRule.ShortestPath, "/a/new")]
    public void Marking_keeps_one_copy_by_the_chosen_rule(KeepRule rule, string kept)
    {
        var group = new DuplicateGroup(1, new[] { F("/a/new", 1), F("/b/old", 30), F("/c/longer/mid", 10) });
        var marked = DuplicateFinder.AllButKept(group, rule).Select(f => f.Path).ToList();
        Assert.Equal(2, marked.Count);
        Assert.DoesNotContain(kept, marked);
    }
}
