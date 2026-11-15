using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class LocalFileProviderTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly LocalFileProvider _p = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Handles_any_path_and_is_not_remote()
    {
        Assert.True(_p.CanHandle("/anything"));
        Assert.False(_p.IsRemote);
    }

    [Fact]
    public void Enumerates_directories_and_files_separately_with_metadata()
    {
        _t.Dir("sub");
        var f = _t.File("a.txt", "hello", Times.Base);

        var dirs = _p.EnumerateDirectories(_t.Path).ToList();
        var files = _p.EnumerateFiles(_t.Path).ToList();

        var d = Assert.Single(dirs);
        Assert.Equal("sub", d.Name);
        Assert.True(d.IsDirectory);
        Assert.Equal(_t.Combine("sub"), d.Path);

        var e = Assert.Single(files);
        Assert.Equal("a.txt", e.Name);
        Assert.Equal(f, e.Path);
        Assert.False(e.IsDirectory);
        Assert.Equal(5, e.Size);
        Assert.Equal(Times.Base, e.LastModified.ToUniversalTime());
        Assert.StartsWith("RW-", e.AttributeString);
    }

    [Fact]
    public void Stat_file_directory_and_missing()
    {
        var f = _t.File("a.txt", "abc");
        var s = _p.Stat(f)!;
        Assert.False(s.IsDirectory);
        Assert.Equal(3, s.Size);
        Assert.Equal("a.txt", s.Name);

        var ds = _p.Stat(_t.Path)!;
        Assert.True(ds.IsDirectory);
        Assert.Equal(0, ds.Size);

        Assert.Null(_p.Stat(_t.Combine("nope")));
    }

    [Fact]
    public void Exists_checks_distinguish_files_and_directories()
    {
        var f = _t.File("a.txt");
        var d = _t.Dir("d");
        Assert.True(_p.FileExists(f));
        Assert.False(_p.DirectoryExists(f));
        Assert.True(_p.DirectoryExists(d));
        Assert.False(_p.FileExists(d));
    }

    [Fact]
    public void CreateDirectory_creates_nested_folders()
    {
        var path = _t.Combine("x/y/z");
        _p.CreateDirectory(path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CopyFile_respects_overwrite()
    {
        var src = _t.File("src.txt", "new");
        var dst = _t.File("dst.txt", "old");

        Assert.Throws<IOException>(() => _p.CopyFile(src, dst, overwrite: false));
        Assert.Equal("old", File.ReadAllText(dst));

        _p.CopyFile(src, dst, overwrite: true);
        Assert.Equal("new", File.ReadAllText(dst));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public async Task CopyFileAsync_copies_and_reports_final_progress()
    {
        var content = new string('x', 200_000);
        var src = _t.File("big.txt", content);
        var dst = _t.Combine("copy.txt");
        var reports = new List<TransferProgress>();

        await _p.CopyFileAsync(src, dst, overwrite: false, new SyncProgress<TransferProgress>(reports.Add));

        Assert.Equal(content, File.ReadAllText(dst));
        var last = reports.Last();
        Assert.Equal("big.txt", last.CurrentFile);
        Assert.Equal(200_000, last.BytesDone);
        Assert.Equal(200_000, last.BytesTotal);
    }

    [Fact]
    public async Task CopyFileAsync_refuses_to_overwrite_when_not_allowed()
    {
        var src = _t.File("a.txt", "new");
        var dst = _t.File("b.txt", "old");
        await Assert.ThrowsAsync<IOException>(() => _p.CopyFileAsync(src, dst, false, null));
        Assert.Equal("old", File.ReadAllText(dst));
    }

    [Fact]
    public async Task CopyFileAsync_honours_cancellation()
    {
        var src = _t.File("a.txt", "abc");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _p.CopyFileAsync(src, _t.Combine("b.txt"), false, null, cts.Token));
    }

    [Fact]
    public void MoveFile_and_MoveDirectory()
    {
        var f = _t.File("a.txt", "abc");
        var target = _t.Combine("b.txt");
        _p.MoveFile(f, target);
        Assert.False(File.Exists(f));
        Assert.Equal("abc", File.ReadAllText(target));

        _t.File("d1/inner.txt", "i");
        _p.MoveDirectory(_t.Combine("d1"), _t.Combine("d2"));
        Assert.False(Directory.Exists(_t.Combine("d1")));
        Assert.True(File.Exists(_t.Combine("d2/inner.txt")));
    }

    [Fact]
    public void Delete_file_and_directory()
    {
        var f = _t.File("a.txt");
        _p.DeleteFile(f);
        Assert.False(File.Exists(f));

        _t.File("d/inner.txt");
        Assert.ThrowsAny<IOException>(() => _p.DeleteDirectory(_t.Combine("d"), recursive: false));
        _p.DeleteDirectory(_t.Combine("d"), recursive: true);
        Assert.False(Directory.Exists(_t.Combine("d")));
    }

    [Fact]
    public void ComputeHash_is_uppercase_md5_or_null_for_missing()
    {
        var f = _t.File("abc.txt", "abc");
        Assert.Equal("900150983CD24FB0D6963F7D28E17F72", _p.ComputeHash(f));
        Assert.Null(_p.ComputeHash(_t.Combine("missing.txt")));
    }

    [Fact]
    public void GetDirectorySize_shallow_and_recursive()
    {
        _t.File("a.txt", "12345");
        _t.File("sub/b.txt", "123");
        _t.File("sub/deeper/c.txt", "12");
        Assert.Equal(5, _p.GetDirectorySize(_t.Path, recursive: false));
        Assert.Equal(10, _p.GetDirectorySize(_t.Path, recursive: true));
        Assert.Equal(0, _p.GetDirectorySize(_t.Combine("missing"), recursive: true));
    }

    [Fact]
    public void OpenRead_and_OpenWrite_round_trip()
    {
        var path = _t.Combine("rw.bin");
        using (var w = _p.OpenWrite(path)) w.Write(new byte[] { 1, 2, 3 });
        using var r = _p.OpenRead(path);
        var buf = new byte[10];
        Assert.Equal(3, r.Read(buf, 0, 10));
        Assert.Equal(new byte[] { 1, 2, 3 }, buf[..3]);
    }

    internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
