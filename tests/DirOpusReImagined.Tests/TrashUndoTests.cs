using System.Text;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

/// <summary>Finding items in the trash and putting them back. Uses a private trash folder, never the real one.</summary>
public class TrashUndoTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private string DataHome => _t.Combine("data");

    [Fact]
    public void A_trashinfo_record_is_read()
    {
        var rec = TrashRecords.ParseTrashInfo("[Trash Info]\nPath=/home/me/My%20Docs/a%25b.txt\nDeletionDate=2026-11-15T10:20:30\n");
        Assert.NotNull(rec);
        Assert.Equal("/home/me/My Docs/a%b.txt", rec!.Value.Path);
        Assert.Equal(new DateTime(2026, 11, 15, 10, 20, 30), rec.Value.Deleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[Other]\nPath=/x\n")]
    public void A_record_without_a_path_is_ignored(string content)
        => Assert.Null(TrashRecords.ParseTrashInfo(content));

    private static byte[] RecycleRecord(int version, string path, DateTime deleted, long size)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes((long)version));
        b.AddRange(BitConverter.GetBytes(size));
        b.AddRange(BitConverter.GetBytes(deleted.ToFileTime()));
        byte[] name = Encoding.Unicode.GetBytes(path + "\0");
        if (version == 2)
        {
            b.AddRange(BitConverter.GetBytes(path.Length + 1));
            b.AddRange(name);
        }
        else
        {
            var fixedField = new byte[520];
            Array.Copy(name, fixedField, name.Length);
            b.AddRange(fixedField);
        }
        return b.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_recycle_bin_record_is_read(int version)
    {
        var when = new DateTime(2026, 11, 15, 9, 0, 0);
        var rec = TrashRecords.ParseRecycleBinInfo(RecycleRecord(version, @"C:\Users\me\report.docx", when, 1234));
        Assert.NotNull(rec);
        Assert.Equal(@"C:\Users\me\report.docx", rec!.Value.Path);
        Assert.Equal(when, rec.Value.Deleted);
        Assert.Equal(1234, rec.Value.Size);
    }

    [Fact]
    public void A_short_or_unknown_recycle_bin_record_is_ignored()
    {
        Assert.Null(TrashRecords.ParseRecycleBinInfo(new byte[10]));
        var bad = RecycleRecord(2, "x", DateTime.Now, 1);
        bad[0] = 9;
        Assert.Null(TrashRecords.ParseRecycleBinInfo(bad));
    }

    [Fact]
    public void The_newest_matching_record_since_the_delete_wins()
    {
        var since = new DateTime(2026, 11, 15, 12, 0, 0);
        var records = new[]
        {
            ("old.info", "/a/x.txt", since.AddHours(-1)),     // an older delete of the same path
            ("other.info", "/a/y.txt", since.AddSeconds(1)),  // a different file
            ("new.info", "/a/x.txt", since.AddSeconds(-1)),   // within the slack for whole-second times
        };
        Assert.Equal("new.info", TrashRecords.PickNewest(records, "/a/x.txt", since)!.Value.Info);
        Assert.Null(TrashRecords.PickNewest(records, "/a/z.txt", since));
    }

    [Fact]
    public void A_file_trashed_by_hand_is_found_and_put_back()
    {
        var file = _t.File("docs/note.txt", "keep me");
        var since = DateTime.Now;
        var item = TrashService.FreedesktopTrash(file, DataHome);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(item.TrashedPath));
        Assert.True(File.Exists(item.InfoPath));

        // The same item is found again from the records alone, as after gio or trash-put.
        var found = TrashService.FindInFreedesktopTrash(new[] { Path.Combine(DataHome, "Trash") }, file, false, since);
        Assert.Equal(item.TrashedPath, found!.TrashedPath);

        TrashService.Restore(found);
        Assert.Equal("keep me", File.ReadAllText(file));
        Assert.False(File.Exists(item.TrashedPath));
        Assert.False(File.Exists(item.InfoPath));
    }

    [Fact]
    public void A_folder_and_its_contents_come_back()
    {
        _t.File("proj/src/a.cs", "a");
        var folder = _t.Combine("proj");
        var item = TrashService.FreedesktopTrash(folder, DataHome);
        Assert.True(item.IsDirectory);
        Assert.False(Directory.Exists(folder));

        TrashService.Restore(item);
        Assert.Equal("a", File.ReadAllText(_t.Combine("proj/src/a.cs")));
    }

    [Fact]
    public void Two_deletes_of_the_same_name_are_told_apart()
    {
        var path = _t.File("same.txt", "first");
        var first = TrashService.FreedesktopTrash(path, DataHome);
        _t.File("same.txt", "second");
        var second = TrashService.FreedesktopTrash(path, DataHome);
        Assert.NotEqual(first.TrashedPath, second.TrashedPath);

        TrashService.Restore(second);
        Assert.Equal("second", File.ReadAllText(path));
    }

    [Fact]
    public void Putting_back_never_overwrites()
    {
        var path = _t.File("x.txt", "old");
        var item = TrashService.FreedesktopTrash(path, DataHome);
        _t.File("x.txt", "new");

        var ex = Assert.Throws<IOException>(() => TrashService.Restore(item));
        Assert.Contains("something new", ex.Message);
        Assert.Equal("new", File.ReadAllText(path));
        Assert.True(File.Exists(item.TrashedPath));
    }

    [Fact]
    public void An_item_gone_from_the_trash_says_so()
    {
        var path = _t.File("y.txt", "y");
        var item = TrashService.FreedesktopTrash(path, DataHome);
        File.Delete(item.TrashedPath);   // the trash was emptied
        Assert.Contains("no longer in the trash", Assert.Throws<IOException>(() => TrashService.Restore(item)).Message);
    }

    [Fact]
    public void Undo_plans_only_what_can_come_back()
    {
        var items = new[]
        {
            new TrashedItem("/a/ok.txt", "/t/ok.txt", null, false),
            new TrashedItem("/a/emptied.txt", "/t/emptied.txt", null, false),
            new TrashedItem("/a/taken.txt", "/t/taken.txt", null, false),
        };
        var undo = UndoPlanner.RecordTrash("/a", items, DateTime.Now)!;
        Assert.Equal("delete of 3 items", undo.Description);

        var present = new HashSet<string> { "/t/ok.txt", "/t/taken.txt", "/a/taken.txt" };
        var (restores, problems) = UndoPlanner.ReverseTrash(undo, present.Contains);
        Assert.Equal("/a/ok.txt", Assert.Single(restores).OriginalPath);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("no longer in the trash"));
        Assert.Contains(problems, p => p.Contains("something new"));
    }

    [Fact]
    public void A_permanent_delete_records_nothing_to_undo()
        => Assert.Null(UndoPlanner.RecordTrash("/a", Array.Empty<TrashedItem>(), DateTime.Now));

    [Fact]
    public async Task A_queued_delete_reports_what_it_trashed()
    {
        // Only meaningful where deletes go to a real trash; skip on a machine with no trash tool.
        if (!OperatingSystem.IsLinux()) return;
        var file = _t.File("queued.txt", "q");
        var op = FileOperation.DeleteBatch(new[] { (file, false) }, useTrash: true, "delete");
        var q = new OperationQueue();
        q.Enqueue(op);
        await op.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(OperationStatus.Completed, op.Status);
        var trashed = Assert.Single(op.Trashed);
        Assert.Equal(file, trashed.OriginalPath);
        TrashService.Restore(trashed);   // put it back, leaving the real trash as it was
        Assert.Equal("q", File.ReadAllText(file));
    }
}
