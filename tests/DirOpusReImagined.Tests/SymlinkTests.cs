using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

/// <summary>
/// Real symbolic links. The tree has a link back to its own root (the case that used to send
/// recursive walks round forever), a relative file link and a broken link. Where links can't be
/// made (Windows without Developer Mode), the tests return early.
/// </summary>
public class SymlinkTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly LocalFileProvider _p = new();
    private readonly bool _canLink;
    private readonly string _tree;

    public SymlinkTests()
    {
        _tree = _t.Dir("tree");
        _t.File("tree/file.txt", "hello");
        _t.File("tree/real/inner.txt", "inner");
        try
        {
            Directory.CreateSymbolicLink(_t.Combine("tree/real/loop"), _tree);           // back to the root
            File.CreateSymbolicLink(_t.Combine("tree/filelink"), "file.txt");             // relative
            File.CreateSymbolicLink(_t.Combine("tree/broken"), _t.Combine("tree/nowhere"));
            Directory.CreateSymbolicLink(_t.Combine("tree/dirlink"), _t.Combine("tree/real"));
            _canLink = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _canLink = false;
        }
    }

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Links_are_listed_with_their_targets()
    {
        if (!_canLink) return;
        var dirs = _p.EnumerateDirectories(_tree).ToDictionary(e => e.Name);
        var files = _p.EnumerateFiles(_tree).ToDictionary(e => e.Name);

        Assert.True(dirs["dirlink"].IsLink);
        Assert.Equal(_t.Combine("tree/real"), dirs["dirlink"].LinkTarget);
        Assert.False(dirs["real"].IsLink);
        Assert.Equal("file.txt", files["filelink"].LinkTarget);
        Assert.True(files["filelink"].Flags.HasFlag(FileEntryFlags.Symlink));
        Assert.Contains("LNK", files["filelink"].AttributeString);
        Assert.DoesNotContain("LNK", files["file.txt"].AttributeString);
    }

    [Fact]
    public void A_broken_link_is_still_listed_and_statted()
    {
        if (!_canLink) return;
        var broken = Assert.Single(_p.EnumerateFiles(_tree), e => e.Name == "broken");
        Assert.True(broken.IsLink);
        var stat = _p.Stat(_t.Combine("tree/broken"));
        Assert.NotNull(stat);
        Assert.True(stat!.IsLink);
    }

    [Fact]
    public void Relative_link_targets_resolve_against_the_links_folder()
    {
        if (!_canLink) return;
        Assert.Equal(_t.Combine("tree/file.txt"), LocalFileProvider.ResolveLink(_t.Combine("tree/filelink")));
        Assert.Null(LocalFileProvider.ResolveLink(_t.Combine("tree/file.txt")));
        Assert.True(LocalFileProvider.IsLink(_t.Combine("tree/broken")));
        Assert.False(LocalFileProvider.IsLink(_t.Combine("tree/real")));
    }

    [Fact]
    public void Folder_size_does_not_follow_folder_links()
    {
        if (!_canLink) return;
        // file.txt (5) + inner.txt (5) + the two file links' own sizes; nothing via dirlink or loop.
        long links = new FileInfo(_t.Combine("tree/filelink")).Length + new FileInfo(_t.Combine("tree/broken")).Length;
        Assert.Equal(10 + links, _p.GetDirectorySize(_tree, recursive: true));
    }

    [Fact]
    public void Search_lists_a_matching_link_but_does_not_loop_through_it()
    {
        if (!_canLink) return;
        var hits = new List<SearchHit>();
        FileSearch.Search(_tree, "*", new SearchOptions(false, IncludeFolders: true), hits.Add, CancellationToken.None);
        Assert.Contains(hits, h => h.Name == "loop" && h.IsDir);
        Assert.Single(hits, h => h.Name == "inner.txt");          // found once, not again via dirlink or loop
    }

    [Fact]
    public void Text_search_finishes_despite_a_loop()
    {
        if (!_canLink) return;
        var hits = new List<SearchHit>();
        FileSearch.Search(_tree, "", new SearchOptions(false, false, ContainingText: "inner"), hits.Add, CancellationToken.None);
        Assert.Single(hits);
    }

    [Fact]
    public void Checksums_do_not_follow_folder_links()
    {
        if (!_canLink) return;
        var files = Checksums.CollectFiles(_t.Path, new[] { ("tree", true) });
        Assert.Single(files, f => f.Name.EndsWith("inner.txt"));
        Assert.DoesNotContain(files, f => f.Name.Contains("/loop/") || f.Name.Contains("/dirlink/"));
    }

    [Fact]
    public void Compare_and_sync_planning_finish_and_do_not_plan_inside_links()
    {
        if (!_canLink) return;
        var dest = _t.Dir("dest");
        var plan = DirectoryComparer.PlanSync(_tree, dest);
        // Everything is new on the source side, so each top-level item is one copy.
        Assert.Contains(plan.Copies, c => c.Source.EndsWith("dirlink") && c.IsDirectory);
        Assert.DoesNotContain(plan.Copies, c => c.Source.Contains("loop" + Path.DirectorySeparatorChar));

        var result = DirectoryComparer.Compare(_tree, _tree, recursive: true);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Copying_a_folder_copies_links_as_links_and_does_not_loop()
    {
        if (!_canLink) return;
        var dest = _t.Dir("copy");
        await FileUtility.CopyDirectoryToFolderAsync(_tree, Path.Combine(dest, "tree"), null, CancellationToken.None);

        string c(string rel) => Path.Combine(dest, "tree", rel);
        Assert.Equal("hello", File.ReadAllText(c("file.txt")));
        Assert.Equal("file.txt", new FileInfo(c("filelink")).LinkTarget);              // still relative
        Assert.Equal("hello", File.ReadAllText(c("filelink")));                         // and works in its new place
        Assert.Equal(_tree, new DirectoryInfo(c("real/loop")).LinkTarget);              // copied, not followed
        Assert.NotNull(new FileInfo(c("broken")).LinkTarget);                           // a broken link copies as is

        // Walking the copy without following links finds just the two real files: nothing was
        // copied through the loop or the folder link.
        Assert.Equal(new[] { "file.txt", "real/inner.txt" }, RealFiles(Path.Combine(dest, "tree")));
    }

    /// <summary>Regular (non-link) files under a folder, relative with '/', without following links.</summary>
    private static string[] RealFiles(string root)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            foreach (var f in Directory.EnumerateFiles(dir))
                if (!LocalFileProvider.IsLink(f))
                    found.Add(Path.GetRelativePath(root, f).Replace('\\', '/'));
            foreach (var d in Directory.EnumerateDirectories(dir))
                if (!LocalFileProvider.IsLink(d)) pending.Push(d);
        }
        return found.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task Copying_a_selected_link_copies_the_link()
    {
        if (!_canLink) return;
        var dest = _t.Dir("copy2");
        var items = new List<TransferItem>
        {
            new(_t.Combine("tree/filelink"), dest, Path.Combine(dest, "filelink"), false),
            new(_t.Combine("tree/dirlink"), dest, Path.Combine(dest, "dirlink"), true),
        };
        await TransferCoordinator.RunAsync(items, move: false, progress: null, CancellationToken.None);

        Assert.Equal("file.txt", new FileInfo(Path.Combine(dest, "filelink")).LinkTarget);
        Assert.Equal(_t.Combine("tree/real"), new DirectoryInfo(Path.Combine(dest, "dirlink")).LinkTarget);
    }

    [Fact]
    public async Task Moving_a_link_moves_the_link_not_what_it_points_to()
    {
        if (!_canLink) return;
        var dest = _t.Dir("moved");
        var items = new List<TransferItem> { new(_t.Combine("tree/dirlink"), dest, Path.Combine(dest, "dirlink"), true) };
        await TransferCoordinator.RunAsync(items, move: true, progress: null, CancellationToken.None);

        Assert.False(LocalFileProvider.IsLink(_t.Combine("tree/dirlink")));
        Assert.True(LocalFileProvider.IsLink(Path.Combine(dest, "dirlink")));
        Assert.True(File.Exists(_t.Combine("tree/real/inner.txt")));
    }

    [Fact]
    public async Task Deleting_a_folder_link_leaves_what_it_points_to()
    {
        if (!_canLink) return;
        var q = new OperationQueue();
        var op = FileOperation.DeleteBatch(new[] { (_t.Combine("tree/dirlink"), true) }, useTrash: false, "delete link");
        q.Enqueue(op);
        await op.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.False(LocalFileProvider.IsLink(_t.Combine("tree/dirlink")));
        Assert.True(File.Exists(_t.Combine("tree/real/inner.txt")));
    }

    [Fact]
    public async Task Archiving_a_folder_does_not_follow_links_inside_it()
    {
        if (!_canLink) return;
        var r = await ArchiveCreator.CreateAsync(_t.Path, "t.zip", new[] { _tree }, ArchiveFormat.Zip);
        using var z = System.IO.Compression.ZipFile.OpenRead(r.ArchivePath);
        Assert.Single(z.Entries, e => e.FullName.EndsWith("inner.txt"));
    }

    [Fact]
    public void CreateLink_makes_file_and_folder_links()
    {
        if (!_canLink) return;
        LocalFileProvider.CreateLink(_t.Combine("made-file"), _t.Combine("tree/file.txt"), isDirectory: false);
        LocalFileProvider.CreateLink(_t.Combine("made-dir"), _t.Combine("tree/real"), isDirectory: true);
        Assert.Equal("hello", File.ReadAllText(_t.Combine("made-file")));
        Assert.True(File.Exists(_t.Combine("made-dir/inner.txt")));
    }
}
