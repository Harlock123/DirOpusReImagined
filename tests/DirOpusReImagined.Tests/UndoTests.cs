using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class UndoHistoryTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0);

    private static RenameUndo Rename(params (string Old, string New)[] items)
        => new("/f", items.Select(i => new RenameUndo.Item(i.Old, i.New, false)).ToList(), Now);

    [Fact]
    public void Starts_empty()
    {
        var h = new UndoHistory();
        Assert.Equal(0, h.Count);
        Assert.Null(h.Peek());
        Assert.Null(h.Pop());
    }

    [Fact]
    public void Newest_entry_comes_off_first()
    {
        var h = new UndoHistory();
        var a = Rename(("a", "b"));
        var c = Rename(("c", "d"));
        h.Push(a);
        h.Push(c);
        Assert.Same(c, h.Peek());
        Assert.Same(c, h.Pop());
        Assert.Same(a, h.Pop());
        Assert.Null(h.Pop());
    }

    [Fact]
    public void Keeps_only_the_newest_entries()
    {
        var h = new UndoHistory();
        for (int i = 0; i < UndoHistory.MaxEntries + 5; i++) h.Push(Rename(($"{i}", $"{i}x")));
        Assert.Equal(UndoHistory.MaxEntries, h.Count);
        Assert.Equal($"rename of \"{UndoHistory.MaxEntries + 4}x\"", h.Peek()!.Description);
    }

    [Fact]
    public void Changed_fires_on_push_and_pop_only_when_something_changes()
    {
        var h = new UndoHistory();
        int changes = 0;
        h.Changed += () => changes++;
        h.Push(Rename(("a", "b")));
        h.Pop();
        h.Pop(); // empty: no change
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Descriptions_name_a_single_item_or_count_several()
    {
        Assert.Equal("rename of \"b.txt\"", Rename(("a.txt", "b.txt")).Description);
        Assert.Equal("rename of 2 items", Rename(("a", "b"), ("c", "d")).Description);

        var one = new MoveUndo("/src/", new[] { new TransferItem("/src/a.txt", "/dst/", "/dst/a.txt", false) }, Now);
        Assert.Equal("move of \"a.txt\"", one.Description);
        var two = new MoveUndo("/src/", new[]
        {
            new TransferItem("/src/a", "/dst/", "/dst/a", false),
            new TransferItem("/src/b", "/dst/", "/dst/b", true),
        }, Now);
        Assert.Equal("move of 2 items", two.Description);
    }
}

public class UndoPlannerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0);
    private static readonly HashSet<string> None = new();

    private static Func<string, bool> Disk(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.Ordinal);
        return set.Contains;
    }

    private static string Join(string folder, string name) => folder.TrimEnd('/') + "/" + name;

    private static TransferItem Item(string name, bool dir = false)
        => new("/src/" + name, "/dst/", "/dst/" + name, dir);

    [Fact]
    public void RecordMove_keeps_only_items_that_left_and_arrived()
    {
        var items = new[] { Item("moved"), Item("failed"), Item("vanished") };
        // "failed" never left the source; "vanished" left but isn't at the target.
        var rec = UndoPlanner.RecordMove("/src/", items, None, Disk("/dst/moved", "/src/failed"), Now);
        var only = Assert.Single(rec!.Items);
        Assert.Equal("/src/moved", only.Source);
        Assert.Equal("/src/", rec.SourceFolder);
    }

    [Fact]
    public void RecordMove_leaves_out_overwrites()
    {
        var items = new[] { Item("a"), Item("b") };
        var rec = UndoPlanner.RecordMove("/src/", items, new HashSet<string> { "/dst/a" }, Disk("/dst/a", "/dst/b"), Now);
        Assert.Equal("/src/b", Assert.Single(rec!.Items).Source);
    }

    [Fact]
    public void RecordMove_is_null_when_nothing_moved()
        => Assert.Null(UndoPlanner.RecordMove("/src/", new[] { Item("a") }, None, Disk("/src/a"), Now));

    [Fact]
    public void RecordMove_ignores_an_item_moved_onto_itself()
    {
        var same = new TransferItem("/x/a", "/x/", "/x/a", false);
        Assert.Null(UndoPlanner.RecordMove("/x/", new[] { same }, None, Disk("/x/a"), Now));
    }

    [Fact]
    public void ReverseMove_swaps_source_and_target()
    {
        var undo = new MoveUndo("/src/", new[] { Item("a.txt"), Item("dir", dir: true) }, Now);
        var (moves, problems) = UndoPlanner.ReverseMove(undo, Disk("/dst/a.txt", "/dst/dir"));

        Assert.Empty(problems);
        Assert.Equal(2, moves.Count);
        Assert.Equal(new TransferItem("/dst/a.txt", "/src/", "/src/a.txt", false), moves[0]);
        Assert.Equal(new TransferItem("/dst/dir", "/src/", "/src/dir", true), moves[1]);
    }

    [Fact]
    public void ReverseMove_reports_items_gone_from_the_target_and_taken_sources()
    {
        var undo = new MoveUndo("/src/", new[] { Item("gone"), Item("taken"), Item("ok") }, Now);
        var (moves, problems) = UndoPlanner.ReverseMove(undo, Disk("/dst/taken", "/src/taken", "/dst/ok"));

        Assert.Equal("/src/ok", Assert.Single(moves).TargetPath);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("gone:"));
        Assert.Contains(problems, p => p.StartsWith("taken:"));
    }

    [Fact]
    public void ReverseRename_gives_each_item_its_old_name()
    {
        var undo = new RenameUndo("/f", new[] { new RenameUndo.Item("a.txt", "b.txt", false) }, Now);
        var (plan, problems) = UndoPlanner.ReverseRename(undo, Disk("/f/b.txt"), Join);

        Assert.Empty(problems);
        var row = Assert.Single(plan);
        Assert.Equal("b.txt", row.OldName);
        Assert.Equal("a.txt", row.NewName);
        Assert.True(row.WillRename);
    }

    [Fact]
    public void ReverseRename_allows_swaps_and_chains()
    {
        // a→b and b→a (swap), and c→d, d→e (chain): every old name is held by another item.
        var undo = new RenameUndo("/f", new[]
        {
            new RenameUndo.Item("a", "b", false), new RenameUndo.Item("b", "a", false),
            new RenameUndo.Item("c", "d", false), new RenameUndo.Item("d", "e", false),
        }, Now);
        var (plan, problems) = UndoPlanner.ReverseRename(undo, Disk("/f/a", "/f/b", "/f/d", "/f/e"), Join);
        Assert.Empty(problems);
        Assert.Equal(4, plan.Count);
    }

    [Fact]
    public void ReverseRename_reports_missing_items_and_taken_names()
    {
        var undo = new RenameUndo("/f", new[]
        {
            new RenameUndo.Item("a", "a2", false), // a2 has gone
            new RenameUndo.Item("b", "b2", false), // a new "b" has appeared
            new RenameUndo.Item("c", "c2", false), // fine
        }, Now);
        var (plan, problems) = UndoPlanner.ReverseRename(undo, Disk("/f/b2", "/f/b", "/f/c2"), Join);

        Assert.Equal("c", Assert.Single(plan).NewName);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void ReverseRename_allows_a_change_of_case_only()
    {
        // On a case-insensitive disk "readme" and "README" are the same file, so "exists" says yes for both.
        var undo = new RenameUndo("/f", new[] { new RenameUndo.Item("readme", "README", false) }, Now);
        var (plan, problems) = UndoPlanner.ReverseRename(undo, _ => true, Join);
        Assert.Empty(problems);
        Assert.Equal("readme", Assert.Single(plan).NewName);
    }
}

/// <summary>Real files: do a rename or move with the app's own code, then undo it.</summary>
public class UndoRoundTripTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static string[] Names(string dir)
        => Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task A_batch_rename_including_a_swap_is_undone()
    {
        var dir = _t.Dir("f");
        _t.File("f/a.txt", "A");
        _t.File("f/b.txt", "B");
        _t.File("f/c.txt", "C");
        _t.Dir("f/sub");

        var plan = new[]
        {
            new RenamePlanRow("a.txt", "b.txt", false) { Status = RenameStatus.Change },
            new RenamePlanRow("b.txt", "a.txt", false) { Status = RenameStatus.Change },
            new RenamePlanRow("c.txt", "c-renamed.txt", false) { Status = RenameStatus.Change },
            new RenamePlanRow("sub", "folder", true) { Status = RenameStatus.Change },
        };
        var (errors, renamed) = await FileUtility.ApplyRenamePlanDetailedAsync(dir, plan);
        Assert.Empty(errors);
        Assert.Equal(4, renamed.Count);
        Assert.Equal("B", File.ReadAllText(Path.Combine(dir, "a.txt")));

        var undo = UndoPlanner.RecordRename(dir, renamed, DateTime.Now)!;
        var (back, problems) = UndoPlanner.ReverseRename(undo, UndoPlanner.Exists, FileUtility.JoinPanelPath);
        Assert.Empty(problems);
        var (errors2, _) = await FileUtility.ApplyRenamePlanDetailedAsync(dir, back);
        Assert.Empty(errors2);

        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt", "sub" }, Names(dir));
        Assert.Equal("A", File.ReadAllText(Path.Combine(dir, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(dir, "b.txt")));
    }

    [Fact]
    public async Task A_move_of_files_and_a_folder_is_undone()
    {
        var src = _t.Dir("src") + Path.DirectorySeparatorChar;
        var dst = _t.Dir("dst") + Path.DirectorySeparatorChar;
        _t.File("src/one.txt", "1");
        _t.File("src/folder/inner.txt", "inner");
        _t.File("src/stays.txt", "not moved");

        var items = new List<TransferItem>
        {
            new(src + "one.txt", dst, dst + "one.txt", false),
            new(src + "folder", dst, dst + "folder", true),
        };
        await TransferCoordinator.RunAsync(items, move: true, progress: null, CancellationToken.None);
        Assert.Equal(new[] { "stays.txt" }, Names(src));

        var undo = UndoPlanner.RecordMove(src, items, new HashSet<string>(), UndoPlanner.Exists, DateTime.Now)!;
        Assert.Equal(2, undo.Items.Count);

        var (moves, problems) = UndoPlanner.ReverseMove(undo, UndoPlanner.Exists);
        Assert.Empty(problems);
        await TransferCoordinator.RunAsync(moves, move: true, progress: null, CancellationToken.None);

        Assert.Equal(new[] { "folder", "one.txt", "stays.txt" }, Names(src));
        Assert.Empty(Names(dst));
        Assert.Equal("inner", File.ReadAllText(Path.Combine(src, "folder", "inner.txt")));
    }

    [Fact]
    public async Task Undoing_a_move_never_replaces_a_file_that_appeared_since()
    {
        var src = _t.Dir("src") + Path.DirectorySeparatorChar;
        var dst = _t.Dir("dst") + Path.DirectorySeparatorChar;
        _t.File("src/a.txt", "original");

        var items = new List<TransferItem> { new(src + "a.txt", dst, dst + "a.txt", false) };
        await TransferCoordinator.RunAsync(items, move: true, progress: null, CancellationToken.None);
        var undo = UndoPlanner.RecordMove(src, items, new HashSet<string>(), UndoPlanner.Exists, DateTime.Now)!;

        _t.File("src/a.txt", "new file with the same name");
        var (moves, problems) = UndoPlanner.ReverseMove(undo, UndoPlanner.Exists);

        Assert.Empty(moves);
        Assert.Single(problems);
        Assert.Equal("new file with the same name", File.ReadAllText(Path.Combine(src, "a.txt")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(dst, "a.txt")));
    }
}
