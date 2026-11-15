using System.IO.Compression;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

/// <summary>Copy, move, delete, rename and undo with archives, through the operations the UI queues.</summary>
public class ArchiveOperationsTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly OperationQueue _q = new();
    private readonly string _zip;

    public ArchiveOperationsTests()
    {
        _zip = _t.Combine("box.zip");
        using var z = ZipFile.Open(_zip, ZipArchiveMode.Create);
        foreach (var (name, text) in new[] { ("keep.txt", "keep"), ("docs/a.md", "A"), ("docs/b.md", "B") })
        {
            using var w = new StreamWriter(z.CreateEntry(name).Open());
            w.Write(text);
        }
    }

    public void Dispose()
    {
        _q.CancelAll();
        _t.Dispose();
    }

    private string U(string entry) => new ArchivePath(_zip, entry).FullUri;
    private string Root => ArchivePath.RootUriFor(_zip);

    private Dictionary<string, string> Zip()
    {
        using var z = ZipFile.OpenRead(_zip);
        return z.Entries.Where(e => !e.FullName.EndsWith('/'))
                .ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    private async Task Run(FileOperation op)
    {
        _q.Enqueue(op);
        await op.Completion.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Copying_files_and_a_folder_in_keeps_the_originals()
    {
        var file = _t.File("in/new.txt", "new");
        _t.File("in/folder/x.txt", "x");
        var items = new List<TransferItem>
        {
            new(file, U("docs"), U("docs/new.txt"), false),
            new(_t.Combine("in/folder"), Root, U("folder"), true),
        };
        Assert.True(FileOperation.NeedsArchiveTransfer(false, items));

        var op = FileOperation.ArchiveTransfer(false, items, "copy in");
        await Run(op);

        Assert.Equal(OperationStatus.Completed, op.Status);
        var z = Zip();
        Assert.Equal("new", z["docs/new.txt"]);
        Assert.Equal("x", z["folder/x.txt"]);
        Assert.Equal("keep", z["keep.txt"]);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Moving_in_removes_the_originals()
    {
        var file = _t.File("in/moved.txt", "m");
        _t.File("in/dir/y.txt", "y");
        var items = new List<TransferItem>
        {
            new(file, Root, U("moved.txt"), false),
            new(_t.Combine("in/dir"), Root, U("dir"), true),
        };
        var op = FileOperation.ArchiveTransfer(true, items, "move in");
        await Run(op);

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal("m", Zip()["moved.txt"]);
        Assert.Equal("y", Zip()["dir/y.txt"]);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(_t.Combine("in/dir")));
    }

    [Fact]
    public async Task Moving_within_the_archive_is_a_rename()
    {
        var items = new List<TransferItem> { new(U("docs/a.md"), Root, U("a.md"), false) };
        var op = FileOperation.ArchiveTransfer(true, items, "move within");
        await Run(op);

        var z = Zip();
        Assert.Equal("A", z["a.md"]);
        Assert.False(z.ContainsKey("docs/a.md"));
    }

    [Fact]
    public async Task Moving_out_extracts_then_removes_everything_moved()
    {
        var dest = _t.Dir("out") + Path.DirectorySeparatorChar;
        var items = new List<TransferItem>
        {
            new(U("keep.txt"), dest, dest + "keep.txt", false),
            new(U("docs"), dest, dest + "docs", true),
        };
        Assert.True(FileOperation.NeedsArchiveTransfer(true, items));
        Assert.False(FileOperation.NeedsArchiveTransfer(false, items));   // a plain extract needs no rewrite

        var op = FileOperation.ArchiveTransfer(true, items, "move out");
        await Run(op);

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal("keep", File.ReadAllText(dest + "keep.txt"));
        Assert.Equal("B", File.ReadAllText(Path.Combine(dest, "docs", "b.md")));
        Assert.Empty(Zip());
    }

    [Fact]
    public async Task Deleting_inside_an_archive_removes_the_entries()
    {
        var op = FileOperation.DeleteBatch(new[] { (U("keep.txt"), false), (U("docs"), true) }, useTrash: false, "delete");
        await Run(op);

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Empty(Zip());
    }

    [Fact]
    public async Task Renaming_inside_an_archive_including_a_swap()
    {
        var folder = U("docs");
        var plan = new[]
        {
            new RenamePlanRow("a.md", "b.md", false) { Status = RenameStatus.Change },
            new RenamePlanRow("b.md", "a.md", false) { Status = RenameStatus.Change },
        };
        var (errors, renamed) = await FileUtility.ApplyRenamePlanDetailedAsync(folder, plan);

        Assert.Empty(errors);
        Assert.Equal(2, renamed.Count);
        Assert.Equal("B", Zip()["docs/a.md"]);
        Assert.Equal("A", Zip()["docs/b.md"]);
    }

    [Fact]
    public async Task A_move_into_an_archive_can_be_undone()
    {
        var src = _t.Dir("undo-src") + Path.DirectorySeparatorChar;
        _t.File("undo-src/u.txt", "u");
        var items = new List<TransferItem> { new(src + "u.txt", Root, U("u.txt"), false) };
        await Run(FileOperation.ArchiveTransfer(true, items, "move in"));

        var undo = UndoPlanner.RecordMove(src, items, new HashSet<string>(), UndoPlanner.Exists, DateTime.Now)!;
        var (moves, problems) = UndoPlanner.ReverseMove(undo, UndoPlanner.Exists);
        Assert.Empty(problems);
        Assert.True(FileOperation.NeedsArchiveTransfer(true, moves));
        await Run(FileOperation.ArchiveTransfer(true, moves, "undo"));

        Assert.Equal("u", File.ReadAllText(src + "u.txt"));
        Assert.False(Zip().ContainsKey("u.txt"));
    }

    [Fact]
    public async Task A_failed_copy_in_leaves_the_archive_untouched_and_can_be_retried()
    {
        var before = File.ReadAllBytes(_zip);
        var items = new List<TransferItem> { new(_t.Combine("missing.txt"), Root, U("missing.txt"), false) };
        var op = FileOperation.ArchiveTransfer(false, items, "copy in");
        await Run(op);

        Assert.Equal(OperationStatus.Failed, op.Status);
        Assert.Equal(before, File.ReadAllBytes(_zip));

        _t.File("missing.txt", "here now");
        var again = FileOperation.Retry(op)!;
        await Run(again);
        Assert.Equal(OperationStatus.Completed, again.Status);
        Assert.Equal("here now", Zip()["missing.txt"]);
    }

    [Fact]
    public void Making_a_folder_inside_an_archive()
    {
        var p = ProviderRegistry.For(Root);
        p.CreateDirectory(U("newfolder"));
        p.CreateDirectory(U("newfolder"));   // already there: nothing happens
        using var z = ZipFile.OpenRead(_zip);
        Assert.Single(z.Entries, e => e.FullName == "newfolder/");
        Assert.True(p.DirectoryExists(U("newfolder")));
    }
}
