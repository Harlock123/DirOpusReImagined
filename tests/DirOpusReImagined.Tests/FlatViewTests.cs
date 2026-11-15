using System.IO.Compression;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

public class FlatViewTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static string S(string p) => p.Replace('/', Path.DirectorySeparatorChar);

    [Fact]
    public void Every_file_below_is_listed_by_its_relative_path()
    {
        _t.File("root/top.txt", "t");
        _t.File("root/src/a.cs", "a");
        _t.File("root/src/deep/b.cs", "b");
        _t.Dir("root/empty");
        var (files, truncated) = FileUtility.CollectFlat(new LocalFileProvider(), _t.Combine("root"), showHidden: true, 1000);

        Assert.False(truncated);
        Assert.Equal(new[] { S("src/a.cs"), S("src/deep/b.cs"), "top.txt" }, files.Select(f => f.Name).OrderBy(n => n));
        Assert.All(files, f => Assert.True(File.Exists(f.Path)));   // the real path is kept for opening
    }

    [Fact]
    public void Linked_folders_are_not_walked_into()
    {
        _t.File("root/real.txt", "r");
        try { Directory.CreateSymbolicLink(_t.Combine("root/loop"), _t.Combine("root")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        var (files, _) = FileUtility.CollectFlat(new LocalFileProvider(), _t.Combine("root"), true, 1000);
        Assert.Equal(new[] { "real.txt" }, files.Select(f => f.Name));
    }

    [Fact]
    public void Hidden_folders_are_skipped_unless_hidden_files_are_shown()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;   // dot-names are hidden there
        _t.File("root/.git/config", "c");
        _t.File("root/a.txt", "a");
        var p = new LocalFileProvider();
        Assert.Equal(new[] { "a.txt" }, FileUtility.CollectFlat(p, _t.Combine("root"), false, 1000).Files.Select(f => f.Name));
        Assert.Equal(2, FileUtility.CollectFlat(p, _t.Combine("root"), true, 1000).Files.Count);
    }

    [Fact]
    public void A_huge_tree_stops_at_the_limit_and_says_so()
    {
        for (int i = 0; i < 7; i++) _t.File($"root/d{i % 3}/f{i}.txt", "x");
        var (files, truncated) = FileUtility.CollectFlat(new LocalFileProvider(), _t.Combine("root"), true, 5);
        Assert.True(truncated);
        Assert.Equal(5, files.Count);
    }

    [Fact]
    public void Archives_list_flat_with_forward_slashes()
    {
        var zip = _t.Combine("a.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (var n in new[] { "x.txt", "docs/y.md", "docs/sub/z.md" })
                using (var w = new StreamWriter(z.CreateEntry(n).Open())) w.Write(n);

        var root = ArchivePath.RootUriFor(zip);
        var (files, _) = FileUtility.CollectFlat(ProviderRegistry.For(root), root, true, 1000);
        Assert.Equal(new[] { "docs/sub/z.md", "docs/y.md", "x.txt" }, files.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Copying_a_file_from_a_flat_view_keeps_its_sub_folder()
    {
        var root = _t.Dir("root");
        _t.File("root/src/deep/b.cs", "b");
        var dest = _t.Dir("dest");
        string name = S("src/deep/b.cs");   // what a flat panel names it
        var item = new TransferItem(FileUtility.JoinPanelPath(root, name), dest, FileUtility.JoinPanelPath(dest, name), false);

        await TransferCoordinator.RunAsync(new List<TransferItem> { item }, move: false, progress: null, CancellationToken.None);
        Assert.Equal("b", File.ReadAllText(Path.Combine(dest, "src", "deep", "b.cs")));
    }
}
