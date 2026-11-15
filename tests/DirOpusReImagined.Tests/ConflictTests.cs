using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class ConflictResolverTests
{
    [Theory]
    [InlineData("photo.jpg", false, "photo (2).jpg")]
    [InlineData("archive.tar.gz", false, "archive.tar (2).gz")]
    [InlineData("noext", false, "noext (2)")]
    [InlineData(".gitignore", false, ".gitignore (2)")]
    [InlineData("my.folder", true, "my.folder (2)")]
    public void SuggestKeepBothName_appends_a_counter(string name, bool isDir, string expected)
        => Assert.Equal(expected, ConflictResolver.SuggestKeepBothName(name, isDir, _ => false));

    [Fact]
    public void SuggestKeepBothName_skips_taken_candidates()
    {
        var taken = new HashSet<string> { "a (2).txt", "a (3).txt" };
        Assert.Equal("a (4).txt", ConflictResolver.SuggestKeepBothName("a.txt", false, taken.Contains));
    }

    [Theory]
    [InlineData("/home/u/file.txt", "file.txt")]
    [InlineData("/home/u/dir/", "dir")]
    [InlineData(@"C:\data\file.txt", "file.txt")]
    [InlineData(@"\\server\share\x", "x")]
    [InlineData("cloud://gdrive/a/b.txt", "b.txt")]
    [InlineData("plain", "plain")]
    public void LeafOf_returns_the_last_segment(string path, string expected)
        => Assert.Equal(expected, ConflictResolver.LeafOf(path));

    [Theory]
    [InlineData("/home/u/file.txt", "new.txt", "/home/u/new.txt")]
    [InlineData(@"C:\data\file.txt", "new.txt", @"C:\data\new.txt")]
    [InlineData("cloud://gdrive/a/b.txt", "c.txt", "cloud://gdrive/a/c.txt")]
    [InlineData("/home/u/dir/", "dir2", "/home/u/dir2")]
    [InlineData("plain", "other", "other")]
    public void ReplaceLeaf_keeps_the_parent_and_its_separator_style(string path, string leaf, string expected)
        => Assert.Equal(expected, ConflictResolver.ReplaceLeaf(path, leaf));

    [Fact]
    public void ApplyResolutions_skips_overwrites_renames_and_passes_through()
    {
        var keep = new TransferItem("/s/keep.txt", "/d", "/d/keep.txt", false);
        var skip = new TransferItem("/s/skip.txt", "/d", "/d/skip.txt", false);
        var over = new TransferItem("/s/over.txt", "/d", "/d/over.txt", false);
        var ren = new TransferItem("/s/ren.txt", "/d", "/d/ren.txt", false);
        var renBlank = new TransferItem("/s/blank.txt", "/d", "/d/blank.txt", false);

        var decisions = new Dictionary<string, ConflictDecision>
        {
            [skip.Source] = new(ConflictResolution.Skip, null),
            [over.Source] = new(ConflictResolution.Overwrite, null),
            [ren.Source] = new(ConflictResolution.Rename, "ren (2).txt"),
            [renBlank.Source] = new(ConflictResolution.Rename, "  "),
        };

        var result = ConflictResolver.ApplyResolutions(new[] { keep, skip, over, ren, renBlank }, decisions);

        Assert.Equal(new[]
        {
            keep,
            over,
            ren with { TargetPath = "/d/ren (2).txt" },
            renBlank, // a blank new name keeps the original leaf
        }, result);
    }
}

public class ConflictScannerTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private TransferItem Item(string name, bool isDir = false)
        => new(_t.Combine("src/" + name), _t.Combine("dst"), _t.Combine("dst/" + name), isDir);

    [Fact]
    public void Scan_reports_only_items_whose_target_exists_with_stats_and_suggestion()
    {
        _t.File("src/clash.txt", "source!", Times.Later);
        _t.File("dst/clash.txt", "dst", Times.Base);
        _t.File("dst/clash (2).txt", "already there");
        _t.File("src/fresh.txt", "new");
        _t.Dir("src/folder");
        _t.Dir("dst/folder");

        var conflicts = ConflictScanner.Scan(new[] { Item("clash.txt"), Item("fresh.txt"), Item("folder", true) });

        Assert.Equal(2, conflicts.Count);
        var c = conflicts.Single(x => x.Name == "clash.txt");
        Assert.False(c.IsDirectory);
        Assert.True(c.HasSourceStat);
        Assert.True(c.HasDestStat);
        Assert.Equal(7, c.SourceSize);
        Assert.Equal(3, c.DestSize);
        Assert.Equal(Times.Later, c.SourceModified.ToUniversalTime());
        Assert.Equal(Times.Base, c.DestModified.ToUniversalTime());
        Assert.Equal("clash (3).txt", c.SuggestedNewName); // (2) exists at the destination

        var f = conflicts.Single(x => x.Name == "folder");
        Assert.True(f.IsDirectory);
        Assert.Equal("folder (2)", f.SuggestedNewName);
    }

    [Fact]
    public void Scan_does_not_treat_a_folder_at_a_file_target_as_a_conflict()
    {
        _t.File("src/x", "file");
        _t.Dir("dst/x");
        Assert.Empty(ConflictScanner.Scan(new[] { Item("x") }));
    }

    [Fact]
    public void Scan_suggestions_avoid_other_batch_names_and_each_other()
    {
        // Two sources from different folders with the same leaf would both suggest "a (2).txt";
        // and "a (2).txt" is itself part of the batch.
        _t.File("src/a.txt", "1");
        _t.File("src2/a.txt", "2");
        _t.File("src/a (2).txt", "3");
        _t.File("dst/a.txt", "x");
        var items = new[]
        {
            Item("a.txt"),
            new TransferItem(_t.Combine("src2/a.txt"), _t.Combine("dst"), _t.Combine("dst/a.txt"), false),
            Item("a (2).txt"),
        };

        var suggestions = ConflictScanner.Scan(items).Select(c => c.SuggestedNewName).ToList();

        Assert.Equal(new[] { "a (3).txt", "a (4).txt" }, suggestions);
    }
}
