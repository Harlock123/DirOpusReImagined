using System.Text;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace DirOpusReImagined.Tests;

public class RenameTokenTests
{
    private static readonly FileFacts Facts = new(
        new DateTime(2024, 2, 3, 4, 5, 6), new DateTime(2023, 7, 4, 9, 15, 30),
        new Dictionary<string, string> { ["Artist"] = "AC/DC", ["Album"] = "Back in Black", ["Track"] = "3/10", ["Title"] = "Hells: Bells" });

    private static string Name(string basename, string original = "IMG_1.jpg", FileFacts? facts = null, string folder = "Holiday")
        => RenameEngine.ComputeNewName(original, false, 1, RenameOptions.Default with { Basename = basename }, out _, facts ?? Facts, folder);

    [Fact] public void Extension_and_folder() => Assert.Equal("Holiday-jpg.jpg", Name("%FOLDER%-%EXT%"));
    [Fact] public void Taken_date_with_a_format() => Assert.Equal("2023-07-04_0915.jpg", Name("%TAKEN:yyyy-MM-dd_HHmm%"));
    [Fact] public void Date_defaults_to_year_month_day() => Assert.Equal("2024-02-03.jpg", Name("%DATE%"));

    [Fact]
    public void Taken_falls_back_to_the_file_date()
        => Assert.Equal("2024-02-03.jpg", Name("%TAKEN%", facts: Facts with { Taken = null }));

    [Fact]
    public void Tags_are_made_safe_for_names_and_tracks_get_two_digits()
        => Assert.Equal("03 AC-DC - Hells_ Bells.mp3", Name("%TRACK% %ARTIST% - %TITLE%", "x.mp3"));

    [Fact] public void Missing_tags_are_empty() => Assert.Equal("[].jpg", Name("[%GENRE%]"));
    [Fact] public void Unknown_tokens_are_left_as_typed() => Assert.Equal("100%NOPE%.jpg", Name("100%NOPE%"));
    [Fact] public void Tokens_are_case_insensitive() => Assert.Equal("Holiday.jpg", Name("%folder%"));

    [Fact]
    public void Only_dates_and_tags_need_the_file_read()
    {
        Assert.False(RenameEngine.NeedsFacts(RenameOptions.Default with { Basename = "%NAME%_%ORD%_%EXT%_%FOLDER%" }));
        Assert.True(RenameEngine.NeedsFacts(RenameOptions.Default with { Prefix = "%TAKEN:yyyy%" }));
        Assert.True(RenameEngine.NeedsFacts(RenameOptions.Default with { Suffix = " %artist%" }));
    }

    [Fact]
    public void The_plan_reads_facts_only_when_needed()
    {
        int reads = 0;
        FileFacts Read(string _) { reads++; return Facts; }
        var sel = new List<(string, bool)> { ("a.jpg", false), ("b.jpg", false) };
        RenameEngine.BuildPlan(sel, RenameOptions.Default, Array.Empty<string>(), false, Read, "F");
        Assert.Equal(0, reads);
        var plan = RenameEngine.BuildPlan(sel, RenameOptions.Default with { Basename = "%TAKEN% %ORD%" }, Array.Empty<string>(), false, Read, "F");
        Assert.Equal(2, reads);
        Assert.Equal("2023-07-04 0001.jpg", plan[0].NewName);
    }

    [Fact]
    public void Folder_names_come_from_any_kind_of_panel_path()
    {
        Assert.Equal("Photos", RenameFileInterface.FolderLeaf("/home/me/Photos/"));
        Assert.Equal("trip", RenameFileInterface.FolderLeaf("archive:///home/me/trip.zip!/"));
        Assert.Equal("day1", RenameFileInterface.FolderLeaf("archive:///home/me/trip.zip!/2024/day1"));
        Assert.Equal("backup", RenameFileInterface.FolderLeaf("cloud://drive/backup"));
    }
}

public class FileFactsTests
{
    /// <summary>A JPEG holding only an EXIF date taken (DateTimeOriginal), little-endian.</summary>
    private static byte[] JpegTaken(string exifDate)
    {
        var tiff = new List<byte>();
        void U16(int v) { tiff.Add((byte)v); tiff.Add((byte)(v >> 8)); }
        void U32(int v) { U16(v & 0xFFFF); U16(v >> 16); }
        tiff.AddRange("II*\0"u8.ToArray()); U32(8);
        U16(1); U16(0x8769); U16(4); U32(1); U32(26); U32(0);          // IFD0 → Exif IFD at 26
        U16(1); U16(0x9003); U16(2); U32(20); U32(44); U32(0);         // Exif IFD → date at 44
        tiff.AddRange(Encoding.ASCII.GetBytes(exifDate + "\0"));
        var app1 = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        int len = app1.Length + 2;
        return new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, (byte)(len >> 8), (byte)len }.Concat(app1)
            .Concat(new byte[] { 0xFF, 0xDA, 0, 2, 1, 2, 3, 0xFF, 0xD9 }).ToArray();
    }

    /// <summary>An MP3 that is only an ID3v2.3 tag (artist, album, title, track).</summary>
    private static byte[] Id3(params (string Id, string Text)[] frames)
    {
        var body = new List<byte>();
        foreach (var (id, text) in frames)
        {
            var t = new byte[] { 0 }.Concat(Encoding.Latin1.GetBytes(text)).ToArray();
            body.AddRange(Encoding.ASCII.GetBytes(id));
            body.AddRange(new[] { (byte)(t.Length >> 24), (byte)(t.Length >> 16), (byte)(t.Length >> 8), (byte)t.Length, (byte)0, (byte)0 });
            body.AddRange(t);
        }
        int n = body.Count;
        var head = new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0,
            (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };
        // A silent MPEG-1 layer III frame after the tag.
        var frame = new byte[417]; frame[0] = 0xFF; frame[1] = 0xFB; frame[2] = 0x90; frame[3] = 0x64;
        return head.Concat(body).Concat(frame).ToArray();
    }

    [Fact]
    public void Photos_give_their_date_taken()
    {
        using var t = new TempDir();
        string f = t.File("p.jpg");
        File.WriteAllBytes(f, JpegTaken("2023:07:04 09:15:30"));
        var facts = FileFacts.Read(f);
        Assert.Equal(new DateTime(2023, 7, 4, 9, 15, 30), facts.Taken);
        Assert.NotNull(facts.Modified);
    }

    [Fact]
    public void Music_gives_its_tags()
    {
        using var t = new TempDir();
        string f = t.File("s.mp3");
        File.WriteAllBytes(f, Id3(("TPE1", "Band"), ("TALB", "Record"), ("TIT2", "Song"), ("TRCK", "7/9")));
        var facts = FileFacts.Read(f);
        Assert.Equal("Band", facts.Tags["Artist"]);
        Assert.Equal("Record", facts.Tags["Album"]);
        Assert.Equal("Song", facts.Tags["Title"]);
        string renamed = RenameEngine.ComputeNewName("s.mp3", false, 1,
            RenameOptions.Default with { Basename = "%TRACK% - %TITLE%" }, out _, facts);
        Assert.Equal("07 - Song.mp3", renamed);
    }

    [Fact]
    public void Unreadable_files_give_nothing()
        => Assert.Same(FileFacts.None, FileFacts.Read("/no/such/file.jpg"));
}

public class GoToFolderTests
{
    [Fact]
    public void Each_folder_shows_once_under_its_first_group()
    {
        var merged = GoToFolder.Merge(new[]
        {
            new FolderPlace("Bookmark 1", "Docs", "/home/me/Docs"),
            new FolderPlace("Recent", "Docs", "/home/me/Docs/"),
            new FolderPlace("Recent", "tmp", "/tmp"),
            new FolderPlace("Tab", "", ""),
        });
        Assert.Equal(new[] { "Bookmark 1", "Recent" }, merged.Select(m => m.Group));
        Assert.Equal("/tmp", merged[1].Path);
    }

    [Theory]
    [InlineData("~", "/home/me")]
    [InlineData("~/Music", "/home/me/Music")]
    [InlineData("/etc", "/etc")]
    [InlineData("cloud://drive/x", "cloud://drive/x")]
    [InlineData("  \"/var/log\" ", "/var/log")]
    public void Typed_paths_are_recognised(string typed, string path)
        => Assert.Equal(path, GoToFolder.TypedPath(typed, "/home/me"));

    [Theory]
    [InlineData("docs")]
    [InlineData("")]
    [InlineData("src/app")]
    public void Words_are_searched_for_instead(string typed) => Assert.Null(GoToFolder.TypedPath(typed, "/home/me"));
}

public class CommandLineHistoryTests
{
    [Fact]
    public void Up_and_down_walk_through_earlier_commands()
    {
        var h = new CommandLineHistory();
        h.Add("ls");
        h.Add("git status");
        h.Add("ls");   // moves to the end
        Assert.Equal(new[] { "git status", "ls" }, h.Items);
        Assert.Equal("ls", h.Older());
        Assert.Equal("git status", h.Older());
        Assert.Null(h.Older());
        Assert.Equal("ls", h.Newer());
        Assert.Equal("", h.Newer());
        Assert.Null(h.Newer());
    }

    [Fact]
    public void History_keeps_the_newest_hundred()
    {
        var h = new CommandLineHistory();
        h.Load(Enumerable.Range(0, 150).Select(i => "c" + i));
        Assert.Equal(CommandLineHistory.MaxItems, h.Items.Count);
        Assert.Equal("c149", h.Items[^1]);
    }

    [Fact]
    public void Commands_run_through_the_shell()
    {
        var (file, args) = CommandLineHistory.ShellFor("echo hi | wc -c");
        if (OperatingSystem.IsWindows()) Assert.Contains("cmd", file, StringComparison.OrdinalIgnoreCase);
        else Assert.Equal(new[] { "-c", "echo hi | wc -c" }, args);
    }
}

public class FolderTreePathsTests
{
    [Fact]
    public void The_closest_root_holds_the_path()
    {
        var roots = new[] { "/", "/home/me" };
        Assert.Equal("/home/me", FolderTreePaths.RootOf("/home/me/a/b", roots));
        Assert.Equal("/", FolderTreePaths.RootOf("/etc", roots));
        Assert.Equal("/", FolderTreePaths.RootOf("/home/meadow", roots));   // not under /home/me
        Assert.Null(FolderTreePaths.RootOf("C:\\x", new[] { "/home/me" }));
    }

    [Fact]
    public void Steps_lead_down_from_the_root()
    {
        Assert.Equal(new[] { "a", "b" }, FolderTreePaths.StepsBelow("/home/me", "/home/me/a/b/"));
        Assert.Equal(new[] { "etc" }, FolderTreePaths.StepsBelow("/", "/etc"));
        Assert.Empty(FolderTreePaths.StepsBelow("/home/me", "/home/me"));
    }
}

public class SyncJobTests
{
    [Fact]
    public void Daily_jobs_are_due_once_their_time_has_passed()
    {
        var job = new SyncJob { From = "/a", To = "/b", DailyAt = new TimeSpan(2, 0, 0) };
        var now = new DateTime(2026, 3, 10, 9, 0, 0);
        Assert.True(job.IsDue(now));                                    // never run
        job.LastRun = new DateTime(2026, 3, 10, 2, 0, 5);
        Assert.False(job.IsDue(now));                                   // ran at 02:00 today
        job.LastRun = new DateTime(2026, 3, 9, 2, 0, 5);
        Assert.True(job.IsDue(now));                                    // missed today's
        Assert.False(job.IsDue(new DateTime(2026, 3, 10, 1, 0, 0)));   // before 02:00, yesterday's done
        Assert.False(new SyncJob { From = "/a", To = "/b" }.IsDue(now)); // by hand only
    }

    [Fact]
    public void Jobs_are_saved_and_read_back()
    {
        using var t = new TempDir();
        string file = t.Combine("SyncJobs.xml");
        var last = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        SyncJobStore.Save(new[]
        {
            new SyncJob { Name = "Photos", From = "/p", To = "cloud://d/p", Mode = SyncJobMode.Mirror, Content = true, DailyAt = new TimeSpan(23, 30, 0), LastRun = last },
            new SyncJob { Name = "Two", From = "/x", To = "/y", Mode = SyncJobMode.TwoWay },
        }, file);
        var back = SyncJobStore.Load(file);
        Assert.Equal(2, back.Count);
        Assert.Equal(SyncJobMode.Mirror, back[0].Mode);
        Assert.True(back[0].Content);
        Assert.Equal(new TimeSpan(23, 30, 0), back[0].DailyAt);
        Assert.Equal(last, back[0].LastRun!.Value.ToLocalTime());
        Assert.Null(back[1].DailyAt);
    }

    [Fact]
    public void Problems_are_named()
    {
        Assert.NotNull(new SyncJob { From = "", To = "/b" }.Problem());
        Assert.NotNull(new SyncJob { From = "/a/", To = "/a" }.Problem());
        Assert.Null(new SyncJob { From = "/a", To = "/b" }.Problem());
    }
}

public class PasswordZipTests
{
    private static string MakeZip(TempDir t, string password, string name = "secret.zip")
    {
        string zip = t.Combine(name);
        using var fs = File.Create(zip);
        using var w = new AesZipWriter(fs, password);
        w.AddFolder("docs/", DateTime.Now);
        using (var a = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("hello world ", 3000)))))
            w.AddFile("docs/hello.txt", a, new DateTime(2024, 5, 6, 7, 8, 10));
        using (var b = new MemoryStream(Enumerable.Range(0, 100_000).Select(i => (byte)(i * 31 % 253)).ToArray()))
            w.AddFile("data.bin", b, DateTime.Now);
        w.Finish();
        return zip;
    }

    [Fact]
    public void Sharpcompress_reads_what_is_written_with_the_password()
    {
        using var t = new TempDir();
        string zip = MakeZip(t, "pa55 wörd");
        using var archive = ArchiveFactory.OpenArchive(zip, new ReaderOptions { Password = "pa55 wörd" });
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Key!);
        Assert.True(entries["docs/hello.txt"].IsEncrypted);
        using (var r = new StreamReader(entries["docs/hello.txt"].OpenEntryStream()))
            Assert.Equal(string.Concat(Enumerable.Repeat("hello world ", 3000)), r.ReadToEnd());
        using var ms = new MemoryStream();
        using (var s = entries["data.bin"].OpenEntryStream()) s.CopyTo(ms);
        Assert.Equal(Enumerable.Range(0, 100_000).Select(i => (byte)(i * 31 % 253)), ms.ToArray());
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 10), entries["docs/hello.txt"].LastModifiedTime);
    }

    [Fact]
    public void Archives_know_when_they_need_a_password_and_which_one_works()
    {
        using var t = new TempDir();
        string zip = MakeZip(t, "right", "check.zip");
        Assert.True(ArchivePasswords.NeedsPassword(zip));
        Assert.False(ArchivePasswords.Try(zip, "wrong"));
        Assert.True(ArchivePasswords.Try(zip, "right"));
        ArchivePasswords.Set(zip, "right");
        Assert.False(ArchivePasswords.NeedsPassword(zip));
        Assert.NotNull(ArchiveEditor.WhyReadOnly(zip));   // read, not changed

        // Browsing and reading through the archive provider use the password.
        string root = ArchivePath.RootUriFor(zip);
        var provider = ProviderRegistry.For(root);
        Assert.Contains("data.bin", provider.EnumerateFiles(root).Select(e => e.Name));
        using var s = provider.OpenRead(new ArchivePath(zip, "docs/hello.txt").FullUri);
        Assert.StartsWith("hello world", new StreamReader(s).ReadToEnd());
    }

    [Fact]
    public void Plain_archives_need_no_password()
    {
        using var t = new TempDir();
        t.File("src/a.txt", "a");
        System.IO.Compression.ZipFile.CreateFromDirectory(t.Combine("src"), t.Combine("plain.zip"));
        Assert.False(ArchivePasswords.NeedsPassword(t.Combine("plain.zip")));
        Assert.Null(ArchiveEditor.WhyReadOnly(t.Combine("plain.zip")));
    }

    [Fact]
    public async Task The_zip_buttons_can_make_a_password_zip()
    {
        using var t = new TempDir();
        t.File("in/one.txt", "first");
        t.File("in/sub/two.txt", "second");
        string outDir = t.Dir("out");
        var result = await ArchiveCreator.CreateAsync(outDir, "locked.zip", new[] { t.Combine("in") }, ArchiveFormat.Zip, password: "pw");
        Assert.Equal(2, result.Files);
        Assert.False(ArchivePasswords.Try(result.ArchivePath, "nope"));
        using var archive = ArchiveFactory.OpenArchive(result.ArchivePath, new ReaderOptions { Password = "pw" });
        var two = archive.Entries.Single(e => e.Key == "in/sub/two.txt");
        Assert.Equal("second", new StreamReader(two.OpenEntryStream()).ReadToEnd());
        Assert.Contains(archive.Entries, e => e.IsDirectory && e.Key!.TrimEnd('/') == "in/sub");
    }

    [Fact]
    public async Task Only_zips_take_a_password()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            ArchiveCreator.CreateAsync("/tmp", "x.tar", new[] { "/tmp" }, ArchiveFormat.Tar, password: "pw"));

    [Fact]
    public void Dos_times_are_clamped_to_what_zips_can_hold()
    {
        var (_, date) = AesZipWriter.DosTime(new DateTime(1970, 1, 1));
        Assert.Equal((1980 - 1980) << 9 | 1 << 5 | 1, date);
    }
}

public class DiscImageTests
{
    private static string MakeIso(TempDir t)
    {
        var b = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true, VolumeIdentifier = "TEST" };
        b.AddFile("README.txt", Encoding.UTF8.GetBytes("read me"));
        b.AddFile("docs\\Long file name.md", Encoding.UTF8.GetBytes("# heading"));
        b.AddDirectory("empty");
        string iso = t.Combine("disc.iso");
        b.Build(iso);
        return iso;
    }

    [Fact]
    public void Images_list_their_files_and_folders()
    {
        using var t = new TempDir();
        var list = DiscImage.List(MakeIso(t));
        Assert.Contains(list, e => e.Key == "README.txt" && !e.IsDir && e.Size == 7);
        Assert.Contains(list, e => e.Key == "docs" && e.IsDir);
        Assert.Contains(list, e => e.Key == "docs/Long file name.md");
        Assert.Contains(list, e => e.Key == "empty" && e.IsDir);
    }

    [Fact]
    public void Images_browse_like_archives()
    {
        using var t = new TempDir();
        string iso = MakeIso(t);
        string root = ArchivePath.RootUriFor(iso);
        var provider = ProviderRegistry.For(root);
        Assert.Equal(new[] { "docs", "empty" }, provider.EnumerateDirectories(root).Select(e => e.Name).OrderBy(n => n));
        using var s = provider.OpenRead(new ArchivePath(iso, "docs/Long file name.md").FullUri);
        Assert.Equal("# heading", new StreamReader(s).ReadToEnd());
        Assert.NotNull(ArchiveEditor.WhyReadOnly(iso));
        Assert.True(ArchivePath.IsArchiveFile("ubuntu.ISO"));
    }

    [Theory]
    [InlineData("\\DOCS\\README.TXT;1", "DOCS/README.TXT")]
    [InlineData("\\MAKEFILE.;1", "MAKEFILE")]
    [InlineData("\\a\\b", "a/b")]
    public void Iso_names_lose_their_version_numbers(string full, string key) => Assert.Equal(key, DiscImage.Key(full));
}
