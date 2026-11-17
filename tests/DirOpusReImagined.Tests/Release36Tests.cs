using System.Text;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined.Tests;

public class ReplaceInFilesTests
{
    private static string Run(string text, ReplaceOptions o, out int count, List<LineChange>? changes = null)
        => ReplaceInFiles.Apply(text, ReplaceInFiles.MakeRegex(o), o, out count, changes);

    [Fact]
    public void Plain_text_is_replaced_exactly_as_typed()
    {
        string result = Run("cost $5 and $5\nnothing", new ReplaceOptions("$5", "$1.50"), out int n);
        Assert.Equal("cost $1.50 and $1.50\nnothing", result);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Regex_groups_can_be_used_in_the_replacement()
        => Assert.Equal("2024-03-01", Run("01/03/2024", new ReplaceOptions(@"(\d\d)/(\d\d)/(\d{4})", "$3-$2-$1", Regex: true), out _));

    [Fact]
    public void Whole_words_and_case()
    {
        var o = new ReplaceOptions("cat", "dog", WholeWord: true);
        Assert.Equal("dog concat dog", Run("cat concat Cat", o, out int n));
        Assert.Equal(2, n);
        Assert.Equal("dog concat Cat", Run("cat concat Cat", o with { MatchCase = true }, out _));
    }

    [Fact]
    public void Each_line_keeps_its_own_ending_and_changes_are_listed()
    {
        var changes = new List<LineChange>();
        string result = Run("a x\r\nb\nx c", new ReplaceOptions("x", "y"), out _, changes);
        Assert.Equal("a y\r\nb\ny c", result);
        Assert.Equal(new[] { 1, 3 }, changes.Select(c => c.Line));
        Assert.Equal("a x", changes[0].Before);
        Assert.Equal("a y", changes[0].After);
    }

    [Fact]
    public void A_match_never_spans_lines()
        => Assert.Equal("a\nb", Run("a\nb", new ReplaceOptions(@"a\nb", "X", Regex: true), out _));

    [Fact]
    public void Encodings_round_trip_byte_for_byte()
    {
        var samples = new[]
        {
            new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' },
            Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("héllo")).ToArray(),
            new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'\n' },     // Latin-1, not valid UTF-8
            Encoding.UTF8.GetBytes("naïve ✓"),
        };
        foreach (var bytes in samples)
        {
            var d = ReplaceInFiles.Decode(bytes)!;
            Assert.Equal(bytes, ReplaceInFiles.Encode(d, d.Text));
        }
        Assert.Null(ReplaceInFiles.Decode(new byte[] { 1, 0, 2 }));
    }

    [Fact]
    public void Latin1_files_keep_the_bytes_outside_the_matches()
    {
        using var t = new TempDir();
        string f = t.Combine("old.txt");
        File.WriteAllBytes(f, new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)' ', (byte)'x' });
        var o = new ReplaceOptions("x", "y");
        var done = ReplaceInFiles.ReplaceFile(f, ReplaceInFiles.MakeRegex(o), o, t.Combine("backups"), out int n);
        Assert.NotNull(done);
        Assert.Equal(1, n);
        Assert.Equal(new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)' ', (byte)'y' }, File.ReadAllBytes(f));
        Assert.Equal((byte)'x', File.ReadAllBytes(done!.Backup)[^1]);
        Assert.True(ReplaceInFiles.Unchanged(done));
        File.WriteAllText(f, "changed again, and longer");
        Assert.False(ReplaceInFiles.Unchanged(done));
    }

    [Fact]
    public void Nothing_matching_writes_nothing()
    {
        using var t = new TempDir();
        string f = t.File("a.txt", "hello");
        var o = new ReplaceOptions("zzz", "y");
        Assert.Null(ReplaceInFiles.ReplaceFile(f, ReplaceInFiles.MakeRegex(o), o, t.Combine("b"), out _));
        Assert.False(Directory.Exists(t.Combine("b")));
    }

    [Fact]
    public void Collecting_files_follows_names_and_skips_hidden_folders()
    {
        using var t = new TempDir();
        t.File("a.txt"); t.File("b.md"); t.File("sub/c.txt"); t.File(".git/d.txt");
        var all = ReplaceInFiles.CollectFiles(new[] { t.Path }, "*.txt", subfolders: true);
        Assert.Equal(new[] { "a.txt", "c.txt" }, all.Select(Path.GetFileName).OrderBy(x => x));
        var top = ReplaceInFiles.CollectFiles(new[] { t.Path }, "", subfolders: false);
        Assert.Equal(new[] { "a.txt", "b.md" }, top.Select(Path.GetFileName).OrderBy(x => x));
    }

    [Fact]
    public void A_bad_pattern_is_refused()
        => Assert.ThrowsAny<ArgumentException>(() => ReplaceInFiles.MakeRegex(new ReplaceOptions("(", "x", Regex: true)));

    [Fact]
    public void Undo_describes_files()
    {
        var one = new ReplaceUndo("/f", new[] { new ReplacedFile("/f/a.txt", "/b", 1, DateTime.UtcNow) }, DateTime.Now);
        Assert.Equal("replace in \"a.txt\"", one.Description);
        var two = new ReplaceUndo("/f", new[] { new ReplacedFile("/f/a", "/b", 1, DateTime.UtcNow), new ReplacedFile("/f/b", "/c", 1, DateTime.UtcNow) }, DateTime.Now);
        Assert.Equal("replace in 2 files", two.Description);
    }
}

public class TrashBrowserTests
{
    [Fact]
    public void A_freedesktop_trash_lists_with_where_items_came_from()
    {
        using var t = new TempDir();
        t.File("Trash/files/report.txt", "12345");
        t.Dir("Trash/files/photos");
        t.File("Trash/info/report.txt.trashinfo", "[Trash Info]\nPath=/home/me/My%20Docs/report.txt\nDeletionDate=2024-05-06T07:08:09\n");
        t.File("Trash/info/photos.trashinfo", "[Trash Info]\nPath=pics/photos\nDeletionDate=2024-05-07T07:08:09\n");

        var list = TrashBrowser.ListFreedesktop(t.Combine("Trash"), "/mnt/usb");
        var report = list.Single(e => e.Name == "report.txt");
        Assert.Equal("/home/me/My Docs/report.txt", report.OriginalPath);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9), report.Deleted);
        Assert.Equal(5, report.Size);
        Assert.True(report.CanRestore);
        var photos = list.Single(e => e.Name == "photos");
        Assert.True(photos.IsDirectory);
        Assert.Equal(Path.Combine("/mnt/usb", "pics/photos"), photos.OriginalPath);
    }

    [Fact]
    public void An_item_without_a_record_is_listed_but_cannot_go_back()
    {
        using var t = new TempDir();
        t.File("Trash/files/stray.bin", "x");
        var e = Assert.Single(TrashBrowser.ListFreedesktop(t.Combine("Trash"), null));
        Assert.False(e.CanRestore);
        Assert.Equal("stray.bin", e.Name);
    }

    [Fact]
    public void A_recycle_bin_record_is_read()
    {
        using var t = new TempDir();
        string bin = t.Dir("S-1-5-21");
        string original = @"C:\Users\me\notes.txt";
        var rec = new List<byte>();
        rec.AddRange(BitConverter.GetBytes(2L));
        rec.AddRange(BitConverter.GetBytes(42L));
        rec.AddRange(BitConverter.GetBytes(new DateTime(2024, 1, 2, 3, 4, 5).ToFileTime()));
        rec.AddRange(BitConverter.GetBytes(original.Length + 1));
        rec.AddRange(Encoding.Unicode.GetBytes(original + "\0"));
        File.WriteAllBytes(Path.Combine(bin, "$IABC123.txt"), rec.ToArray());
        File.WriteAllText(Path.Combine(bin, "$RABC123.txt"), "hello");

        var e = Assert.Single(TrashBrowser.ListRecycleBin(bin));
        Assert.Equal(original, e.OriginalPath);
        Assert.Equal(42, e.Size);
        Assert.EndsWith("$RABC123.txt", e.TrashedPath);
    }

    [Fact]
    public void Deleting_for_good_removes_the_item_and_its_record()
    {
        using var t = new TempDir();
        t.File("Trash/files/dir/inner.txt", "x");
        t.File("Trash/info/dir.trashinfo", "[Trash Info]\nPath=/x/dir\nDeletionDate=2024-05-06T07:08:09\n");
        var e = Assert.Single(TrashBrowser.ListFreedesktop(t.Combine("Trash"), null));
        TrashBrowser.DeleteForever(e);
        Assert.False(Directory.Exists(t.Combine("Trash/files/dir")));
        Assert.False(File.Exists(t.Combine("Trash/info/dir.trashinfo")));
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void Folders_on_the_command_line_are_made_absolute_and_switches_skipped()
    {
        string cwd = OperatingSystem.IsWindows() ? @"C:\work" : "/work";
        var paths = SingleInstance.PathsFrom(new[] { "--scale", "1.5", "docs", "--new-window", "/scale=2" }, cwd);
        Assert.Equal(new[] { Path.GetFullPath("docs", cwd) }, paths);
    }

    [Fact]
    public void File_uris_are_turned_into_paths()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(new[] { "/home/me/My Docs" }, SingleInstance.PathsFrom(new[] { "file:///home/me/My%20Docs" }, "/"));
    }

    [Fact]
    public void The_pipe_name_is_the_same_each_time()
    {
        Assert.Equal(SingleInstance.PipeName(), SingleInstance.PipeName());
        Assert.StartsWith("dori-", SingleInstance.PipeName());
    }
}

public class DesktopIntegrationTests
{
    [Fact]
    public void The_desktop_entry_handles_folders()
    {
        string entry = DesktopIntegration.DesktopEntry("/opt/dori/DirOpusReImagined", "system-file-manager");
        Assert.Contains("Exec=/opt/dori/DirOpusReImagined %U\n", entry);
        Assert.Contains("MimeType=inode/directory;", entry);
        Assert.Contains("Icon=system-file-manager", entry);
    }

    [Fact]
    public void Program_paths_with_spaces_are_quoted_for_exec()
    {
        Assert.Equal("\"/home/me/My Apps/dori\"", DesktopIntegration.QuoteExec("/home/me/My Apps/dori"));
        Assert.Equal("\"/a b/\\\\$x\"", DesktopIntegration.QuoteExec("/a b/$x"));
    }

    [Fact]
    public void The_explorer_command_passes_the_folder()
        => Assert.Equal("\"C:\\DORI\\dori.exe\" \"%V\"", DesktopIntegration.WindowsCommand(@"C:\DORI\dori.exe"));
}

public class QuickConnectTests
{
    [Fact]
    public void A_full_sftp_address_fills_everything()
    {
        var d = QuickConnect.ParseAddress("sftp://me:secret@nas.local:2222/var/www", ServerKind.Ftp)!;
        Assert.Equal(ServerKind.Sftp, d.Kind);
        Assert.Equal("nas.local", d.Host);
        Assert.Equal(2222, d.Port);
        Assert.Equal("me", d.User);
        Assert.Equal("secret", d.Password);
        Assert.Equal("/var/www", d.Path);
        Assert.True(QuickConnect.NeedsAlias(d));
    }

    [Fact]
    public void A_windows_share_path_is_smb()
    {
        var d = QuickConnect.ParseAddress(@"\\nas\photos\2024", ServerKind.Sftp)!;
        Assert.Equal(ServerKind.Smb, d.Kind);
        Assert.Equal("nas", d.Host);
        Assert.Equal("photos/2024", d.Path);
        Assert.Equal(445, d.Port);
    }

    [Fact]
    public void A_bare_host_keeps_the_chosen_kind()
    {
        var d = QuickConnect.ParseAddress("files.example.com", ServerKind.Ftp)!;
        Assert.Equal(ServerKind.Ftp, d.Kind);
        Assert.Equal(21, d.Port);
        Assert.Null(QuickConnect.ParseAddress("  ", ServerKind.Ftp));
    }

    [Fact]
    public void Https_is_webdav_with_tls()
    {
        var d = QuickConnect.ParseAddress("https://cloud.example.com/remote.php/dav", ServerKind.Sftp)!;
        Assert.Equal(ServerKind.WebDav, d.Kind);
        Assert.True(d.Tls);
        Assert.Equal(443, d.Port);
        var (type, p) = QuickConnect.ToRclone(d);
        Assert.Equal("webdav", type);
        Assert.Equal("https://cloud.example.com/", p["url"]);
        Assert.Equal("cloud://webdav-cloud-example-com/remote.php/dav", QuickConnect.PanelPath("webdav-cloud-example-com", d.Path));
    }

    [Fact]
    public void Rclone_settings_leave_out_defaults_and_use_the_agent_without_a_password()
    {
        var (type, p) = QuickConnect.ToRclone(new ServerDetails(ServerKind.Sftp, "h", 22, "me", "", ""));
        Assert.Equal("sftp", type);
        Assert.False(p.ContainsKey("port"));
        Assert.Equal("true", p["key_use_agent"]);
        var (_, ftp) = QuickConnect.ToRclone(new ServerDetails(ServerKind.Ftp, "h", 2121, "u", "pw", "", Tls: true));
        Assert.Equal("2121", ftp["port"]);
        Assert.Equal("pw", ftp["pass"]);
        Assert.Equal("true", ftp["explicit_tls"]);
    }

    [Fact]
    public void Remote_names_are_safe_and_unique()
    {
        var d = new ServerDetails(ServerKind.Sftp, "my.server", 22, "me", "", "");
        Assert.Equal("sftp-me-my-server", QuickConnect.RemoteName(d, Array.Empty<string>()));
        Assert.Equal("sftp-me-my-server-2", QuickConnect.RemoteName(d, new[] { "sftp-me-my-server:" }));
        Assert.Equal("home_nas", QuickConnect.RemoteName(d, Array.Empty<string>(), "home_nas"));
    }

    [Fact]
    public void Alias_points_at_the_folder_from_the_top()
        => Assert.Equal("base:/var/www", QuickConnect.AliasParameters("base", "/var/www/")["remote"]);
}

public class DevicesTests
{
    [Fact]
    public void Mount_names_are_unescaped()
        => Assert.Equal("/run/media/me/My Stick\\x", Devices.UnescapeMount("/run/media/me/My\\040Stick\\134x"));

    [Fact]
    public void The_whole_disk_of_a_partition()
    {
        Assert.Equal("/dev/sdb", Devices.WholeDisk("/dev/sdb1"));
        Assert.Equal("/dev/mmcblk0", Devices.WholeDisk("/dev/mmcblk0p1"));
        Assert.Equal("/dev/nvme0n1", Devices.WholeDisk("/dev/nvme0n1p2"));
        Assert.Null(Devices.WholeDisk("/dev/mapper/x"));
    }

    [Fact]
    public void User_mounts_are_offered()
    {
        Assert.True(Devices.IsUserMount("/", DriveType.Fixed));
        Assert.True(Devices.IsUserMount("/run/media/me/USB/", DriveType.Removable));
        Assert.False(Devices.IsUserMount("/sys/fs/cgroup/", DriveType.Unknown));
        Assert.False(Devices.IsUserMount("/proc/sys/fs/binfmt_misc", DriveType.Network));
        Assert.True(Devices.IsUserMount("/mnt/nas/", DriveType.Network));
    }

    [Fact]
    public void Only_plugged_in_drives_can_be_ejected()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(Devices.CanEject("/run/media/me/USB/", DriveType.Fixed));
        Assert.True(Devices.CanEject("/media/usb/", DriveType.Fixed));
        Assert.False(Devices.CanEject("/", DriveType.Fixed));
        Assert.False(Devices.CanEject("/mnt/data/", DriveType.Fixed));
    }
}

public class UpdateCheckTests
{
    [Fact]
    public void Tags_are_read_as_versions()
    {
        Assert.Equal(new Version(0, 1, 36, 0), UpdateCheck.ParseTag("v0.1.36"));
        Assert.Equal(new Version(1, 2, 0, 0), UpdateCheck.ParseTag("1.2"));
        Assert.Equal(new Version(0, 2, 0, 0), UpdateCheck.ParseTag("v0.2.0-beta"));
        Assert.Null(UpdateCheck.ParseTag("latest"));
    }

    [Fact]
    public void Newer_compares_all_four_parts()
    {
        Assert.True(UpdateCheck.IsNewer(new Version(0, 1, 37, 0), new Version(0, 1, 36, 0)));
        Assert.False(UpdateCheck.IsNewer(new Version(0, 1, 36), new Version(0, 1, 36, 0)));
        Assert.False(UpdateCheck.IsNewer(new Version(0, 1, 9, 0), new Version(0, 1, 36, 0)));
    }

    [Fact]
    public void GitHubs_answer_is_read()
    {
        string json = "{\"tag_name\":\"v0.1.40\",\"name\":\"DORI 0.1.40\",\"html_url\":\"https://example/rel\",\"body\":\"Notes\",\"published_at\":\"2026-12-01T10:00:00Z\"}";
        var r = UpdateCheck.ParseRelease(json)!;
        Assert.Equal(new Version(0, 1, 40, 0), r.Version);
        Assert.Equal("https://example/rel", r.Url);
        Assert.Equal("Notes", r.Notes);
        Assert.NotNull(r.Published);
        Assert.Null(UpdateCheck.ParseRelease("{\"tag_name\":\"nightly\"}"));
    }
}

public class SystemTagsTests
{
    [Fact]
    public void Linux_tags_carry_the_colour_as_a_tag()
    {
        var label = new FileLabel(LabelColour.Red, new[] { "work", "to do" });
        Assert.Equal("Red,work,to do", SystemTags.ToLinux(label));
        var back = SystemTags.FromLinux("Red,work,to do")!;
        Assert.Equal(LabelColour.Red, back.Colour);
        Assert.Equal(new[] { "work", "to do" }, back.Tags);
        Assert.Equal(LabelColour.None, SystemTags.FromLinux("music,old")!.Colour);
        Assert.Null(SystemTags.FromLinux(""));
    }

    [Fact]
    public void Finder_tags_map_to_colours()
    {
        var mac = SystemTags.ToMac(new FileLabel(LabelColour.Grey, new[] { "work" }));
        Assert.Equal(new[] { "Gray\n1", "work\n0" }, mac);
        var back = SystemTags.FromMac(new[] { "Green\n2", "Important\n6", "plain" })!;
        Assert.Equal(LabelColour.Green, back.Colour);
        Assert.Equal(new[] { "Important", "plain" }, back.Tags);
    }

    [Fact]
    public void The_property_list_round_trips()
    {
        var items = new List<string> { "Red\n6", "a much longer tag name than fifteen\n0", "naïve ✓\n0" };
        for (int i = 0; i < 300; i++) items.Add("t" + i);
        var bytes = BinaryPlist.WriteStringArray(items);
        Assert.Equal("bplist00", Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Equal(items, BinaryPlist.ReadStringArray(bytes));
        Assert.Empty(BinaryPlist.ReadStringArray(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Labels_go_onto_a_real_file_where_the_drive_allows()
    {
        if (!SystemTags.Supported) return;
        using var t = new TempDir();
        string f = t.File("tagged.txt", "x");
        var label = new FileLabel(LabelColour.Blue, new[] { "project" });
        if (!SystemTags.Write(f, label)) return;   // a temp folder without extended attributes
        var back = SystemTags.Read(f)!;
        Assert.Equal(LabelColour.Blue, back.Colour);
        Assert.Equal(new[] { "project" }, back.Tags);
        Assert.True(SystemTags.Write(f, FileLabel.Empty));
        Assert.Null(SystemTags.Read(f));
    }

    [Fact]
    public void The_store_prefers_the_files_own_label_and_reports_what_it_set()
    {
        var store = new LabelStore(null);
        var onFile = new Dictionary<string, FileLabel> { ["/a/x"] = new FileLabel(LabelColour.Green, new[] { "from finder" }) };
        store.SystemReader = p => onFile.TryGetValue(p, out var l) ? l : null;
        Assert.Equal(LabelColour.Green, store.Get("/a/x").Colour);
        IReadOnlyCollection<string>? set = null;
        store.LabelsSet += p => set = p;
        store.SetColour(new[] { "/a/x" }, LabelColour.Red);
        Assert.Equal(new[] { "/a/x" }, set);
        // The new colour joins the tags that were on the file.
        Assert.Equal(new FileLabel(LabelColour.Red, new[] { "from finder" }).Display, store.GetStored("/a/x").Display);
        Assert.True(store.AnyLabels);
    }
}

public class MediaConvertTests
{
    private static readonly MediaPreset Mp3 = MediaConvert.Presets.First(p => p.Extension == "mp3");
    private static readonly MediaPreset Mp4 = MediaConvert.Presets.First(p => p.Extension == "mp4");
    private static readonly MediaPreset Cut = MediaConvert.Presets.First(p => p.Extension == "");

    [Fact]
    public void Times_are_read_and_written()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), MediaConvert.ParseTime("90"));
        Assert.Equal(TimeSpan.FromSeconds(90.5), MediaConvert.ParseTime("1:30.5"));
        Assert.Equal(TimeSpan.FromSeconds(3723), MediaConvert.ParseTime("01:02:03"));
        Assert.Null(MediaConvert.ParseTime("soon"));
        Assert.Null(MediaConvert.ParseTime(""));
        Assert.Equal("1:02:03.500", MediaConvert.FormatTime(TimeSpan.FromSeconds(3723.5)));
    }

    [Fact]
    public void Targets_never_replace_a_file()
    {
        var taken = new HashSet<string> { Path.Combine("/m", "song.mp3") };
        Assert.Equal(Path.Combine("/m", "song (2).mp3"), MediaConvert.TargetFor("/v/song.mp4", "/m", Mp3, taken.Contains));
        Assert.Equal(Path.Combine("/v", "song (cut).mp4"), MediaConvert.TargetFor("/v/song.mp4", "/v", Cut, _ => false));
        Assert.Equal(Path.Combine("/v", "song (2).mp4"), MediaConvert.TargetFor("/v/song.mp4", "/v", Mp4, _ => false));
    }

    [Fact]
    public void Arguments_seek_cut_and_scale()
    {
        var job = new MediaJob("/v/in.mov", "/v/out.mp4", Mp4, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25), 720);
        var args = MediaConvert.BuildArgs(job, "/v/.out.dori-part.mp4");
        int ss = args.IndexOf("-ss"), input = args.IndexOf("-i"), t = args.IndexOf("-t");
        Assert.True(ss >= 0 && ss < input && input < t);
        Assert.Equal("0:00:15.000", args[t + 1]);
        Assert.Contains("scale=-2:720", args);
        Assert.Contains("-n", args);          // never overwrite
        Assert.Equal("/v/.out.dori-part.mp4", args[^1]);
        Assert.DoesNotContain("-vf", MediaConvert.BuildArgs(new MediaJob("/a.mp4", "/a.mp3", Mp3, Height: 720), "x.mp3"));
    }

    [Fact]
    public void Ffmpeg_output_is_read()
    {
        Assert.Equal(TimeSpan.FromSeconds(192.45), MediaConvert.ParseDuration("  Duration: 00:03:12.45, start: 0.000000, bitrate: 128 kb/s"));
        Assert.Equal(TimeSpan.FromSeconds(1.5), MediaConvert.ParseProgress("out_time_us=1500000"));
        Assert.Equal(TimeSpan.FromSeconds(1.5), MediaConvert.ParseProgress("out_time_ms=1500000"));
        Assert.Equal(TimeSpan.FromSeconds(2), MediaConvert.ParseProgress("out_time=00:00:02.000000"));
        Assert.Null(MediaConvert.ParseProgress("progress=continue"));
    }

    [Fact]
    public void The_part_file_keeps_the_type_and_is_hidden()
        => Assert.Equal(Path.Combine("/m", ".song.dori-part.mp3"), MediaConvert.PartFile("/m/song.mp3"));

    [Fact]
    public void A_real_conversion_when_ffmpeg_is_installed()
    {
        if (!VideoFrames.Available) return;
        using var t = new TempDir();
        string wav = t.Combine("tone.wav");
        var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VideoFrames.FfmpegPath!,
            new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", wav })
            { RedirectStandardError = true, UseShellExecute = false })!;
        make.WaitForExit();
        if (!File.Exists(wav)) return;
        var flac = MediaConvert.Presets.First(p => p.Extension == "flac");
        string target = MediaConvert.TargetFor(wav, t.Path, flac);
        var reports = new List<(long, long)>();
        MediaConvert.Run(new MediaJob(wav, target, flac, End: TimeSpan.FromSeconds(1)), new SyncProgress<(long, long)>(reports.Add), CancellationToken.None);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(MediaConvert.PartFile(target)));
        Assert.Equal((1000L, 1000L), reports[^1]);
        Assert.Throws<IOException>(() => MediaConvert.Run(new MediaJob(wav, target, flac), null, CancellationToken.None));
    }

    [Fact]
    public void Video_presets_work_with_the_installed_ffmpeg()
    {
        if (!VideoFrames.Available) return;
        using var t = new TempDir();
        string clip = t.Combine("clip.mp4");
        var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VideoFrames.FfmpegPath!,
            new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-t", "2", "-pix_fmt", "yuv420p", clip })
            { RedirectStandardError = true, UseShellExecute = false })!;
        make.WaitForExit();
        if (!File.Exists(clip)) return;
        foreach (var preset in MediaConvert.Presets.Where(p => p.Extension is "mp4" or "gif" or "webm").GroupBy(p => p.Extension).Select(g => g.First()))
        {
            string target = MediaConvert.TargetFor(clip, t.Path, preset);
            try { MediaConvert.Run(new MediaJob(clip, target, preset, Height: 120), null, CancellationToken.None); }
            catch (IOException ex) when (ex.Message.Contains("Unknown encoder") || ex.Message.Contains("not found")) { continue; }   // a build without that encoder
            Assert.True(new FileInfo(target).Length > 0, preset.Name);
        }
        // A video with no sound can't become an MP3, and says so plainly.
        var mp3 = MediaConvert.Presets.First(p => p.Extension == "mp3");
        var ex2 = Assert.Throws<IOException>(() => MediaConvert.Run(new MediaJob(clip, t.Combine("x.mp3"), mp3), null, CancellationToken.None));
        Assert.Contains("no sound", ex2.Message);
    }
}

public class HexDocumentTests
{
    [Fact]
    public void Edits_show_through_reads_and_can_be_undone()
    {
        using var doc = HexDocument.FromBytes(new byte[] { 1, 2, 3, 4 });
        doc.Set(1, 0xAA);
        doc.Set(1, 0xBB);
        var buf = new byte[4];
        doc.Read(0, buf);
        Assert.Equal(new byte[] { 1, 0xBB, 3, 4 }, buf);
        Assert.Equal(1, doc.ChangeCount);
        Assert.Equal(1, doc.Undo());
        Assert.Equal(0xAA, doc.Get(1));
        doc.Undo();
        Assert.False(doc.IsChanged);
        doc.Set(2, 3);                      // the same as before: no change at all
        Assert.False(doc.IsChanged);
    }

    [Fact]
    public void Finding_works_both_ways_and_across_chunks()
    {
        var data = new byte[3 << 20];
        data[(1 << 20) - 1] = 0xDE; data[1 << 20] = 0xAD;        // straddles the first chunk boundary
        data[(2 << 20) + 5] = 0xDE; data[(2 << 20) + 6] = 0xAD;
        using var doc = HexDocument.FromBytes(data);
        var p = new byte[] { 0xDE, 0xAD };
        Assert.Equal((1 << 20) - 1, doc.Find(p, 0));
        Assert.Equal((2 << 20) + 5, doc.Find(p, 1 << 20));
        Assert.Equal((2 << 20) + 5, doc.Find(p, data.Length - 1, forward: false));
        Assert.Equal((1 << 20) - 1, doc.Find(p, (2 << 20) + 4, forward: false));
        Assert.Equal(-1, doc.Find(p, (2 << 20) + 6));
        doc.Set(7, 0xDE); doc.Set(8, 0xAD);
        Assert.Equal(7, doc.Find(p, 0));
    }

    [Fact]
    public void Patterns_and_offsets_are_read()
    {
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0x0F }, HexDocument.ParsePattern("de AD 0xf", asHex: true));
        Assert.Equal(new byte[] { 0xDE, 0xAD }, HexDocument.ParsePattern("DEAD", asHex: true));
        Assert.Null(HexDocument.ParsePattern("XYZ", asHex: true));
        Assert.Equal(Encoding.UTF8.GetBytes("Hi"), HexDocument.ParsePattern("Hi", asHex: false));
        Assert.Equal(0x1F40, HexDocument.ParseOffset("0x1F40", 100000));
        Assert.Equal(0x1F40, HexDocument.ParseOffset("1F40h", 100000));
        Assert.Equal(8000, HexDocument.ParseOffset("8000", 100000));
        Assert.Equal(0xBEEF, HexDocument.ParseOffset("beef", 100000));
        Assert.Equal(50000, HexDocument.ParseOffset("50%", 100000));
        Assert.Null(HexDocument.ParseOffset("zz", 100));
    }

    [Fact]
    public void Rows_are_laid_out_in_columns()
    {
        string row = HexDocument.FormatRow(0x10, Encoding.ASCII.GetBytes("Hello"), 16, 8);
        Assert.StartsWith("00000010  48 65 6C 6C 6F ", row);
        Assert.EndsWith("Hello", row);
        Assert.Equal(8, HexDocument.OffsetDigits(100));
        Assert.Equal(9, HexDocument.OffsetDigits(0x1_0000_0001));
    }

    [Fact]
    public void Saving_a_local_file_writes_only_the_changed_bytes()
    {
        using var t = new TempDir();
        string f = t.Combine("data.bin");
        File.WriteAllBytes(f, Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray());
        using (var doc = HexDocument.Open(f))
        {
            Assert.True(doc.IsLocal);
            Assert.Null(doc.ReadOnlyReason);
            Assert.Equal((byte)(150_000 % 256), doc.Get(150_000));   // a later page
            doc.Set(0, 0xFF); doc.Set(1, 0xFE); doc.Set(150_000, 0x00);
            Assert.Equal(2, doc.Runs().Count);
            doc.Save();
            Assert.False(doc.IsChanged);
            Assert.Equal(0xFE, doc.Get(1));
        }
        var bytes = File.ReadAllBytes(f);
        Assert.Equal(200_000, bytes.Length);
        Assert.Equal(new byte[] { 0xFF, 0xFE, 2 }, bytes[..3]);
        Assert.Equal(0, bytes[150_000]);
        Assert.Equal(unchecked((byte)150_001), bytes[150_001]);
    }

    [Fact]
    public void A_file_that_changed_size_is_not_written()
    {
        using var t = new TempDir();
        string f = t.File("a.bin", "abcdef");
        using var doc = HexDocument.Open(f);
        doc.Set(0, (byte)'X');
        File.AppendAllText(f, "more");
        Assert.Throws<IOException>(doc.Save);
    }

    [Fact]
    public void A_read_only_file_says_so()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        using var t = new TempDir();
        string f = t.File("ro.bin", "abc");
        File.SetUnixFileMode(f, UnixFileMode.UserRead);
        using var doc = HexDocument.Open(f);
        Assert.NotNull(doc.ReadOnlyReason);
        Assert.Throws<InvalidOperationException>(() => doc.Set(0, 1));
    }
}
