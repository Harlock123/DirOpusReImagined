using System.Formats.Tar;
using System.IO.Compression;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

public class ArchiveCreatorNamingTests
{
    [Theory]
    [InlineData("photos", ArchiveFormat.Zip, "photos.zip")]
    [InlineData("photos.zip", ArchiveFormat.TarGz, "photos.tar.gz")]
    [InlineData("photos.tar.gz", ArchiveFormat.Zip, "photos.zip")]
    [InlineData("photos.tgz", ArchiveFormat.Tar, "photos.tar")]
    [InlineData("PHOTOS.ZIP", ArchiveFormat.Tar, "PHOTOS.tar")]
    [InlineData("notes.txt", ArchiveFormat.Zip, "notes.txt.zip")]
    [InlineData(".zip", ArchiveFormat.Tar, ".zip.tar")]
    public void WithExtension_swaps_only_archive_extensions(string name, ArchiveFormat f, string expected)
        => Assert.Equal(expected, ArchiveCreator.WithExtension(name, f));

    [Theory]
    [InlineData("a.zip", ArchiveFormat.Zip)]
    [InlineData("a.TAR.GZ", ArchiveFormat.TarGz)]
    [InlineData("a.tgz", ArchiveFormat.TarGz)]
    [InlineData("a.tar", ArchiveFormat.Tar)]
    public void FormatFromName_recognises_extensions(string name, ArchiveFormat expected)
        => Assert.Equal(expected, ArchiveCreator.FormatFromName(name));

    [Theory]
    [InlineData("a")]
    [InlineData("a.gz")]
    public void FormatFromName_is_null_for_other_names(string name)
        => Assert.Null(ArchiveCreator.FormatFromName(name));

    [Fact]
    public void UniqueName_numbers_taken_names_and_keeps_double_extensions_together()
    {
        var taken = new HashSet<string> { "a.zip", "a_1.zip", "b.tar.gz" };
        Assert.Equal("new.zip", ArchiveCreator.UniqueName("new.zip", taken.Contains));
        Assert.Equal("a_2.zip", ArchiveCreator.UniqueName("a.zip", taken.Contains));
        Assert.Equal("b_1.tar.gz", ArchiveCreator.UniqueName("b.tar.gz", taken.Contains));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sub/a.zip")]
    [InlineData("sub\\a.zip")]
    [InlineData("..")]
    public void ValidateName_rejects_bad_names(string name)
        => Assert.NotNull(ArchiveCreator.ValidateName(name));

    [Fact]
    public void ValidateName_accepts_a_normal_name()
        => Assert.Null(ArchiveCreator.ValidateName("Holiday photos 2024.zip"));
}

public class ArchiveCreatorTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly string _src;
    private readonly string _dest;

    public ArchiveCreatorTests()
    {
        _src = _t.Dir("src");
        _dest = _t.Dir("dest");
        _t.File("src/top.txt", "top file", Times.Base);
        _t.File("src/Folder/a.txt", "aaa", Times.Base);
        _t.File("src/Folder/.hidden", "secret", Times.Base);
        _t.File("src/Folder/sub/b.bin", new string('b', 300_000), Times.Base);
        _t.Dir("src/Folder/empty");
    }

    public void Dispose() => _t.Dispose();

    private string S(string rel) => _t.Combine("src/" + rel);

    private Task<ArchiveResult> Create(string name, ArchiveFormat f, params string[] rels)
        => ArchiveCreator.CreateAsync(_dest, name, rels.Select(S).ToList(), f);

    private static readonly string[] ExpectedEntries =
    {
        "Folder/", "Folder/.hidden", "Folder/a.txt", "Folder/empty/", "Folder/sub/", "Folder/sub/b.bin", "top.txt",
    };

    [Fact]
    public async Task Zip_holds_files_folders_hidden_files_and_empty_folders_with_forward_slashes()
    {
        var r = await Create("out.zip", ArchiveFormat.Zip, "top.txt", "Folder");

        Assert.Equal(Path.Combine(_dest, "out.zip"), r.ArchivePath);
        Assert.Equal(4, r.Files);
        Assert.Equal(3, r.Folders);
        Assert.Empty(r.Skipped);

        using var z = ZipFile.OpenRead(r.ArchivePath);
        Assert.Equal(ExpectedEntries, z.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
        using var reader = new StreamReader(z.GetEntry("Folder/a.txt")!.Open());
        Assert.Equal("aaa", reader.ReadToEnd());
        Assert.Equal(300_000, z.GetEntry("Folder/sub/b.bin")!.Length);
    }

    [Fact]
    public async Task Zip_keeps_file_modification_times()
    {
        var r = await Create("t.zip", ArchiveFormat.Zip, "top.txt");
        using var z = ZipFile.OpenRead(r.ArchivePath);
        // Zip stores local time at two-second resolution.
        var stored = z.GetEntry("top.txt")!.LastWriteTime.UtcDateTime;
        Assert.True(Math.Abs((stored - Times.Base).TotalSeconds) <= 2, $"stored {stored:o}");
    }

    [Fact]
    public async Task Zip_extracts_back_to_the_same_tree()
    {
        var r = await Create("round.zip", ArchiveFormat.Zip, "Folder");
        var outDir = _t.Dir("extracted");
        ZipFile.ExtractToDirectory(r.ArchivePath, outDir);

        Assert.Equal("secret", File.ReadAllText(Path.Combine(outDir, "Folder", ".hidden")));
        Assert.True(Directory.Exists(Path.Combine(outDir, "Folder", "empty")));
        Assert.Equal(300_000, new FileInfo(Path.Combine(outDir, "Folder", "sub", "b.bin")).Length);
    }

    [Theory]
    [InlineData(ArchiveFormat.TarGz, "out.tar.gz")]
    [InlineData(ArchiveFormat.Tar, "out.tar")]
    public async Task Tar_formats_hold_the_same_entries_and_contents(ArchiveFormat f, string name)
    {
        var r = await Create(name, f, "top.txt", "Folder");

        var entries = ReadTar(r.ArchivePath, f == ArchiveFormat.TarGz);
        Assert.Equal(ExpectedEntries, entries.Keys.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("top file", entries["top.txt"]);
        Assert.Equal(300_000, entries["Folder/sub/b.bin"]!.Length);
    }

    [Fact]
    public async Task TarGz_is_actually_compressed()
    {
        var r = await Create("c.tar.gz", ArchiveFormat.TarGz, "Folder");
        // 300 KB of one repeated byte compresses to almost nothing.
        Assert.True(new FileInfo(r.ArchivePath).Length < 20_000);
    }

    [Fact]
    public async Task Tar_keeps_the_execute_bit_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = _t.File("src/run.sh", "#!/bin/sh\n");
        File.SetUnixFileMode(script, (UnixFileMode)0b111_101_101);

        var r = await Create("x.tar", ArchiveFormat.Tar, "run.sh");
        using var fs = File.OpenRead(r.ArchivePath);
        using var tr = new TarReader(fs);
        var e = tr.GetNextEntry()!;
        Assert.True(e.Mode.HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task A_taken_name_gets_a_number_instead_of_overwriting()
    {
        File.WriteAllText(Path.Combine(_dest, "out.zip"), "existing");
        var r = await Create("out.zip", ArchiveFormat.Zip, "top.txt");

        Assert.Equal(Path.Combine(_dest, "out_1.zip"), r.ArchivePath);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_dest, "out.zip")));
    }

    [Fact]
    public async Task A_missing_source_is_reported_as_skipped_not_fatal()
    {
        var r = await ArchiveCreator.CreateAsync(_dest, "m.zip", new[] { S("top.txt"), S("gone.txt") }, ArchiveFormat.Zip);
        var skip = Assert.Single(r.Skipped);
        Assert.EndsWith("gone.txt", skip.Path);
        Assert.Equal(1, r.Files);
    }

    [Fact]
    public async Task Cancelling_leaves_nothing_behind()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArchiveCreator.CreateAsync(_dest, "c.zip", new[] { S("Folder") }, ArchiveFormat.Zip, ct: cts.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dest));
    }

    [Fact]
    public async Task Progress_reaches_the_total()
    {
        var reports = new List<ArchiveProgress>();
        var progress = new SyncProgress<ArchiveProgress>(reports.Add);
        await ArchiveCreator.CreateAsync(_dest, "p.zip", new[] { S("Folder") }, ArchiveFormat.Zip, progress: progress);

        var last = reports.Last();
        Assert.Equal(last.BytesTotal, last.BytesDone);
        Assert.Equal(3, last.FilesTotal);
        Assert.Equal(3, last.FilesDone);
    }

    [Fact]
    public async Task Writing_into_an_archive_is_refused()
    {
        var first = await Create("inner.zip", ArchiveFormat.Zip, "top.txt");
        var insideArchive = ArchivePath.RootUriFor(first.ArchivePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArchiveCreator.CreateAsync(insideArchive, "nested.zip", new[] { S("top.txt") }, ArchiveFormat.Zip));
    }

    [Fact]
    public async Task Files_inside_an_archive_can_be_re_archived_as_tar()
    {
        // Entries read out of a zip aren't seekable, which exercises the tar spooling path.
        var zip = await Create("source.zip", ArchiveFormat.Zip, "Folder");
        var folderInZip = new ArchivePath(zip.ArchivePath, "Folder").FullUri;

        var r = await ArchiveCreator.CreateAsync(_dest, "from-zip.tar", new[] { folderInZip }, ArchiveFormat.Tar);

        Assert.Empty(r.Skipped);
        var entries = ReadTar(r.ArchivePath, gz: false);
        Assert.Equal("aaa", entries["Folder/a.txt"]);
        Assert.Equal(300_000, entries["Folder/sub/b.bin"]!.Length);
    }

    [Fact]
    public async Task A_bad_name_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => Create("sub/x.zip", ArchiveFormat.Zip, "top.txt"));

    /// <summary>Entry name → text content (null for directories).</summary>
    private static Dictionary<string, string?> ReadTar(string path, bool gz)
    {
        var result = new Dictionary<string, string?>();
        using var fs = File.OpenRead(path);
        using Stream s = gz ? new GZipStream(fs, CompressionMode.Decompress) : fs;
        using var tr = new TarReader(s);
        while (tr.GetNextEntry() is { } e)
        {
            string? text = null;
            if (e.DataStream != null)
            {
                using var r = new StreamReader(e.DataStream);
                text = r.ReadToEnd();
            }
            result[e.Name] = text;
        }
        return result;
    }

    /// <summary>IProgress that reports synchronously (Progress&lt;T&gt; posts to a sync context).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
