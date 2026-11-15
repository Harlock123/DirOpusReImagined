using System.IO.Compression;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

public class ArchiveFileProviderTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly ArchiveFileProvider _p = new();
    private readonly string _zip;
    private readonly string _root;
    private static readonly DateTime EntryTime = new(2023, 6, 1, 10, 30, 0);

    public ArchiveFileProviderTests()
    {
        _zip = _t.Combine("sample.zip");
        using (var z = ZipFile.Open(_zip, ZipArchiveMode.Create))
        {
            Add(z, "readme.txt", "hello zip");
            Add(z, "docs/a.md", "# A");
            Add(z, "docs/sub/b.md", "bbbbb");
            z.CreateEntry("empty/"); // explicit directory entry
            Add(z, "./dotslash.txt", "ds"); // tar-style "./" prefix is normalized away
        }
        _root = ArchivePath.RootUriFor(_zip);
    }

    private static void Add(ZipArchive z, string name, string content)
    {
        var e = z.CreateEntry(name);
        e.LastWriteTime = EntryTime;
        using var w = new StreamWriter(e.Open());
        w.Write(content);
    }

    public void Dispose() => _t.Dispose();

    private string U(string entry) => new ArchivePath(_zip, entry).FullUri;

    [Fact]
    public void Lists_root_with_explicit_and_implicit_directories()
    {
        var dirs = _p.EnumerateDirectories(_root).Select(d => d.Name).OrderBy(n => n).ToArray();
        var files = _p.EnumerateFiles(_root).Select(f => f.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "docs", "empty" }, dirs);
        Assert.Equal(new[] { "dotslash.txt", "readme.txt" }, files);
    }

    [Fact]
    public void Lists_nested_entries_with_uris_sizes_and_times()
    {
        var docs = U("docs");
        var sub = Assert.Single(_p.EnumerateDirectories(docs));
        Assert.Equal("sub", sub.Name);
        Assert.Equal(U("docs/sub"), sub.Path);
        Assert.True(sub.IsDirectory);

        var a = Assert.Single(_p.EnumerateFiles(docs));
        Assert.Equal("a.md", a.Name);
        Assert.Equal(U("docs/a.md"), a.Path);
        Assert.Equal(3, a.Size);
        Assert.Equal(EntryTime.Date, a.LastModified.Date);

        Assert.Equal("b.md", Assert.Single(_p.EnumerateFiles(U("docs/sub"))).Name);
        Assert.Empty(_p.EnumerateFiles(U("empty")));
    }

    [Fact]
    public void Reads_entry_contents()
    {
        using var s = _p.OpenRead(U("docs/sub/b.md"));
        Assert.Equal("bbbbb", new StreamReader(s).ReadToEnd());
    }

    [Fact]
    public void Entry_paths_with_backslashes_are_normalized()
    {
        using var s = _p.OpenRead("archive://" + _zip + "!/docs\\a.md");
        Assert.Equal("# A", new StreamReader(s).ReadToEnd());
    }

    [Fact]
    public void Reading_a_missing_entry_throws_FileNotFound()
        => Assert.Throws<FileNotFoundException>(() => _p.OpenRead(U("nope.txt")));

    [Fact]
    public void Exists_checks()
    {
        Assert.True(_p.DirectoryExists(_root));
        Assert.True(_p.DirectoryExists(U("docs")));      // implicit
        Assert.True(_p.DirectoryExists(U("empty")));     // explicit
        Assert.True(_p.DirectoryExists(U("docs/sub")));
        Assert.False(_p.DirectoryExists(U("readme.txt")));
        Assert.False(_p.DirectoryExists(U("nope")));

        Assert.True(_p.FileExists(U("readme.txt")));
        Assert.True(_p.FileExists(U("docs/a.md")));
        Assert.False(_p.FileExists(U("docs")));
        Assert.False(_p.FileExists(_root));
        Assert.False(_p.FileExists("/not/an/archive/uri"));
    }

    [Fact]
    public void Stat_files_directories_and_root()
    {
        var f = _p.Stat(U("docs/sub/b.md"))!;
        Assert.Equal("b.md", f.Name);
        Assert.False(f.IsDirectory);
        Assert.Equal(5, f.Size);

        var d = _p.Stat(U("docs"))!;
        Assert.True(d.IsDirectory);
        Assert.Equal("docs", d.Name);

        var root = _p.Stat(_root)!;
        Assert.True(root.IsDirectory);
        Assert.Equal("sample.zip", root.Name);

        Assert.Null(_p.Stat(U("missing")));
    }

    [Fact]
    public void GetDirectorySize_sums_files_beneath()
    {
        Assert.Equal(3 + 5, _p.GetDirectorySize(U("docs"), true));
        Assert.Equal(9 + 3 + 5 + 2, _p.GetDirectorySize(_root, true));
    }

    [Fact]
    public void ListEntries_returns_the_flat_normalized_list()
    {
        var keys = _p.ListEntries(_zip).Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "docs/a.md", "docs/sub/b.md", "dotslash.txt", "empty", "readme.txt" }, keys);
        Assert.True(_p.ListEntries(_zip).Single(e => e.Key == "empty").IsDirectory);
    }

    [Fact]
    public void All_writes_are_refused()
    {
        Assert.Throws<NotSupportedException>(() => _p.OpenWrite(U("new.txt")));
        Assert.Throws<NotSupportedException>(() => _p.CreateDirectory(U("newdir")));
        Assert.Throws<NotSupportedException>(() => _p.DeleteFile(U("readme.txt")));
        Assert.Throws<NotSupportedException>(() => _p.DeleteDirectory(U("docs"), true));
        Assert.Throws<NotSupportedException>(() => _p.CopyFile(U("readme.txt"), U("copy.txt"), true));
        Assert.Throws<NotSupportedException>(() => _p.MoveFile(U("readme.txt"), U("moved.txt")));
        Assert.Throws<NotSupportedException>(() => _p.MoveDirectory(U("docs"), U("docs2")));

        using var s = _p.OpenRead(U("readme.txt"));
        Assert.False(s.CanWrite);
        Assert.Throws<NotSupportedException>(() => s.Write(new byte[1], 0, 1));
    }

    [Fact]
    public void Listing_reflects_a_rewritten_archive()
    {
        Assert.Equal(2, _p.EnumerateFiles(_root).Count());

        File.Delete(_zip);
        using (var z = ZipFile.Open(_zip, ZipArchiveMode.Create)) Add(z, "only.txt", "o");
        File.SetLastWriteTimeUtc(_zip, Times.Later); // cache is keyed on the archive's mtime

        Assert.Equal("only.txt", Assert.Single(_p.EnumerateFiles(_root)).Name);
    }

    [Fact]
    public void Search_works_inside_an_archive_through_the_registry()
    {
        var hits = new List<SearchHit>();
        FileSearch.Search(_root, "*.md", new SearchOptions(false, false), hits.Add, CancellationToken.None);
        Assert.Equal(new[] { U("docs/a.md"), U("docs/sub/b.md") }, hits.Select(h => h.FullPath).OrderBy(x => x));
    }

    [Fact]
    public void Tar_gz_archives_are_listed_and_read_via_the_streaming_fallback()
    {
        var src = _t.Dir("tarsrc");
        _t.File("tarsrc/inner/x.txt", "tarred");
        var tar = _t.Combine("t.tar");
        System.Formats.Tar.TarFile.CreateFromDirectory(src, tar, includeBaseDirectory: false);
        var tgz = _t.Combine("t.tar.gz");
        using (var input = File.OpenRead(tar))
        using (var output = new GZipStream(File.Create(tgz), CompressionLevel.Fastest))
            input.CopyTo(output);

        var root = ArchivePath.RootUriFor(tgz);
        Assert.Equal("inner", Assert.Single(_p.EnumerateDirectories(root)).Name);
        var inner = new ArchivePath(tgz, "inner").FullUri;
        Assert.Equal("x.txt", Assert.Single(_p.EnumerateFiles(inner)).Name);
        using var s = _p.OpenRead(new ArchivePath(tgz, "inner/x.txt").FullUri);
        Assert.Equal("tarred", new StreamReader(s).ReadToEnd());
    }
}

public class ArchivePathTests
{
    [Fact]
    public void Parse_splits_on_the_marker_and_normalizes_the_entry()
    {
        var ap = ArchivePath.Parse("archive:///home/u/a.zip!/docs\\sub/");
        Assert.Equal("/home/u/a.zip", ap.ArchiveFsPath);
        Assert.Equal("docs/sub", ap.EntryPath);
        Assert.False(ap.IsRoot);
    }

    [Fact]
    public void Parse_without_marker_is_the_root_and_keeps_windows_paths()
    {
        var ap = ArchivePath.Parse(@"archive://C:\data\a.zip");
        Assert.Equal(@"C:\data\a.zip", ap.ArchiveFsPath);
        Assert.True(ap.IsRoot);
    }

    [Fact]
    public void Parse_rejects_non_archive_uris()
        => Assert.Throws<ArgumentException>(() => ArchivePath.Parse("/home/u/a.zip"));

    [Fact]
    public void FullUri_round_trips()
    {
        const string uri = "archive:///home/u/a.zip!/docs/a.md";
        Assert.Equal(uri, ArchivePath.Parse(uri).FullUri);
        Assert.Equal("archive:///x.zip!/", ArchivePath.RootUriFor("/x.zip"));
    }

    [Fact]
    public void Join_and_WithEntry()
    {
        var root = new ArchivePath("/a.zip", "");
        Assert.Equal("docs", root.Join("/docs/").EntryPath);
        Assert.Equal("docs/sub", root.Join("docs").Join("sub").EntryPath);
        Assert.Equal("x/y", root.WithEntry("/x/y/").EntryPath);
    }

    [Fact]
    public void ParentUri_steps_up_entries_then_out_of_the_archive()
    {
        var zip = Path.Combine(Path.GetTempPath(), "a.zip");
        Assert.Equal(new ArchivePath(zip, "docs").FullUri, new ArchivePath(zip, "docs/sub").ParentUri());
        Assert.Equal(new ArchivePath(zip, "").FullUri, new ArchivePath(zip, "docs").ParentUri());
        Assert.Equal(Path.GetDirectoryName(zip), new ArchivePath(zip, "").ParentUri());
    }

    [Theory]
    [InlineData("a.zip", true)]
    [InlineData("A.ZIP", true)]
    [InlineData("a.7z", true)]
    [InlineData("a.rar", true)]
    [InlineData("a.tar", true)]
    [InlineData("a.tar.gz", true)]
    [InlineData("a.tgz", true)]
    [InlineData("a.tar.bz2", true)]
    [InlineData("a.tbz2", true)]
    [InlineData("a.gz", true)]
    [InlineData("a.txt", false)]
    [InlineData("zip", false)]
    [InlineData("", false)]
    public void IsArchiveFile_by_extension(string name, bool expected)
        => Assert.Equal(expected, ArchivePath.IsArchiveFile(name));

    [Theory]
    [InlineData("archive://x", true)]
    [InlineData("ARCHIVE://x", true)]
    [InlineData("cloud://x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsArchiveUri(string? uri, bool expected) => Assert.Equal(expected, ArchivePath.IsArchiveUri(uri!));
}
