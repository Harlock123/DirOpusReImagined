using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DirOpusReImagined.FileSystem.Archive;
using DirOpusReImagined.FileSystem.Preview;
using DirOpusReImagined.FileSystem.Rclone;
using SharpCompress.Archives;
using SkiaSharp;

namespace DirOpusReImagined.Tests;

public class DoriScriptTests
{
    private sealed class FakeHost : IScriptHost
    {
        public readonly List<string> Calls = new();
        public bool LeftActive { get; set; } = true;
        public string Left = "/l", Right = "/r";
        public HashSet<string> Existing = new();
        public int Selected;
        public string PanelPath(bool left) => left ? Left : Right;
        public Task GoAsync(bool left, string path) { Calls.Add($"go {(left ? "L" : "R")} {path}"); return Task.CompletedTask; }
        public Task NewTabAsync(bool left, string path) { Calls.Add($"tab {path}"); return Task.CompletedTask; }
        public int Select(bool left, string patterns, bool add) { Calls.Add($"select {patterns} {add}"); Selected = 2; return 2; }
        public void SelectAll(bool left) => Calls.Add("all");
        public void Deselect(bool left) { Calls.Add("deselect"); Selected = 0; }
        public void Invert(bool left) => Calls.Add("invert");
        public int SelectedCount(bool left) => Selected;
        public void Filter(bool left, string text) => Calls.Add($"filter {text}");
        public Task RefreshAsync(bool left) { Calls.Add("refresh"); return Task.CompletedTask; }
        public bool Command(bool left, string name) { Calls.Add($"command {name} {(left ? "L" : "R")}"); return name != "Nope"; }
        public bool Button(string label) { Calls.Add($"button {label}"); return true; }
        public Task RunProgramAsync(bool left, string commandLine) { Calls.Add($"run {commandLine}"); return Task.CompletedTask; }
        public Task WaitForOperationsAsync(CancellationToken ct) { Calls.Add("wait"); return Task.CompletedTask; }
        public Task MessageAsync(string text) { Calls.Add($"message {text}"); return Task.CompletedTask; }
        public bool Exists(string path) => Existing.Contains(path);
    }

    private static async Task<List<string>> Run(string script, FakeHost? host = null)
    {
        host ??= new FakeHost();
        await DoriScript.RunAsync(DoriScript.Parse(script), host);
        return host.Calls;
    }

    [Fact]
    public async Task Commands_run_in_order_on_the_chosen_panel()
    {
        var calls = await Run("go /a; select *.jpg *.png\nright\ncopy  # to the other side\nfilter tag:red");
        Assert.Equal(new[] { "go L /a", "select *.jpg *.png False", "command Copy R", "filter tag:red" }, calls);
    }

    [Fact]
    public async Task If_else_end_and_conditions()
    {
        var host = new FakeHost { Existing = { "/l/x.txt" } };
        var calls = await Run("if exists %PANEL%/x.txt\n message yes\nelse\n message no\nend\nif not exists /nope; message gone; end\nif selected; copy; end", host);
        Assert.Equal(new[] { "message yes", "message gone" }, calls);
    }

    [Fact]
    public async Task Paths_expand()
    {
        var calls = await Run("go %OTHER%/sub\ntab \"%LEFT% and %RIGHT%\"\ngo ~/docs");
        Assert.Equal("go L /r/sub", calls[0]);
        Assert.Equal("tab /l and /r", calls[1]);
        Assert.Equal("go L " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/docs", calls[2]);
    }

    [Fact]
    public void Semicolons_inside_quotes_stay()
        => Assert.Equal(new[] { "message \"a;b\"", " go x " }, DoriScript.SplitStatements("message \"a;b\"; go x # done").ToArray());

    [Theory]
    [InlineData("frobnicate", "isn't a command")]
    [InlineData("if exists /x\ngo /y", "has no end")]
    [InlineData("else", "else without")]
    [InlineData("end", "end without")]
    [InlineData("if sunny\nend", "if needs")]
    [InlineData("go", "needs something")]
    public void Mistakes_are_said_with_their_line(string script, string message)
    {
        var ex = Assert.Throws<ScriptException>(() => DoriScript.Parse(script));
        Assert.Contains(message, ex.Message);
        Assert.StartsWith("Line ", ex.Message);
    }

    [Fact]
    public async Task An_unknown_panel_command_stops_the_script()
        => await Assert.ThrowsAsync<ScriptException>(() => Run("command Nope\ngo /never"));

    [Fact]
    public void Scripts_travel_through_the_pipe_and_command_line()
    {
        string script = "go /x\nselect *.jpg";
        Assert.Equal(script, SingleInstance.DecodeScript(SingleInstance.EncodeScript(script)));
        Assert.Null(SingleInstance.DecodeScript("/home/me"));
        var args = new[] { "--cmd", "go /a; copy", "/b" };
        Assert.Equal(new[] { "go /a; copy" }, SingleInstance.ScriptsFrom(args));
        Assert.Equal(new[] { Path.GetFullPath("/b") }, SingleInstance.PathsFrom(args, "/"));
    }
}

public class SnapshotsTests
{
    [Fact]
    public void Snapper_and_zfs_snapshots_are_found_newest_first()
    {
        using var t = new TempDir();
        string file = t.File("data/docs/report.txt", "now");
        t.File(".snapshots/12/snapshot/data/docs/report.txt", "older");
        File.WriteAllText(t.Combine(".snapshots/12/info.xml"), "<snapshot><num>12</num><date>2024-03-01 09:30:00</date><description>timeline</description></snapshot>");
        t.File(".snapshots/13/snapshot/data/other.txt", "not this one");
        t.File("data/.zfs/snapshot/autosnap_2024-05-02_00:00:01_daily/docs/report.txt", "zfs copy");

        var found = Snapshots.Find(file);
        Assert.Equal(2, found.Count);
        Assert.Equal("ZFS", found[0].Source);
        Assert.Equal(new DateTime(2024, 5, 2, 0, 0, 1), found[0].Taken);
        Assert.Equal("Snapper", found[1].Source);
        Assert.Equal("#12 timeline", found[1].Label);
        Assert.Equal(new DateTime(2024, 3, 1, 9, 30, 0, DateTimeKind.Utc).ToLocalTime(), found[1].Taken);
        Assert.Equal("older", File.ReadAllText(found[1].Path));
    }

    [Fact]
    public void Unchanged_copies_across_snapshots_show_once()
    {
        var a = new FileVersion("/a", new DateTime(2024, 1, 3), "ZFS", "c", false, 10, new DateTime(2023, 1, 1));
        var b = a with { Path = "/b", Taken = new DateTime(2024, 1, 2) };
        var c = a with { Path = "/c", Taken = new DateTime(2024, 1, 1), Size = 9 };
        Assert.Equal(new[] { "/a", "/c" }, Snapshots.Distinct(new[] { c, b, a }).Select(v => v.Path));
    }

    [Theory]
    [InlineData("2024-01-02_03-04-05", 2024, 1, 2, 3, 4, 5)]
    [InlineData("zfs-auto-snap_daily-2023-12-31-0000", 2023, 12, 31, 0, 0, 0)]
    [InlineData("20240615", 2024, 6, 15, 0, 0, 0)]
    public void Dates_in_snapshot_names(string name, int y, int mo, int d, int h, int mi, int s)
        => Assert.Equal(new DateTime(y, mo, d, h, mi, s), Snapshots.NameDate(name));

    [Fact]
    public void Vssadmin_output_is_read()
    {
        string text = "vssadmin 1.1\r\nContents of shadow copy set ID: {a}\r\n   Contained 1 shadow copies at creation time: 1/2/2024 3:04:05 AM\r\n" +
                      "      Shadow Copy ID: {b}\r\n         Original Volume: (C:)\\\\?\\Volume{c}\\\r\n" +
                      "         Shadow Copy Volume: \\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy7\r\n";
        var (device, drive, _) = Assert.Single(Snapshots.ParseVssadmin(text));
        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7", device);
        Assert.Equal("C:", drive);
    }
}

public class SimilarPhotosTests
{
    private static void Picture(string path, int w, int h, int seed, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg, int quality = 90)
    {
        var rnd = new Random(seed);
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256)));
            for (int i = 0; i < 12; i++)
                c.DrawCircle(rnd.Next(w), rnd.Next(h), rnd.Next(w / 8, w / 3), new SKPaint { Color = new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256)) });
        }
        using var data = bmp.Encode(format, quality);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void Resized(string from, string to, int w, int h, int quality)
    {
        using var src = SKBitmap.Decode(from);
        using var small = src.Resize(new SKImageInfo(w, h), SKFilterQuality.High)!;
        using var data = small.Encode(SKEncodedImageFormat.Jpeg, quality);
        File.WriteAllBytes(to, data.ToArray());
    }

    [Fact]
    public void Resized_and_resaved_copies_group_with_the_original_and_the_largest_leads()
    {
        using var t = new TempDir();
        Picture(t.Combine("a.jpg"), 1200, 900, 1);
        Resized(t.Combine("a.jpg"), t.Combine("a_small.jpg"), 400, 300, 60);
        Picture(t.Combine("b.png"), 800, 600, 2, SKEncodedImageFormat.Png);
        Picture(t.Combine("c.jpg"), 800, 600, 3);

        var prints = SimilarPhotos.CollectPictures(t.Path, subfolders: true).Select(SimilarPhotos.Fingerprint).OfType<PhotoPrint>().ToList();
        Assert.Equal(4, prints.Count);
        var group = Assert.Single(SimilarPhotos.Group(prints, SimilarPhotos.Normal));
        Assert.Equal(new[] { "a.jpg", "a_small.jpg" }, group.Select(p => Path.GetFileName(p.Path)));
        Assert.Equal(1200, group[0].Width);
    }

    [Fact]
    public void Distance_counts_differing_bits()
    {
        Assert.Equal(0, SimilarPhotos.Distance(0xFF, 0xFF));
        Assert.Equal(64, SimilarPhotos.Distance(0, ulong.MaxValue));
        Assert.Null(SimilarPhotos.Fingerprint("/no/such/file.jpg"));
    }
}

public class PhotoPrivacyTests
{
    private static Dictionary<string, string> Fields(byte[] jpeg) => ExifReader.Read(jpeg)?.Fields.ToDictionary(f => f.Label, f => f.Value) ?? new();

    [Fact]
    public void Location_only_keeps_the_camera_and_the_picture()
    {
        byte[] photo = ExifReaderTests.Jpeg(ExifReaderTests.Camera(true).Build());
        Assert.True(Fields(photo).ContainsKey("Location"));
        byte[] clean = PhotoPrivacy.Strip(photo, StripMode.Location);
        var f = Fields(clean);
        Assert.False(f.ContainsKey("Location"));
        Assert.Equal("Canon EOS R6", f["Camera"]);
        Assert.Equal(photo.Length, clean.Length);              // blanked in place
        Assert.Equal(photo[^7..], clean[^7..]);                // the image data, untouched
        Assert.DoesNotContain("\"N\"", Encoding.ASCII.GetString(clean));
    }

    [Fact]
    public void All_details_go_but_the_turn_stays()
    {
        foreach (bool le in new[] { true, false })
        {
            byte[] photo = ExifReaderTests.Jpeg(ExifReaderTests.Camera(le).Build());
            byte[] clean = PhotoPrivacy.Strip(photo, StripMode.All);
            var d = ExifReader.Read(clean);
            Assert.NotNull(d);
            Assert.Equal(6, d!.Orientation);
            Assert.DoesNotContain(d.Fields, x => x.Label is "Camera" or "Location" or "Taken");
            Assert.DoesNotContain("Canon", Encoding.ASCII.GetString(clean));
            Assert.Equal(photo[^7..], clean[^7..]);
        }
    }

    [Fact]
    public void Png_text_and_exif_chunks_go_and_it_still_decodes()
    {
        using var bmp = new SKBitmap(20, 10);
        bmp.Erase(SKColors.Teal);
        var png = bmp.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        byte[] Chunk(string type, byte[] body)
        {
            var c = new List<byte>();
            c.AddRange(new[] { (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length });
            c.AddRange(Encoding.ASCII.GetBytes(type));
            c.AddRange(body);
            c.AddRange(new byte[4]);   // CRC isn't checked by the stripper
            return c.ToArray();
        }
        var withText = png[..33].Concat(Chunk("tEXt", Encoding.ASCII.GetBytes("Author\0Someone"))).Concat(Chunk("eXIf", new byte[] { 1, 2 })).Concat(png[33..]).ToArray();
        byte[] clean = PhotoPrivacy.Strip(withText, StripMode.All);
        Assert.Equal(png, clean);
    }

    [Fact]
    public void WebP_exif_and_xmp_go_and_the_flags_follow()
    {
        using var bmp = new SKBitmap(16, 16);
        bmp.Erase(SKColors.Orange);
        var plain = bmp.Encode(SKEncodedImageFormat.Webp, 80).ToArray();
        // Rebuild as an extended WebP: VP8X (EXIF + XMP flags), the image chunk, then EXIF and XMP.
        var image = plain[12..];
        byte[] Chunk(string type, byte[] body) => Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes(body.Length)).Concat(body).Concat(body.Length % 2 == 1 ? new byte[1] : Array.Empty<byte>()).ToArray();
        var vp8x = new byte[10]; vp8x[0] = 0x08 | 0x04; vp8x[4] = 15; vp8x[7] = 15;
        var body = Chunk("VP8X", vp8x).Concat(image).Concat(Chunk("EXIF", new byte[] { 1, 2, 3 })).Concat(Chunk("XMP ", Encoding.ASCII.GetBytes("<x/>"))).ToArray();
        var webp = Encoding.ASCII.GetBytes("RIFF").Concat(BitConverter.GetBytes(body.Length + 4)).Concat(Encoding.ASCII.GetBytes("WEBP")).Concat(body).ToArray();

        byte[] clean = PhotoPrivacy.Strip(webp, StripMode.Location);
        string text = Encoding.ASCII.GetString(clean);
        Assert.DoesNotContain("EXIF", text);
        Assert.DoesNotContain("XMP ", text);
        Assert.Equal(0, clean[20] & 0x0C);
        Assert.Equal(clean.Length - 8, BitConverter.ToInt32(clean, 4));
        using var decoded = SKBitmap.Decode(clean);
        Assert.Equal(16, decoded.Width);
    }

    [Fact]
    public void Other_files_are_refused_and_names_never_clash()
    {
        Assert.Throws<InvalidDataException>(() => PhotoPrivacy.Strip(new byte[] { 1, 2, 3, 4 }, StripMode.All));
        var taken = new HashSet<string> { Path.Combine("/p", "a_clean.jpg") };
        Assert.Equal(Path.Combine("/p", "a_clean (2).jpg"), PhotoPrivacy.CleanName("/p/a.jpg", "/p", taken.Contains));
    }
}

public class PdfToolsTests
{
    [Fact]
    public void Ranges_are_read()
    {
        Assert.Equal(new[] { 1, 2, 3, 5, 8, 9, 10 }, PdfTools.ParseRanges("1-3, 5, 8-", 10));
        Assert.Equal(new[] { 1, 2 }, PdfTools.ParseRanges("-2", 10));
        Assert.Equal(Enumerable.Range(1, 4), PdfTools.ParseRanges("", 4));
        Assert.Throws<FormatException>(() => PdfTools.ParseRanges("11", 10));
        Assert.Throws<FormatException>(() => PdfTools.ParseRanges("a-b", 10));
    }

    [Fact]
    public void Merge_split_extract_and_turn()
    {
        using var t = new TempDir();
        string a = t.Combine("a.pdf"), b = t.Combine("b.pdf");
        PdfTools.MakeBlank(a, 3);
        PdfTools.MakeBlank(b, 2);

        Assert.Equal(5, PdfTools.Merge(new[] { a, b }, t.Combine("m.pdf")));
        Assert.Equal(5, PdfTools.PageCount(t.Combine("m.pdf")));

        var parts = PdfTools.Split(t.Combine("m.pdf"), 2, t.Dir("parts"));
        Assert.Equal(new[] { "m pages 1-2.pdf", "m pages 3-4.pdf", "m page 5.pdf" }, parts.Select(Path.GetFileName));

        Assert.Equal(2, PdfTools.Extract(t.Combine("m.pdf"), new[] { 5, 1 }, t.Combine("x.pdf")));

        PdfTools.Rotate(a, new[] { 2 }, 90, t.Combine("r.pdf"));
        Assert.Equal(new[] { 0, 90, 0 }, PdfTools.Rotations(t.Combine("r.pdf")));
        Assert.Throws<IOException>(() => PdfTools.Merge(new[] { a }, t.Combine("m.pdf")));   // never replaces
        Assert.Equal(Path.Combine(t.Path, "m (2).pdf"), PdfTools.Unique(t.Combine("m.pdf"), File.Exists));
    }

    [Fact]
    public void Not_a_pdf_says_so()
    {
        using var t = new TempDir();
        string f = t.File("fake.pdf", "hello");
        Assert.Throws<IOException>(() => PdfTools.PageCount(f));
    }
}

public class ArchiveExtrasTests
{
    private static string Sources(TempDir t)
    {
        t.File("src/top/a.txt", "alpha");
        t.File("src/top/sub/b.txt", new string('b', 5000));
        return t.Combine("src/top");
    }

    [Fact]
    public async Task Tar_zst_is_written_and_reads_back()
    {
        using var t = new TempDir();
        string src = Sources(t);
        var r = await ArchiveCreator.CreateAsync(t.Path, "out.tar.zst", new[] { src }, ArchiveFormat.TarZst);
        Assert.EndsWith(".tar.zst", r.ArchivePath);
        var entries = new ArchiveFileProvider().ListEntries(r.ArchivePath);
        Assert.Contains(entries, e => e.Key == "top/sub/b.txt" && e.Size == 5000);
        Assert.True(ArchivePath.IsArchiveFile("x.tar.zst"));
        using (var s = new ArchiveFileProvider().OpenRead(ArchivePath.RootUriFor(r.ArchivePath) + "top/a.txt"))
            Assert.Equal("alpha", new StreamReader(s).ReadToEnd());
        Assert.Empty(ArchiveTester.Test(r.ArchivePath).Problems);
    }

    [Fact]
    public async Task Tar_xz_when_xz_is_installed()
    {
        if (ExternalCompressor.Xz == null) return;
        using var t = new TempDir();
        var r = await ArchiveCreator.CreateAsync(t.Path, "out.tar.xz", new[] { Sources(t) }, ArchiveFormat.TarXz);
        Assert.Equal(2, new ArchiveFileProvider().ListEntries(r.ArchivePath).Count(e => !e.IsDirectory));
        Assert.Empty(ArchiveTester.Test(r.ArchivePath).Problems);
    }

    [Fact]
    public async Task Seven_zip_when_7z_is_installed()
    {
        if (SevenZipTool.Path == null) return;
        using var t = new TempDir();
        var r = await ArchiveCreator.CreateAsync(t.Path, "out.7z", new[] { Sources(t) }, ArchiveFormat.SevenZip, password: "pw");
        Assert.True(File.Exists(r.ArchivePath));
    }

    [Fact]
    public void Formats_and_names()
    {
        Assert.Equal(ArchiveFormat.TarZst, ArchiveCreator.FormatFromName("a.tzst"));
        Assert.Equal(ArchiveFormat.SevenZip, ArchiveCreator.FormatFromName("a.7z"));
        Assert.Equal("photos.tar.xz", ArchiveCreator.WithExtension("photos.zip", ArchiveFormat.TarXz));
        Assert.Equal("a_1.tar.zst", ArchiveCreator.UniqueName("a.tar.zst", n => n == "a.tar.zst"));
        var args = SevenZipTool.BuildArgs("/o.7z", new[] { "x" }, CompressionLevel.SmallestSize, "pw");
        Assert.Contains("-mx=9", args);
        Assert.Contains("-ppw", args);
        Assert.Contains("-mhe=on", args);
        Assert.Equal("x", args[^1]);
    }

    [Fact]
    public void A_damaged_zip_is_caught()
    {
        using var t = new TempDir();
        string zip = t.Combine("a.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("data.txt", CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(Encoding.ASCII.GetBytes(new string('x', 2000)));
        }
        Assert.Empty(ArchiveTester.Test(zip).Problems);
        var bytes = File.ReadAllBytes(zip);
        int at = Encoding.ASCII.GetString(bytes).IndexOf("xxxx", StringComparison.Ordinal) + 100;
        bytes[at] = (byte)'y';
        File.WriteAllBytes(zip, bytes);
        var problem = Assert.Single(ArchiveTester.Test(zip).Problems);
        Assert.Equal("data.txt", problem.Entry);
    }

    [Fact]
    public void Crc32_matches_the_standard_check_value()
    {
        var c = new ArchiveTester.Crc32();
        c.Append("123456789"u8);
        Assert.Equal(0xCBF43926u, c.Value);
    }

    [Fact]
    public void Extract_here_makes_a_folder_only_when_needed()
    {
        Assert.Equal(new[] { "top" }, ExtractHere.TopLevel(new[] { "top/", "top/a.txt", "./top/b/c" }));
        Assert.Equal(new[] { "a", "b.txt" }, ExtractHere.TopLevel(new[] { "a/x", "b.txt" }));
        Assert.Equal("photos", ExtractHere.BaseName("photos.tar.gz"));
        Assert.Equal((false, "/d"), ExtractHere.Plan("x.zip", new[] { "one" }, "/d", _ => false));
        var taken = new HashSet<string> { Path.Combine("/d", "x") };
        Assert.Equal((true, Path.Combine("/d", "x (2)")), ExtractHere.Plan("x.zip", new[] { "a", "b" }, "/d", taken.Contains));
    }
}

public class ElevationTests
{
    private static int RunScript(ElevatedAction action, params string[] paths)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash") { RedirectStandardError = true };
        foreach (var a in Elevation.LinuxArgs(action, paths).Skip(1)) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    [Fact]
    public void The_shell_scripts_do_their_job_without_ever_replacing()
    {
        if (!File.Exists("/bin/bash") || OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        string odd = t.File("src/it's \"odd\" $HOME.txt", "x");
        string dest = t.Dir("dest");
        Assert.Equal(0, RunScript(ElevatedAction.Copy, odd, dest));
        Assert.True(File.Exists(Path.Combine(dest, Path.GetFileName(odd))));
        File.WriteAllText(odd, "changed");
        RunScript(ElevatedAction.Copy, odd, dest);
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, Path.GetFileName(odd))));   // -n: not replaced

        string renamed = Path.Combine(dest, "new name.txt");
        Assert.Equal(0, RunScript(ElevatedAction.Rename, Path.Combine(dest, Path.GetFileName(odd)), renamed));
        Assert.True(File.Exists(renamed));
        string other = t.File("dest/taken.txt", "keep");
        Assert.NotEqual(0, RunScript(ElevatedAction.Rename, renamed, other));
        Assert.Equal("keep", File.ReadAllText(other));

        Assert.Equal(0, RunScript(ElevatedAction.NewFolder, Path.Combine(dest, "made here")));
        Assert.True(Directory.Exists(Path.Combine(dest, "made here")));

        string content = t.File("new.txt", "new contents");
        Assert.Equal(0, RunScript(ElevatedAction.WriteFile, content, other));
        Assert.Equal("new contents", File.ReadAllText(other));

        Assert.Equal(0, RunScript(ElevatedAction.Move, renamed, t.Dir("moved")));
        Assert.True(File.Exists(Path.Combine(t.Combine("moved"), "new name.txt")));

        Assert.Equal(0, RunScript(ElevatedAction.Delete, dest));
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void Quoting_for_the_mac_and_windows()
    {
        Assert.Equal("'it'\\''s'", Elevation.ShellQuote("it's"));
        string mac = Elevation.MacScript(ElevatedAction.Delete, new[] { "/tmp/a \"b\"" });
        Assert.StartsWith("do shell script \"", mac);
        Assert.Contains("\\\"b\\\"", mac);
        Assert.EndsWith("with administrator privileges", mac);
        string ps = Elevation.PowerShellScript(ElevatedAction.Delete, new[] { @"C:\it's" });
        Assert.Contains(@"Remove-Item -LiteralPath 'C:\it''s' -Recurse -Force", ps);
        Assert.Equal("''''", Elevation.PsQuote("'"));
    }
}

public class CloudExtrasTests
{
    [Fact]
    public void Requests_name_the_remote_and_path()
    {
        var link = CloudExtras.LinkRequest("cloud://gdrive/Photos/a.jpg");
        Assert.Equal("gdrive:", link["fs"]);
        Assert.Equal("Photos/a.jpg", link["remote"]);
        var mount = CloudExtras.MountRequest("gdrive", "/home/me/DORI Mounts/gdrive");
        Assert.Equal("gdrive:", mount["fs"]);
        Assert.Equal("writes", ((Dictionary<string, object>)mount["vfsOpt"])["CacheMode"]);
    }

    [Fact]
    public void Mount_points()
    {
        if (OperatingSystem.IsWindows())
            Assert.Equal("Y:", CloudExtras.MountPointFor("x", d => d == "Z:"));
        else
            Assert.EndsWith(Path.Combine("DORI Mounts", "gdrive"), CloudExtras.MountPointFor("gdrive"));
    }

    [Fact]
    public void Mount_lists_are_read()
    {
        using var doc = JsonDocument.Parse("{\"mountPoints\":[{\"Fs\":\"gdrive:\",\"MountPoint\":\"/m/g\"},{\"Fs\":\"x:\"}]}");
        Assert.Equal(new[] { ("gdrive:", "/m/g") }, CloudExtras.ParseMounts(doc.RootElement));
    }
}

public class SettingsTransferTests
{
    [Fact]
    public void Export_inspect_and_import_with_a_backup()
    {
        using var t = new TempDir();
        string settings = t.Dir("settings"), program = t.Dir("program");
        File.WriteAllText(Path.Combine(settings, "Configuration.xml"), "<Settings><Theme>Nord</Theme></Settings>");
        File.WriteAllText(Path.Combine(settings, "Labels.xml"), "<Labels/>");
        File.WriteAllText(Path.Combine(program, "BOOKMARKS.MD"), "/home");
        t.File("settings/Catalogues/a.dcat", "x");

        string zip = t.Combine("export.zip");
        Assert.Equal(4, SettingsTransfer.Export(zip, settings, program, catalogues: true));
        Assert.Null(SettingsTransfer.Inspect(zip).Problem);

        string target = t.Dir("other"), otherProgram = t.Dir("otherprogram");
        File.WriteAllText(Path.Combine(target, "Configuration.xml"), "<Settings><Theme>Light</Theme></Settings>");
        string backup = SettingsTransfer.Import(zip, target, otherProgram);
        Assert.Contains("Nord", File.ReadAllText(Path.Combine(target, "Configuration.xml")));
        Assert.Equal("/home", File.ReadAllText(Path.Combine(otherProgram, "BOOKMARKS.MD")));
        Assert.True(File.Exists(Path.Combine(target, "Catalogues", "a.dcat")));
        using var b = ZipFile.OpenRead(backup);
        using var r = new StreamReader(b.GetEntry("settings/Configuration.xml")!.Open());
        Assert.Contains("Light", r.ReadToEnd());
    }

    [Fact]
    public void Other_zips_are_refused()
    {
        using var t = new TempDir();
        string zip = t.Combine("x.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntry("readme.txt");
        Assert.NotNull(SettingsTransfer.Inspect(zip).Problem);
        Assert.Throws<InvalidDataException>(() => SettingsTransfer.Import(zip, t.Dir("s"), t.Dir("p")));
    }

    [Fact]
    public void Moving_leaves_the_old_config_out_of_the_way()
    {
        using var t = new TempDir();
        string from = t.Dir("a"), to = t.Dir("b");
        File.WriteAllText(Path.Combine(from, "Configuration.xml"), "<Settings/>");
        File.WriteAllText(Path.Combine(from, "Rules.xml"), "<Rules/>");
        SettingsTransfer.Move(from, to);
        Assert.True(File.Exists(Path.Combine(to, "Configuration.xml")));
        Assert.True(File.Exists(Path.Combine(to, "Rules.xml")));
        Assert.False(File.Exists(Path.Combine(from, "Configuration.xml")));
        Assert.True(File.Exists(Path.Combine(from, "Configuration.xml.moved")));
    }
}

public class LocalizationTests
{
    [Fact]
    public void Phrases_translate_and_others_stay()
    {
        try
        {
            Localization.Use("de");
            Assert.Equal("Kopieren", Localization.T("Copy"));
            Assert.Equal("Suchen…", Localization.T("Find…"));
            Assert.Equal("photo.jpg", Localization.T("photo.jpg"));
            Localization.Use("fr");
            Assert.Equal("Fermer", Localization.T("Close"));
            Localization.Use("xx");
            Assert.Equal("en", Localization.Current);
            Assert.Equal("Copy", Localization.T("Copy"));
        }
        finally { Localization.Use("en"); }
    }

    [Fact]
    public void Every_phrase_has_every_language_once()
    {
        Assert.All(Translations.Table, row =>
        {
            Assert.Equal(Localization.Languages.Length, row.Length);
            Assert.All(row, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        });
        var dupes = Translations.Table.GroupBy(r => r[0]).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void The_right_click_menu_is_covered()
    {
        string axaml = File.ReadAllText(Path.Combine(RepoRoot(), "TAIDataGrid.axaml"));
        var headers = System.Text.RegularExpressions.Regex.Matches(axaml, "Header=\"([^\"]+)\"").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct();
        var known = Translations.Table.Select(r => r[0]).ToHashSet();
        Assert.All(headers, h => Assert.Contains(h, known));
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "TAIDataGrid.axaml"))) return d.FullName;
        throw new DirectoryNotFoundException();
    }
}
