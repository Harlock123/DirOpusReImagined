using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

public class ChecksumTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    // Published test vectors for the message "abc".
    private const string Md5Abc = "900150983cd24fb0d6963f7d28e17f72";
    private const string Sha1Abc = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private const string Sha256Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string Sha512Abc = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a" +
                                     "2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";

    [Theory]
    [InlineData(ChecksumAlgorithm.MD5, Md5Abc)]
    [InlineData(ChecksumAlgorithm.SHA1, Sha1Abc)]
    [InlineData(ChecksumAlgorithm.SHA256, Sha256Abc)]
    [InlineData(ChecksumAlgorithm.SHA512, Sha512Abc)]
    public void Computes_the_standard_test_vectors(ChecksumAlgorithm a, string expected)
    {
        var path = _t.File("abc.txt", "abc");
        Assert.Equal(expected, Checksums.Compute(path, a));
        Assert.Equal(Checksums.HexLength(a), expected.Length);
        Assert.Equal(a, Checksums.FromHexLength(expected.Length));
    }

    [Fact]
    public void Reports_bytes_read_as_it_goes()
    {
        var path = _t.File("big.bin", new string('x', 3_000_000));
        long seen = 0;
        Checksums.Compute(path, ChecksumAlgorithm.SHA256, n => seen += n);
        Assert.Equal(3_000_000, seen);
    }

    [Fact]
    public void Hashes_a_file_inside_an_archive()
    {
        var zip = _t.Combine("a.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("abc.txt").Open())) w.Write("abc");
        Assert.Equal(Sha256Abc, Checksums.Compute(new ArchivePath(zip, "abc.txt").FullUri, ChecksumAlgorithm.SHA256));
    }

    [Fact]
    public void Cancelling_stops_hashing()
    {
        var path = _t.File("big.bin", new string('x', 5_000_000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Checksums.Compute(path, ChecksumAlgorithm.MD5, ct: cts.Token));
    }

    [Theory]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("  ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n")]
    [InlineData("SHA256: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  abc.txt")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad cafe.txt")]
    [InlineData("ba 78 16 bf 8f 01 cf ea 41 41 40 de 5d ae 22 23 b0 03 61 a3 96 17 7a 9c b4 10 ff 61 f2 00 15 ad")]
    [InlineData("BA:78:16:BF:8F:01:CF:EA:41:41:40:DE:5D:AE:22:23:B0:03:61:A3:96:17:7A:9C:B4:10:FF:61:F2:00:15:AD")]
    public void Pasted_hashes_are_normalised(string pasted)
    {
        Assert.Equal(Sha256Abc, Checksums.NormalizeHash(pasted));
        Assert.True(Checksums.Matches(pasted, Sha256Abc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("abc123")]
    public void Text_without_a_whole_hash_normalises_to_nothing(string pasted)
    {
        Assert.Equal("", Checksums.NormalizeHash(pasted));
        Assert.False(Checksums.Matches(pasted, pasted));
    }

    [Theory]
    [InlineData("SHA256SUMS", true)]
    [InlineData("MD5SUMS", true)]
    [InlineData("release.sha256", true)]
    [InlineData("release.SHA512", true)]
    [InlineData("x.md5", true)]
    [InlineData("x.sha1", true)]
    [InlineData("x.sha256sum", true)]
    [InlineData("readme.txt", false)]
    [InlineData("sha256.c", false)]
    public void Recognises_checksum_file_names(string name, bool expected)
        => Assert.Equal(expected, Checksums.IsChecksumFileName(name));

    [Fact]
    public void Parses_gnu_binary_mode_and_bsd_lines_and_skips_the_rest()
    {
        string text =
            "# comment\r\n" +
            Sha256Abc + "  abc.txt\r\n" +
            Md5Abc + " *bin/abc.bin\n" +
            "SHA1 (sub dir/abc.txt) = " + Sha1Abc + "\n" +
            "\n" +
            "this line is not a checksum\n" +
            "deadbeef  too-short-to-be-any-algorithm\n";

        var entries = Checksums.Parse(text);
        Assert.Equal(3, entries.Count);
        Assert.Equal(new ChecksumEntry(Sha256Abc, "abc.txt", ChecksumAlgorithm.SHA256), entries[0]);
        Assert.Equal(new ChecksumEntry(Md5Abc, "bin/abc.bin", ChecksumAlgorithm.MD5), entries[1]);
        Assert.Equal(new ChecksumEntry(Sha1Abc, "sub dir/abc.txt", ChecksumAlgorithm.SHA1), entries[2]);
    }

    [Fact]
    public void Names_with_a_backslash_or_newline_are_escaped_and_read_back()
    {
        string line = Checksums.FormatLine(Sha256Abc, "odd\nname");
        Assert.StartsWith("\\", line);
        var entry = Assert.Single(Checksums.Parse(line + "\n"));
        Assert.Equal("odd\nname", entry.Name);
    }

    [Fact]
    public void Saved_format_round_trips()
    {
        var text = Checksums.FormatFile(new[] { (Sha256Abc, "a.txt"), (Sha256Abc, "dir/b.txt") });
        Assert.Equal($"{Sha256Abc}  a.txt\n{Sha256Abc}  dir/b.txt\n", text);
        Assert.Equal(new[] { "a.txt", "dir/b.txt" }, Checksums.Parse(text).Select(e => e.Name));
    }

    [Fact]
    public void Collects_selected_files_and_everything_in_selected_folders()
    {
        _t.File("one.txt", "1");
        _t.File("folder/two.txt", "2");
        _t.File("folder/sub/three.txt", "3");
        _t.Dir("folder/empty");

        var files = Checksums.CollectFiles(_t.Path, new[] { ("one.txt", false), ("folder", true) });
        Assert.Equal(new[] { "one.txt", "folder/two.txt", "folder/sub/three.txt" }, files.Select(f => f.Name));
        Assert.Equal(1, files[0].Size);
        Assert.Equal(_t.Combine("folder/sub/three.txt"), files[2].Path);
    }

    [Fact]
    public void Verifies_files_against_a_checksum_file()
    {
        _t.File("good.txt", "abc");
        _t.File("sub/also-good.txt", "abc");
        _t.File("changed.txt", "abc but edited");
        var entries = Checksums.Parse(Checksums.FormatFile(new[]
        {
            (Sha256Abc, "good.txt"),
            (Sha256Abc, "sub/also-good.txt"),
            (Sha256Abc, "changed.txt"),
            (Sha256Abc, "gone.txt"),
        }));

        var seen = new List<VerifyResult>();
        var results = Checksums.Verify(_t.Path, entries, seen.Add);

        Assert.Equal(new[] { VerifyStatus.Ok, VerifyStatus.Ok, VerifyStatus.Mismatch, VerifyStatus.Missing },
                     results.Select(r => r.Status));
        Assert.Equal(results, seen);
        Assert.NotEqual(Sha256Abc, results[2].ActualHash);
    }

    [Fact]
    public void Entries_resolve_relative_to_the_checksum_files_folder()
    {
        string folder = _t.Path;
        Assert.Equal(_t.Combine("a/b.txt"), Checksums.ResolveEntry(folder, "./a/b.txt"));
        Assert.Equal(_t.Combine("c.txt"), Checksums.ResolveEntry(folder, "*c.txt"));
    }

    [Theory]
    [InlineData("sha256sum", ChecksumAlgorithm.SHA256)]
    [InlineData("md5sum", ChecksumAlgorithm.MD5)]
    public void Files_we_write_are_accepted_by_the_coreutils_tools(string tool, ChecksumAlgorithm a)
    {
        if (!ToolExists(tool)) return;   // not installed (e.g. Windows): nothing to compare with

        _t.File("x.txt", "hello");
        _t.File("dir/y.bin", new string('y', 10_000));
        var files = Checksums.CollectFiles(_t.Path, new[] { ("x.txt", false), ("dir", true) });
        var text = Checksums.FormatFile(files.Select(f => (Checksums.Compute(f.Path, a), f.Name)));
        File.WriteAllText(_t.Combine("SUMS"), text, new UTF8Encoding(false));

        var (exit, output) = Run(tool, "-c SUMS", _t.Path);
        Assert.True(exit == 0, output);

        // And the other way: what the tool writes, we read and verify.
        var (_, theirs) = Run(tool, "x.txt dir/y.bin", _t.Path);
        var results = Checksums.Verify(_t.Path, Checksums.Parse(theirs));
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(VerifyStatus.Ok, r.Status));
    }

    private static bool ToolExists(string tool)
    {
        try { return Run(tool, "--version", Path.GetTempPath()).Exit == 0; }
        catch { return false; }
    }

    private static (int Exit, string Output) Run(string file, string args, string dir)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }
}
