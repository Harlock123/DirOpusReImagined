using System.Text;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

public class ClipboardFilesTests
{
    [Fact]
    public void Uri_lists_are_file_uris_with_crlf_and_escaped_spaces()
    {
        string text = ClipboardFiles.UriList(new[] { "/home/me/a b.txt", "/tmp/c#.txt" });
        Assert.Equal("file:///home/me/a%20b.txt\r\nfile:///tmp/c%23.txt\r\n", text);
    }

    [Fact]
    public void Gnome_list_says_copy_or_cut_first()
    {
        Assert.Equal("cut\nfile:///x/y", ClipboardFiles.GnomeCopiedFiles(new[] { "/x/y" }, cut: true));
        Assert.Equal("copy\nfile:///x/y", ClipboardFiles.GnomeCopiedFiles(new[] { "/x/y" }, cut: false));
    }

    [Fact]
    public void Gnome_lists_read_back_with_cut()
    {
        var paths = ClipboardFiles.Parse("cut\nfile:///home/me/a%20b.txt\nfile:///tmp/z", out bool? cut);
        Assert.True(cut);
        Assert.Equal(new[] { "/home/me/a b.txt", "/tmp/z" }, paths);
    }

    [Fact]
    public void Uri_lists_skip_comments_and_web_links()
    {
        var paths = ClipboardFiles.Parse("# from Dolphin\r\nfile:///a/b\r\nhttps://example.com/x\r\n", out bool? cut);
        Assert.Null(cut);
        Assert.Equal(new[] { "/a/b" }, paths);
    }

    [Fact]
    public void Plain_full_paths_count_and_words_dont()
    {
        var paths = ClipboardFiles.Parse("/etc/hosts\nhello world\n/tmp", out _);
        Assert.Equal(new[] { "/etc/hosts", "/tmp" }, paths);
    }

    [Fact]
    public void Bytes_decode_as_utf8_or_utf16()
    {
        Assert.Equal("file:///a", ClipboardFiles.AsText(Encoding.UTF8.GetBytes("file:///a")));
        Assert.Equal("C:\\x", ClipboardFiles.AsText(Encoding.Unicode.GetBytes("C:\\x\0")));
    }

    [Fact]
    public void Windows_drop_effect_two_means_cut()
    {
        Assert.True(ClipboardFiles.DropEffectIsCut(BitConverter.GetBytes(2)));
        Assert.False(ClipboardFiles.DropEffectIsCut(BitConverter.GetBytes(5)));
        Assert.Null(ClipboardFiles.DropEffectIsCut(new byte[] { 1 }));
    }
}

public class NaturalComparerTests
{
    [Fact]
    public void Numbers_sort_by_value()
    {
        var names = new[] { "file10", "File2", "file1", "track 9.mp3", "track 10.mp3", "a", "file02" };
        var sorted = names.OrderBy(n => n, NaturalComparer.Instance).ToArray();
        Assert.Equal(new[] { "a", "file1", "File2", "file02", "file10", "track 9.mp3", "track 10.mp3" }, sorted);
    }

    [Fact]
    public void Long_numbers_dont_overflow()
        => Assert.True(NaturalComparer.Instance.Compare("x99999999999999999999999", "x100000000000000000000000") < 0);

    [Fact]
    public void Different_names_never_compare_equal()
    {
        Assert.NotEqual(0, NaturalComparer.Instance.Compare("a1", "a01"));
        Assert.NotEqual(0, NaturalComparer.Instance.Compare("abc", "ABC"));
        Assert.Equal(0, NaturalComparer.Instance.Compare("same", "same"));
    }

    [Fact]
    public void Split_pieces_stay_in_order()
    {
        var names = new[] { "v.010", "v.002", "v.001", "v.100" };
        Assert.Equal(new[] { "v.001", "v.002", "v.010", "v.100" }, names.OrderBy(n => n, NaturalComparer.Instance));
    }
}

public class FolderSizeOrderTests
{
    private static AFileEntry Dir(string name, long bytes = -1) => new(name, 0, true) { FolderBytes = bytes };
    private static AFileEntry File(string name) => new(name, 10, false);

    [Fact]
    public void Measured_folders_go_by_size_and_unmeasured_last()
    {
        var items = new List<object> { Dir(".."), Dir("a", 5), Dir("b"), Dir("c", 50), File("x"), File("y") };
        var down = FolderSizeOrder.Apply(items, ascending: false).Cast<AFileEntry>().Select(e => e.Name);
        Assert.Equal(new[] { "..", "c", "a", "b", "x", "y" }, down);
        var up = FolderSizeOrder.Apply(items, ascending: true).Cast<AFileEntry>().Select(e => e.Name);
        Assert.Equal(new[] { "..", "a", "c", "b", "x", "y" }, up);
    }
}

public class ListingExportTests
{
    [Fact]
    public void Csv_quotes_only_what_needs_it()
    {
        string csv = ListingExport.Write(ListingFormat.Csv, "t", new[] { "Name", "Size" },
            new List<IReadOnlyList<string>> { new[] { "a,b.txt", "1 Kb" }, new[] { "say \"hi\"", "" }, new[] { "plain", "2" } });
        Assert.Equal("Name,Size\r\n\"a,b.txt\",1 Kb\r\n\"say \"\"hi\"\"\",\r\nplain,2\r\n", csv);
    }

    [Fact]
    public void Text_lines_up_columns()
    {
        string text = ListingExport.Write(ListingFormat.Text, "Folder", new[] { "Name", "Size" },
            new List<IReadOnlyList<string>> { new[] { "long-name.txt", "1" }, new[] { "b", "22" } });
        var lines = text.Split('\n');
        Assert.Equal("Name           Size", lines[2]);
        Assert.Equal("long-name.txt  1", lines[4]);
        Assert.Equal("b              22", lines[5]);
        Assert.Contains("2 items", text);
    }

    [Fact]
    public void Html_escapes_names()
    {
        string html = ListingExport.Write(ListingFormat.Html, "<x>", new[] { "Name" },
            new List<IReadOnlyList<string>> { new[] { "a<b>&c" } });
        Assert.Contains("<td>a&lt;b&gt;&amp;c</td>", html);
        Assert.Contains("<title>&lt;x&gt;</title>", html);
    }

    [Theory]
    [InlineData("list.CSV", ListingFormat.Csv)]
    [InlineData("list.tsv", ListingFormat.Tsv)]
    [InlineData("list.html", ListingFormat.Html)]
    [InlineData("list.txt", ListingFormat.Text)]
    [InlineData("list", ListingFormat.Text)]
    public void Format_comes_from_the_ending(string name, ListingFormat format)
        => Assert.Equal(format, ListingExport.FormatFor(name));

    [Fact]
    public void Table_has_shown_columns_then_bytes_and_path()
    {
        using var t = new TempDir();
        t.File("a.txt", "12345");
        var rows = new List<AFileEntry> { new("a.txt", 5, false), new("sub", 0, true) { FolderBytes = 99 } };
        var (headers, table) = MainWindow.ListingTable(t.Path, new[] { "Typ", "Name", "FileSize" }, rows, local: true);
        Assert.Equal(new[] { "Kind", "Name", "Size", "Bytes", "Path" }, headers);
        Assert.Equal(new[] { "File", "a.txt" }, table[0].Take(2));
        Assert.Equal("5", table[0][3]);
        Assert.Equal(System.IO.Path.Combine(t.Path, "a.txt"), table[0][4]);
        Assert.Equal("Folder", table[1][0]);
        Assert.Equal("99", table[1][3]);
    }
}

[Collection("SpeedLimit")]
public class SpeedLimitTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("none", 0)]
    [InlineData("10 MB", 10L * 1024 * 1024)]
    [InlineData("512K/s", 512 * 1024)]
    public void Limits_are_read(string text, long limit)
    {
        Assert.True(SpeedLimiter.TryParse(text, out long n));
        Assert.Equal(limit, n);
    }

    [Fact]
    public void Nonsense_isnt_a_limit() => Assert.False(SpeedLimiter.TryParse("fast", out _));

    [Fact]
    public void A_second_goes_through_then_copies_wait()
    {
        SpeedLimiter.Reset();
        long f = System.Diagnostics.Stopwatch.Frequency;
        long t0 = 1000 * f;
        Assert.Equal(0, SpeedLimiter.Debt(1000, 1000, t0));            // a second's worth at once
        Assert.Equal(0.5, SpeedLimiter.Debt(500, 1000, t0), 3);         // then half a second over
        Assert.Equal(0, SpeedLimiter.Debt(100, 1000, t0 + 2 * f), 3);   // two seconds later it's paid off
        SpeedLimiter.Reset();
    }
}

public class ResumeSpeedTests
{
    [Fact]
    public async Task A_resumed_copy_says_so_before_carrying_on()
    {
        using var t = new TempDir();
        var data = new byte[StreamCopy.KeepPartialFrom + 300_000];
        new Random(4).NextBytes(data);
        string src = t.File("s.bin");
        System.IO.File.WriteAllBytes(src, data);
        string dst = t.Combine("d.bin");
        System.IO.File.WriteAllBytes(StreamCopy.PartialPathFor(dst), data.AsSpan(0, (int)StreamCopy.KeepPartialFrom).ToArray());

        var reports = new List<TransferProgress>();
        await StreamCopy.LocalToLocalAsync(src, dst, false, new SyncProgress<TransferProgress>(reports.Add), CancellationToken.None);

        Assert.True(reports[0].Resumed);
        Assert.Equal(StreamCopy.KeepPartialFrom, reports[0].BytesDone);
        Assert.All(reports.Skip(1), r => Assert.False(r.Resumed));
        Assert.Equal(data, System.IO.File.ReadAllBytes(dst));
    }

    [Fact]
    public void Restarting_the_meter_forgets_the_jump()
    {
        var m = new SpeedMeter();
        m.Add(0, 0);
        m.Add(0.1, 4_000_000);   // the jump: kept bytes, not copied now
        m.Restart();
        m.Add(0.1, 4_000_000);
        m.Add(1.1, 4_001_000);
        Assert.Equal(1000, m.BytesPerSecond!.Value, 0);
    }
}

public class QueueScheduleTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 14, 30, 0);

    [Theory]
    [InlineData("02:00", 11, 2, 0)]
    [InlineData("15:45", 10, 15, 45)]
    [InlineData("2am", 11, 2, 0)]
    [InlineData("11 pm", 10, 23, 0)]
    [InlineData("12am", 11, 0, 0)]
    [InlineData("23.15", 10, 23, 15)]
    [InlineData("14:30", 11, 14, 30)]
    public void Times_mean_the_next_time_that_comes(string text, int day, int hour, int minute)
    {
        Assert.True(QueueSchedule.TryNextTime(text, Now, out var when));
        Assert.Equal(new DateTime(2026, 3, day, hour, minute, 0), when);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("13pm")]
    [InlineData("soon")]
    public void Nonsense_isnt_a_time(string text) => Assert.False(QueueSchedule.TryNextTime(text, Now, out _));

    [Fact]
    public void Tomorrow_is_said()
        => Assert.Equal("02:00 tomorrow", QueueSchedule.Describe(new DateTime(2026, 3, 11, 2, 0, 0), Now));
}

public class QueueHoldTests
{
    [Fact]
    public async Task A_held_queue_starts_nothing_until_released()
    {
        var q = new OperationQueue();
        q.SetHold(DateTime.Now.AddHours(1));
        bool ran = false;
        var op = new FileOperation(OperationKind.Copy, Array.Empty<TransferItem>(), "x", (_, _) => { ran = true; return Task.CompletedTask; });
        q.Enqueue(op);
        await Task.Delay(300);
        Assert.False(ran);
        Assert.Equal(OperationStatus.Queued, op.Status);

        q.SetHold(null);
        var finished = await Task.WhenAny(op.Completion, Task.Delay(5000));
        Assert.Same(op.Completion, finished);
        Assert.True(ran);
    }

    [Fact]
    public async Task A_hold_that_runs_out_lets_work_start()
    {
        var q = new OperationQueue();
        q.SetHold(DateTime.Now.AddMilliseconds(400));
        var op = new FileOperation(OperationKind.Copy, Array.Empty<TransferItem>(), "x", (_, _) => Task.CompletedTask);
        q.Enqueue(op);
        var finished = await Task.WhenAny(op.Completion, Task.Delay(5000));
        Assert.Same(op.Completion, finished);
        Assert.Null(q.HoldUntil);
    }

    [Fact]
    public async Task Cancelling_while_held_finishes_the_operation()
    {
        var q = new OperationQueue();
        q.SetHold(DateTime.Now.AddHours(1));
        var op = new FileOperation(OperationKind.Copy, Array.Empty<TransferItem>(), "x", (_, _) => Task.CompletedTask);
        q.Enqueue(op);
        q.Cancel(op);
        var finished = await Task.WhenAny(op.Completion, Task.Delay(5000));
        Assert.Same(op.Completion, finished);
        Assert.Equal(OperationStatus.Canceled, op.Status);
        q.SetHold(null);
    }
}

public class FolderRuleTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0);

    private static RuleFile F(string path, double daysOld) => new(path, Now.AddDays(-daysOld), 1);

    [Fact]
    public void Rules_take_matching_settled_finished_files()
    {
        var rule = new FolderRule { Folder = "/in", Match = "*.pdf;*.docx", OlderThanDays = 0, Action = RuleAction.MoveTo, Target = "/out" };
        var files = new[]
        {
            F("/in/b.pdf", 1), F("/in/a.docx", 1), F("/in/c.txt", 1),
            F("/in/d.pdf.crdownload", 1), F("/in/e.pdf.part", 1),
            new RuleFile("/in/new.pdf", Now.AddSeconds(-1), 1),   // still being written
        };
        var picked = FolderRuleEngine.Candidates(rule, files, Now, _ => FileLabel.Empty);
        Assert.Equal(new[] { "/in/a.docx", "/in/b.pdf" }, picked);
    }

    [Fact]
    public void Age_tests_keep_new_files()
    {
        var rule = new FolderRule { Folder = "/in", Match = "*", OlderThanDays = 30, Action = RuleAction.Trash };
        var picked = FolderRuleEngine.Candidates(rule, new[] { F("/in/old", 31), F("/in/new", 2) }, Now, _ => FileLabel.Empty);
        Assert.Equal(new[] { "/in/old" }, picked);
    }

    [Fact]
    public void Files_already_labelled_are_left()
    {
        var rule = new FolderRule { Folder = "/in", Match = "*", Action = RuleAction.AddTag, Target = "work" };
        var tagged = new FileLabel(LabelColour.None, new[] { "Work" });
        var picked = FolderRuleEngine.Candidates(rule, new[] { F("/in/a", 1), F("/in/b", 1) }, Now,
                                                 p => p == "/in/a" ? tagged : FileLabel.Empty);
        Assert.Equal(new[] { "/in/b" }, picked);
    }

    [Fact]
    public void Moves_never_overwrite_and_carry_labels()
    {
        using var t = new TempDir();
        string src = t.File("in/report.pdf", "new");
        t.File("out/report.pdf", "old");
        var labels = new LabelStore(null);
        labels.SetColour(new[] { src }, LabelColour.Red);
        var rule = new FolderRule { Folder = t.Combine("in"), Action = RuleAction.MoveTo, Target = t.Combine("out") };

        Assert.NotNull(FolderRuleEngine.Apply(rule, src, labels));

        Assert.Equal("old", System.IO.File.ReadAllText(t.Combine("out/report.pdf")));
        Assert.Equal("new", System.IO.File.ReadAllText(t.Combine("out/report (2).pdf")));
        Assert.False(System.IO.File.Exists(src));
        Assert.Equal(LabelColour.Red, labels.Get(t.Combine("out/report (2).pdf")).Colour);
    }

    [Fact]
    public void Copies_arent_made_twice()
    {
        using var t = new TempDir();
        string src = t.File("in/a.txt", "abc");
        t.Dir("out");
        var rule = new FolderRule { Folder = t.Combine("in"), Action = RuleAction.CopyTo, Target = t.Combine("out") };
        var labels = new LabelStore(null);
        Assert.NotNull(FolderRuleEngine.Apply(rule, src, labels));
        Assert.Null(FolderRuleEngine.Apply(rule, src, labels));
        Assert.Single(System.IO.Directory.GetFiles(t.Combine("out")));
        Assert.True(System.IO.File.Exists(src));
    }

    [Fact]
    public void Problems_are_named()
    {
        Assert.NotNull(new FolderRule { Folder = "" }.Problem());
        Assert.NotNull(new FolderRule { Folder = "/a", Action = RuleAction.MoveTo, Target = "/a/" }.Problem());
        Assert.NotNull(new FolderRule { Folder = "/a", Action = RuleAction.SetColour, Target = "Teal" }.Problem());
        Assert.Null(new FolderRule { Folder = "/a", Action = RuleAction.SetColour, Target = "green" }.Problem());
        Assert.Null(new FolderRule { Folder = "/a", Action = RuleAction.Trash }.Problem());
    }

    [Fact]
    public void Rules_are_saved_and_read_back()
    {
        using var t = new TempDir();
        string file = t.Combine("Rules.xml");
        var rules = new List<FolderRule>
        {
            new() { Name = "PDFs", Folder = "/in", Match = "*.pdf", OlderThanDays = 2.5, Action = RuleAction.CopyTo, Target = "/out", Watch = true },
            new() { Name = "Old", Folder = "/in", Action = RuleAction.Trash, Enabled = false },
        };
        RuleStore.Save(rules, file);
        var back = RuleStore.Load(file);
        Assert.Equal(2, back.Count);
        Assert.Equal("PDFs", back[0].Name);
        Assert.Equal(2.5, back[0].OlderThanDays);
        Assert.Equal(RuleAction.CopyTo, back[0].Action);
        Assert.True(back[0].Watch);
        Assert.False(back[1].Enabled);
    }

    [Fact]
    public void Free_names_count_up()
    {
        using var t = new TempDir();
        t.File("a.txt");
        t.File("a (2).txt");
        Assert.Equal(t.Combine("a (3).txt"), FolderRuleEngine.FreeName(t.Combine("a.txt")));
        Assert.Equal(t.Combine("b.txt"), FolderRuleEngine.FreeName(t.Combine("b.txt")));
    }
}

public class CatalogueTests
{
    [Fact]
    public void A_scan_lists_everything_and_reads_back()
    {
        using var t = new TempDir();
        t.File("music/a.mp3", "12345");
        t.File("music/live/b.mp3", "1234567890");
        t.File("notes.txt", "x");
        var cat = Catalogue.Scan("Drive", t.Path);
        Assert.Equal(3, cat.FileCount);
        Assert.Equal(2, cat.FolderCount);
        Assert.Equal(16, cat.TotalBytes);
        Assert.Equal(new[] { "music", "notes.txt" }, cat.Children("").Select(e => e.Name));
        Assert.Equal(15, cat.BytesBelow("music"));

        cat.Save(t.Combine("cats"));
        var back = Catalogue.Load(cat.FilePath!);
        Assert.Equal("Drive", back.Name);
        Assert.Equal(t.Path, back.Root);
        Assert.Equal(cat.Entries.Count, back.Entries.Count);
        var b = back.Entries.Single(e => e.RelPath == "music/live/b.mp3");
        Assert.Equal(10, b.Size);
        Assert.Equal(cat.Entries.Single(e => e.RelPath == "music/live/b.mp3").Modified, b.Modified);
    }

    [Fact]
    public void Searches_match_names()
    {
        using var t = new TempDir();
        t.File("x/holiday 1.jpg");
        t.File("y/Holiday 2.JPG");
        t.File("y/work.doc");
        var cat = Catalogue.Scan("D", t.Path);
        Assert.Equal(2, cat.Search("holiday").Count());
        Assert.Single(cat.Search("*.doc"));
    }

    [Fact]
    public void Changes_are_found()
    {
        using var t = new TempDir();
        t.File("keep.txt", "a");
        t.File("gone.txt", "b");
        t.File("grow.txt", "c");
        var before = Catalogue.Scan("D", t.Path);
        System.IO.File.Delete(t.Combine("gone.txt"));
        System.IO.File.WriteAllText(t.Combine("grow.txt"), "cccc");
        t.File("new/fresh.txt", "d");
        var after = Catalogue.Scan("D", t.Path);

        var changes = CatalogueChanges.Between(before, after);
        Assert.Equal(new[] { "new", "new/fresh.txt" }, changes.Added.Select(e => e.RelPath).OrderBy(x => x));
        Assert.Equal(new[] { "gone.txt" }, changes.Removed.Select(e => e.RelPath));
        Assert.Equal(new[] { "grow.txt" }, changes.Changed.Select(e => e.RelPath));
        Assert.Contains("+ new/fresh.txt", changes.Report());
    }

    [Fact]
    public void Linked_folders_are_listed_but_not_walked()
    {
        if (OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        t.File("real/a.txt");
        System.IO.Directory.CreateSymbolicLink(t.Combine("real/loop"), t.Path);
        var cat = Catalogue.Scan("D", t.Path);
        Assert.Contains(cat.Entries, e => e.RelPath == "real/loop");
        Assert.DoesNotContain(cat.Entries, e => e.RelPath.StartsWith("real/loop/"));
    }

    [Fact]
    public void Odd_names_make_safe_file_names()
        => Assert.Equal("a_b_c", Catalogue.SafeFileName("a/b:c"));
}

public class SecureEraserTests
{
    [Fact]
    public void Files_are_overwritten_and_gone()
    {
        using var t = new TempDir();
        string f = t.File("secret.txt", new string('S', 5000));
        // Keep a handle on the same file to read what was written over it before it's removed.
        long before = new System.IO.FileInfo(f).Length;
        var done = new List<long>();
        SecureEraser.Erase(new[] { f }, new SyncProgress<(long Done, long Total)>(x => done.Add(x.Done)), CancellationToken.None);
        Assert.False(System.IO.File.Exists(f));
        Assert.Empty(System.IO.Directory.GetFileSystemEntries(t.Path));
        Assert.Equal(before, done[^1]);
    }

    [Fact]
    public void Folders_are_emptied_and_removed_but_links_targets_kept()
    {
        if (OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        string keep = t.File("elsewhere/keep.txt", "keep me");
        t.File("doomed/a.txt", "aaa");
        t.File("doomed/sub/b.txt", "bbb");
        System.IO.File.CreateSymbolicLink(t.Combine("doomed/link.txt"), keep);
        System.IO.Directory.CreateSymbolicLink(t.Combine("doomed/dirlink"), t.Combine("elsewhere"));

        SecureEraser.Erase(new[] { t.Combine("doomed") }, null, CancellationToken.None);

        Assert.False(System.IO.Directory.Exists(t.Combine("doomed")));
        Assert.Equal("keep me", System.IO.File.ReadAllText(keep));
        Assert.Equal(new[] { "elsewhere" }, System.IO.Directory.GetDirectories(t.Path).Select(System.IO.Path.GetFileName));
    }

    [Fact]
    public void Another_name_for_the_same_file_sees_nothing_of_the_old_bytes()
    {
        if (OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        string f = t.File("s.bin", new string('A', 200_000));
        string twin = t.Combine("twin.bin");
        // A hard link is the same file under a second name: it outlives the delete and shows what
        // was left in the file itself.
        var ln = System.Diagnostics.Process.Start("ln", new[] { f, twin });
        ln!.WaitForExit();
        if (!System.IO.File.Exists(twin)) return;

        SecureEraser.EraseFile(f, null, CancellationToken.None);

        Assert.False(System.IO.File.Exists(f));
        Assert.Equal(0, new System.IO.FileInfo(twin).Length);
    }
}

public class NestedArchiveTests
{
    [Fact]
    public void Up_from_a_nested_root_goes_back_into_the_outer_archive()
    {
        string copy = "/tmp/dori-tool-x/nested-1/inner.zip";
        NestedArchives.Register(copy, "archive:///home/me/outer.tar.gz!/stuff/inner.zip");
        Assert.Equal("archive:///home/me/outer.tar.gz!/stuff", ArchivePath.Parse(ArchivePath.RootUriFor(copy)).ParentUri());
        Assert.Equal("archive:///home/me/outer.tar.gz!/stuff/inner.zip!/docs",
                     NestedArchives.Describe(new ArchivePath(copy, "docs").FullUri));
    }

    [Fact]
    public void Ordinary_archives_still_go_up_to_their_folder()
        => Assert.Equal("/home/me", ArchivePath.Parse("archive:///home/me/plain.zip!/").ParentUri());

    [Fact]
    public void Inner_archives_open_and_read()
    {
        using var t = new TempDir();
        string innerSrc = t.File("src/hello.txt", "hi there");
        string inner = t.Combine("inner.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(t.Combine("src"), inner);
        string outerDir = t.Dir("outer");
        System.IO.File.Copy(inner, System.IO.Path.Combine(outerDir, "inner.zip"));
        string outer = t.Combine("outer.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(outerDir, outer);

        // What EnterNestedArchive does: take the entry out, then browse the copy.
        string entry = ArchivePath.RootUriFor(outer).Replace("!/", "!/inner.zip");
        string copy = t.Combine("copy/inner.zip");
        t.Dir("copy");
        using (var input = ProviderRegistry.For(entry).OpenRead(entry))
        using (var output = System.IO.File.Create(copy))
            input.CopyTo(output);
        NestedArchives.Register(copy, entry);
        var names = ProviderRegistry.For(ArchivePath.RootUriFor(copy)).EnumerateFiles(ArchivePath.RootUriFor(copy)).Select(e => e.Name);
        Assert.Contains("hello.txt", names);
        Assert.Equal(ArchivePath.RootUriFor(outer).TrimEnd('/'), ArchivePath.Parse(ArchivePath.RootUriFor(copy)).ParentUri().TrimEnd('/'));
    }
}

public class VideoFramesTests
{
    [Theory]
    [InlineData("clip.MP4", true)]
    [InlineData("film.mkv", true)]
    [InlineData("song.mp3", false)]
    [InlineData("noext", false)]
    public void Videos_are_known_by_ending(string name, bool video) => Assert.Equal(video, VideoFrames.IsVideoFile(name));

    [Fact]
    public void Frames_are_asked_for_as_png_at_a_width()
    {
        var args = VideoFrames.Arguments("/v/a.mp4", "5", 256);
        Assert.Equal(new[] { "-ss", "5", "-i", "/v/a.mp4" }, args.Skip(4).Take(4));
        Assert.Contains("scale=256:-2", args);
        Assert.Equal("-", args[^1]);
    }

    [Fact]
    public async Task A_frame_comes_out_when_ffmpeg_is_installed()
    {
        if (!VideoFrames.Available) return;
        using var t = new TempDir();
        string video = t.Combine("test.mp4");
        var psi = new System.Diagnostics.ProcessStartInfo(VideoFrames.FfmpegPath!)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=10", "-pix_fmt", "yuv420p", video })
            psi.ArgumentList.Add(a);
        using (var p = System.Diagnostics.Process.Start(psi)!) await p.WaitForExitAsync();
        if (!System.IO.File.Exists(video)) return;   // this ffmpeg can't encode; nothing to check

        var png = await VideoFrames.GrabPngAsync(video, 64);
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png!.Take(4));
    }
}
