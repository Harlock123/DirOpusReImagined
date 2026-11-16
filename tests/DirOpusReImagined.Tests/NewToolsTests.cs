using System.Security.Cryptography;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;
using DirOpusReImagined.FileSystem.Preview;
using SkiaSharp;

namespace DirOpusReImagined.Tests;

public class ButtonPromptsTests
{
    [Fact]
    public void Each_question_is_asked_once_in_order()
        => Assert.Equal(new[] { "Message", "Value" },
                        ButtonPrompts.Questions("commit -m \"%ASK:Message%\" %ASK% --again %ask:Message%"));

    [Fact]
    public void Arguments_without_the_token_ask_nothing()
        => Assert.Empty(ButtonPrompts.Questions("%LF1% --flag"));

    [Fact]
    public void Answers_fill_every_place_their_question_is_used()
    {
        var answers = new Dictionary<string, string> { ["Message"] = "fix it", ["Value"] = "42" };
        Assert.Equal("-m \"fix it\" -n 42 \"fix it\"",
                     ButtonPrompts.Fill("-m \"%ASK:Message%\" -n %ASK% \"%ASK: Message %\"", answers));
    }
}

public class FileSplitterTests
{
    [Theory]
    [InlineData("100 MB", 100L * 1024 * 1024)]
    [InlineData("4.5GB", (long)(4.5 * 1024 * 1024 * 1024))]
    [InlineData("650m", 650L * 1024 * 1024)]
    [InlineData("1,5 k", 1536)]
    [InlineData("1048576", 1048576)]
    [InlineData("2 KiB", 2048)]
    public void Sizes_are_read(string text, long bytes)
    {
        Assert.True(FileSplitter.TryParseSize(text, out long n));
        Assert.Equal(bytes, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("big")]
    [InlineData("0")]
    [InlineData("-5 MB")]
    public void Nonsense_sizes_are_refused(string text) => Assert.False(FileSplitter.TryParseSize(text, out _));

    [Fact]
    public void Piece_names_have_three_digits_or_more_when_needed()
    {
        Assert.Equal("a.bin.007", FileSplitter.PieceFileName("a.bin", 7, 12));
        Assert.Equal("a.bin.0007", FileSplitter.PieceFileName("a.bin", 7, 1200));
        Assert.Equal("film.mkv", FileSplitter.JoinedName("film.mkv.003"));
        Assert.Null(FileSplitter.JoinedName("film.mkv"));
        Assert.Null(FileSplitter.JoinedName(".001"));
    }

    private static byte[] Data(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);
        return data;
    }

    [Fact]
    public void A_split_file_joins_back_exactly_and_is_checked()
    {
        using var t = new TempDir();
        var data = Data(10_000);
        string source = t.Combine("a.bin");
        File.WriteAllBytes(source, data);
        string pieces = t.Dir("pieces"), joined = t.Dir("joined");

        var written = FileSplitter.Split(source, pieces, 3000);
        Assert.Equal(new[] { "a.bin.001", "a.bin.002", "a.bin.003", "a.bin.004", "a.bin.sha256" }, written.Select(Path.GetFileName));
        Assert.Equal(1000, new FileInfo(Path.Combine(pieces, "a.bin.004")).Length);

        var result = FileSplitter.Join(Path.Combine(pieces, "a.bin.003"), joined);
        Assert.True(result.Verified);
        Assert.Equal(4, result.Pieces);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(joined, "a.bin")));
    }

    [Fact]
    public void A_damaged_piece_is_caught_by_the_checksum()
    {
        using var t = new TempDir();
        string source = t.Combine("a.bin");
        File.WriteAllBytes(source, Data(5000));
        string pieces = t.Dir("pieces");
        FileSplitter.Split(source, pieces, 2000);
        var bytes = File.ReadAllBytes(Path.Combine(pieces, "a.bin.002"));
        bytes[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(pieces, "a.bin.002"), bytes);

        var ex = Assert.Throws<IOException>(() => FileSplitter.Join(Path.Combine(pieces, "a.bin.001"), t.Dir("out")));
        Assert.Contains("doesn't match", ex.Message);
    }

    [Fact]
    public void Pieces_without_a_checksum_file_still_join()
    {
        using var t = new TempDir();
        t.File("p/x.txt.001", "hello ");
        t.File("p/x.txt.002", "world");
        var result = FileSplitter.Join(t.Combine("p/x.txt.002"), t.Dir("out"));
        Assert.Null(result.Verified);
        Assert.Equal("hello world", File.ReadAllText(t.Combine("out/x.txt")));
    }

    [Fact]
    public void Joining_needs_the_first_piece()
    {
        using var t = new TempDir();
        t.File("p/x.txt.002", "b");
        Assert.Throws<IOException>(() => FileSplitter.FindPieces(t.Combine("p/x.txt.002")));
    }

    [Fact]
    public void Nothing_is_overwritten()
    {
        using var t = new TempDir();
        string source = t.File("a.txt", "abcdef");
        t.File("out/a.txt.002", "already here");
        Assert.Throws<IOException>(() => FileSplitter.Split(source, t.Combine("out"), 2));
        Assert.False(File.Exists(t.Combine("out/a.txt.001")));

        t.File("p/b.txt.001", "x");
        t.File("dest/b.txt", "keep");
        Assert.Throws<IOException>(() => FileSplitter.Join(t.Combine("p/b.txt.001"), t.Combine("dest")));
        Assert.Equal("keep", File.ReadAllText(t.Combine("dest/b.txt")));
    }

    [Fact]
    public void A_cancelled_split_leaves_nothing_behind()
    {
        using var t = new TempDir();
        string source = t.Combine("a.bin");
        File.WriteAllBytes(source, Data(4096));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FileSplitter.Split(source, t.Dir("out"), 1000, null, cts.Token));
        Assert.Empty(Directory.GetFiles(t.Combine("out")));
    }
}

public class DiskUsageTests
{
    [Fact]
    public void Folders_add_up_what_is_inside_them_and_list_largest_first()
    {
        using var t = new TempDir();
        t.File("small.txt", new string('a', 10));
        t.File("big/one.bin", new string('b', 1000));
        t.File("big/deeper/two.bin", new string('c', 500));
        t.File("mid/three.bin", new string('d', 600));

        var top = DiskUsage.Scan(t.Path);
        Assert.Equal(2110, top.Size);
        Assert.Equal(4, top.FileCount);
        Assert.Equal(new[] { "big", "mid", "small.txt" }, top.BySize().Select(n => n.Name));
        var big = top.Children.Single(c => c.Name == "big");
        Assert.Equal(1500, big.Size);
        Assert.Equal(2, big.FileCount);
        Assert.Same(top, big.Parent);
    }

    [Fact]
    public void Linked_folders_count_as_nothing_and_are_not_followed()
    {
        if (OperatingSystem.IsWindows()) return;   // making links needs extra rights there
        using var t = new TempDir();
        t.File("real/data.bin", new string('x', 300));
        Directory.CreateSymbolicLink(t.Combine("loop"), t.Path);   // a link back to the top

        var top = DiskUsage.Scan(t.Path);
        Assert.Equal(300, top.Size);
        var loop = top.Children.Single(c => c.Name == "loop");
        Assert.True(loop.IsLink);
        Assert.Equal(0, loop.Size);
        Assert.Empty(loop.Children);
    }

    [Fact]
    public void Shares_are_between_zero_and_one()
    {
        Assert.Equal(0.25, DiskUsage.Share(25, 100));
        Assert.Equal(0, DiskUsage.Share(5, 0));
        Assert.Equal(1, DiskUsage.Share(200, 100));
    }
}

public class LabelStoreTests
{
    [Fact]
    public void Colours_and_tags_are_kept_and_shown()
    {
        var store = new LabelStore(null);
        store.SetColour(new[] { "/a/x.txt" }, LabelColour.Red);
        store.SetTags(new[] { "/a/x.txt", "/a/y.txt" }, LabelStore.ParseTags("work, to do ,Work,"));

        Assert.Equal("Red · work, to do", store.Get("/a/x.txt").Display);
        Assert.Equal("work, to do", store.Get("/a/y.txt").Display);
        Assert.True(store.Get("/a/x.txt").Has("red"));
        Assert.True(store.Get("/a/y.txt").Has("TO DO"));
        Assert.False(store.Get("/a/y.txt").Has("red"));
        Assert.Equal(new[] { "to do", "work" }, store.AllTags());
        Assert.True(store.Get("/a/z.txt").IsEmpty);
    }

    [Fact]
    public void Toggling_a_tag_adds_it_to_all_unless_all_have_it()
    {
        var store = new LabelStore(null);
        store.SetTags(new[] { "/a" }, new[] { "keep" });
        store.ToggleTag(new[] { "/a", "/b" }, "keep");
        Assert.Equal(new[] { "keep" }, store.Get("/b").Tags);
        store.ToggleTag(new[] { "/a", "/b" }, "keep");
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Moving_a_folder_moves_the_labels_inside_it()
    {
        var store = new LabelStore(null);
        store.SetColour(new[] { "/p/dir", "/p/dir/a.txt", "/p/dir/sub/b.txt", "/p/dirt.txt" }, LabelColour.Blue);
        Assert.True(store.Move("/p/dir/", "/q/new"));
        Assert.Equal(LabelColour.Blue, store.Get("/q/new").Colour);
        Assert.Equal(LabelColour.Blue, store.Get("/q/new/a.txt").Colour);
        Assert.Equal(LabelColour.Blue, store.Get("/q/new/sub/b.txt").Colour);
        Assert.Equal(LabelColour.Blue, store.Get("/p/dirt.txt").Colour);   // a different name, not inside
        Assert.True(store.Get("/p/dir/a.txt").IsEmpty);
    }

    [Fact]
    public void Labels_survive_saving_and_loading()
    {
        using var t = new TempDir();
        string file = t.Combine("Labels.xml");
        var store = new LabelStore(file);
        store.SetColour(new[] { "/a b/c & d.txt" }, LabelColour.Green);
        store.SetTags(new[] { "/a b/c & d.txt" }, new[] { "x", "y" });
        store.Save();

        var again = LabelStore.Load(file);
        Assert.Equal("Green · x, y", again.Get("/a b/c & d.txt").Display);
    }

    [Fact]
    public void Forget_and_prune_remove_labels()
    {
        var store = new LabelStore(null);
        store.SetColour(new[] { "/d", "/d/a", "/e" }, LabelColour.Grey);
        store.Forget("/d");
        Assert.Equal(1, store.Count);
        Assert.Equal(1, store.Prune(_ => false));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void An_unreadable_file_means_no_labels()
    {
        using var t = new TempDir();
        string file = t.File("Labels.xml", "<<< not xml");
        Assert.Equal(0, LabelStore.Load(file).Count);
    }
}

public class TagFilterTests
{
    [Fact]
    public void Tag_patterns_match_labels_and_other_patterns_match_names()
    {
        var m = SavedFilters.EntryMatcher("*.cs; tag:work");
        Assert.True(m("a.cs", _ => false));
        Assert.True(m("a.txt", t => t == "work"));
        Assert.False(m("a.txt", _ => false));
        Assert.True(SavedFilters.EntryMatcher("")("anything", _ => false));
        Assert.False(SavedFilters.EntryMatcher("TAG:red")("a.txt", t => t == "blue"));
    }
}

public class GitStatusTests
{
    [Fact]
    public void Porcelain_output_is_read_including_renames()
    {
        var entries = GitStatus.Parse(" M src/a.cs\0R  new.txt\0old.txt\0?? tmp/\0!! bin/\0");
        Assert.Equal(new[] { "src/a.cs", "new.txt", "tmp/", "bin/" }, entries.Select(e => e.Path));
        Assert.True(entries[2].IsDirectory);
    }

    [Theory]
    [InlineData(' ', 'M', "Modified")]
    [InlineData('M', ' ', "Staged")]
    [InlineData('M', 'M', "Modified")]
    [InlineData('A', ' ', "Added")]
    [InlineData('R', ' ', "Renamed")]
    [InlineData('U', 'U', "Conflict")]
    [InlineData('?', '?', "Untracked")]
    [InlineData('!', '!', "Ignored")]
    public void Status_letters_become_words(char x, char y, string word) => Assert.Equal(word, GitStatus.Word(x, y));

    [Fact]
    public void Folders_show_the_strongest_state_inside_them()
    {
        var entries = GitStatus.Parse(" M sub/a.txt\0?? sub/new/\0!! sub/x.log\0?? sub/deep/n.txt\0!! sub/build/\0");
        var words = GitStatus.ForNames(entries, "sub/", new[] { "a.txt", "new", "x.log", "deep", "build", "clean.txt" });
        Assert.Equal("Modified", words["a.txt"]);
        Assert.Equal("Untracked", words["new"]);
        Assert.Equal("Ignored", words["x.log"]);
        Assert.Equal("Untracked", words["deep"]);
        Assert.Equal("Ignored", words["build"]);
        Assert.False(words.ContainsKey("clean.txt"));

        // From the top, "sub" holds a change; ignored files alone wouldn't mark it.
        Assert.Equal("Modified", GitStatus.ForNames(entries, "", new[] { "sub" })["sub"]);
        Assert.Empty(GitStatus.ForNames(GitStatus.Parse("!! sub/x.log\0"), "", new[] { "sub" }));
    }

    [Fact]
    public void Flat_view_names_inside_an_untracked_folder_are_untracked()
    {
        var words = GitStatus.ForNames(GitStatus.Parse("?? new/\0"), "", new[] { "new/deep/file.txt" });
        Assert.Equal("Untracked", words["new/deep/file.txt"]);
    }

    private static bool Git(string folder, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(folder);
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task A_real_repository_is_asked()
    {
        using var t = new TempDir();
        if (!Git(t.Path, "init", "-q")) return;   // no git here: nothing to check
        t.File("a.txt", "one");
        t.File("sub/b.txt", "two");
        Assert.True(Git(t.Path, "add", "."));
        Assert.True(Git(t.Path, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "first"));
        t.File("sub/b.txt", "changed");
        t.File("sub/new.txt", "new");

        var words = await GitStatus.ForFolderAsync(t.Combine("sub"), new[] { "b.txt", "new.txt" });
        Assert.NotNull(words);
        Assert.Equal("Modified", words!["b.txt"]);
        Assert.Equal("Untracked", words["new.txt"]);

        var top = await GitStatus.ForFolderAsync(t.Path, new[] { "a.txt", "sub" });
        Assert.Equal("Modified", top!["sub"]);
        Assert.False(top.ContainsKey("a.txt"));
    }

    [Fact]
    public async Task A_folder_outside_any_repository_gives_nothing()
    {
        using var t = new TempDir();
        if (GitStatus.InsideWorkTree(t.Path)) return;   // the temp folder happens to be in a repository
        Assert.Null(await GitStatus.ForFolderAsync(t.Path, new[] { "x" }));
    }
}

public class SyncBrowseTests
{
    private static readonly Func<string, bool> Exists = _ => true;

    [Fact]
    public void Going_into_a_folder_takes_the_other_panel_into_the_same_name()
        => Assert.Equal("/backup/src/app", SyncBrowse.Follow("/work", "/work/src/app", "/backup", Exists));

    [Fact]
    public void Going_up_takes_the_other_panel_up_as_far()
        => Assert.Equal("/backup", SyncBrowse.Follow("/work/src/app", "/work", "/backup/src/app/", Exists));

    [Fact]
    public void The_other_panel_stays_when_it_has_no_such_folder()
        => Assert.Null(SyncBrowse.Follow("/work", "/work/src", "/backup", _ => false));

    [Fact]
    public void Jumping_elsewhere_does_not_move_the_other_panel()
        => Assert.Null(SyncBrowse.Follow("/work/src", "/home/me", "/backup/src", Exists));

    [Fact]
    public void Going_up_further_than_the_other_panel_is_deep_does_nothing()
        => Assert.Null(SyncBrowse.Follow("/a/b/c", "/a", "/x", Exists));

    [Fact]
    public void Windows_and_archive_paths_keep_their_form()
    {
        Assert.Equal(@"D:\backup\src", SyncBrowse.Follow(@"C:\work", @"C:\work\src", @"D:\backup", Exists));
        Assert.Equal("archive:///t/a.zip!/docs/src", SyncBrowse.Follow("/work", "/work/src", "archive:///t/a.zip!/docs", Exists));
        Assert.Equal("/", SyncBrowse.Follow("/a/b", "/a", "/x", Exists));
    }
}

public class ImageToolsTests
{
    [Theory]
    [InlineData(4000, 3000, 1000, 0, 1000, 750)]
    [InlineData(4000, 3000, 0, 600, 800, 600)]
    [InlineData(4000, 3000, 1000, 500, 667, 500)]
    [InlineData(800, 600, 1000, 1000, 800, 600)]   // never enlarged
    [InlineData(800, 600, 0, 0, 800, 600)]
    public void Pictures_shrink_to_fit_keeping_their_shape(int w, int h, int maxW, int maxH, int ew, int eh)
        => Assert.Equal((ew, eh), ImageTools.FitWithin(w, h, maxW, maxH));

    [Fact]
    public void New_names_follow_the_format_and_suffix()
    {
        var job = new ImageJob(0, 0, 0, true, ImageFormatChoice.Same, 90, "_small");
        Assert.Equal("photo_small.JPG", ImageTools.OutputName("/p/photo.JPG", job));
        Assert.Equal("anim_small.png", ImageTools.OutputName("/p/anim.gif", job));
        Assert.Equal("photo.webp", ImageTools.OutputName("/p/photo.jpeg", job with { Format = ImageFormatChoice.Webp, NameSuffix = "" }));
    }

    /// <summary>A 3×2 picture whose every pixel is a different colour.</summary>
    private static SKBitmap Grid()
    {
        var b = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                b.SetPixel(x, y, new SKColor((byte)(40 * x + 10), (byte)(100 * y + 20), 200));
        return b;
    }

    [Theory]
    [InlineData(2, SKEncodedOrigin.TopRight)]
    [InlineData(3, SKEncodedOrigin.BottomRight)]
    [InlineData(4, SKEncodedOrigin.BottomLeft)]
    [InlineData(5, SKEncodedOrigin.LeftTop)]
    [InlineData(6, SKEncodedOrigin.RightTop)]
    [InlineData(7, SKEncodedOrigin.RightBottom)]
    [InlineData(8, SKEncodedOrigin.LeftBottom)]
    public void Turning_upright_puts_every_pixel_where_the_viewer_does(int orientation, SKEncodedOrigin origin)
    {
        using var src = Grid();
        using var turned = ImageTools.Orient(src, origin);
        bool swap = ImageOrientation.SwapsSides(orientation);
        Assert.Equal(swap ? 2 : 3, turned.Width);
        Assert.Equal(swap ? 3 : 2, turned.Height);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
            {
                var (tx, ty) = ImageOrientation.Target(orientation, x, y, 3, 2);
                Assert.Equal(src.GetPixel(x, y), turned.GetPixel(tx, ty));
            }
    }

    [Fact]
    public void A_picture_is_shrunk_turned_and_converted_into_a_new_file()
    {
        using var t = new TempDir();
        string source = t.Combine("wide.png");
        using (var bmp = new SKBitmap(400, 200))
        {
            using (var c = new SKCanvas(bmp)) c.Clear(SKColors.CornflowerBlue);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(source, data.ToArray());
        }
        string outDir = t.Dir("out");

        string made = ImageTools.Process(source, outDir, new ImageJob(100, 0, 90, true, ImageFormatChoice.Jpeg, 80, ""));
        Assert.Equal(Path.Combine(outDir, "wide.jpg"), made);
        using var codec = SKCodec.Create(made);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        // Turned a quarter, then fitted to 100 wide: 200×400 → 100×200.
        Assert.Equal(100, codec.Info.Width);
        Assert.Equal(200, codec.Info.Height);
        Assert.True(File.Exists(source));

        Assert.Throws<IOException>(() => ImageTools.Process(source, outDir, new ImageJob(100, 0, 90, true, ImageFormatChoice.Jpeg, 80, "")));
        Assert.Throws<IOException>(() => ImageTools.Process(source, t.Path, new ImageJob(0, 0, 0, true, ImageFormatChoice.Same, 80, "")));
    }
}

public class ResumeCopyTests
{
    private static byte[] Data(int length)
    {
        var data = new byte[length];
        new Random(7).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task A_copy_carries_on_from_a_matching_part_file()
    {
        using var t = new TempDir();
        var data = Data(3 * 1024 * 1024 + 123);
        string src = t.Combine("big.bin"), dst = t.Combine("out.bin");
        File.WriteAllBytes(src, data);
        File.WriteAllBytes(StreamCopy.PartialPathFor(dst), data.AsSpan(0, 2 * 1024 * 1024 + 7).ToArray());

        long firstReport = -1;
        await StreamCopy.LocalToLocalAsync(src, dst, overwrite: true,
            new SyncProgress<TransferProgress>(p => { if (firstReport < 0) firstReport = p.BytesDone; }), CancellationToken.None);

        Assert.Equal(data, File.ReadAllBytes(dst));
        Assert.False(File.Exists(StreamCopy.PartialPathFor(dst)));
        Assert.True(firstReport >= 2 * 1024 * 1024);   // it didn't start from nothing
    }

    [Fact]
    public async Task A_part_file_that_does_not_match_is_started_over()
    {
        using var t = new TempDir();
        var data = Data(2 * 1024 * 1024);
        string src = t.Combine("big.bin"), dst = t.Combine("out.bin");
        File.WriteAllBytes(src, data);
        var wrong = data.AsSpan(0, 1024 * 1024).ToArray();
        wrong[^1] ^= 0xFF;
        File.WriteAllBytes(StreamCopy.PartialPathFor(dst), wrong);

        await StreamCopy.LocalToLocalAsync(src, dst, overwrite: true, null, CancellationToken.None);
        Assert.Equal(data, File.ReadAllBytes(dst));
    }

    [Fact]
    public void Only_a_matching_prefix_no_longer_than_the_source_counts()
    {
        using var t = new TempDir();
        string src = t.Combine("s"), part = t.Combine("p");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(part, new byte[] { 1, 2 });
        Assert.Equal(2, StreamCopy.ResumablePrefix(src, part, 4));
        File.WriteAllBytes(part, new byte[] { 1, 2, 3, 4, 5 });
        Assert.Equal(0, StreamCopy.ResumablePrefix(src, part, 4));
        Assert.Equal(0, StreamCopy.ResumablePrefix(src, t.Combine("missing"), 4));
    }

    [Fact]
    public async Task A_cancelled_small_copy_leaves_no_part_file_and_no_target()
    {
        using var t = new TempDir();
        string src = t.Combine("s.bin"), dst = t.Combine("d.bin");
        File.WriteAllBytes(src, Data(1000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StreamCopy.LocalToLocalAsync(src, dst, true, null, cts.Token));
        Assert.False(File.Exists(dst));
        Assert.False(File.Exists(StreamCopy.PartialPathFor(dst)));
    }

    [Fact]
    public async Task An_overwritten_file_keeps_its_old_contents_until_the_copy_is_complete()
    {
        using var t = new TempDir();
        string src = t.Combine("s.bin"), dst = t.File("d.bin", "old");
        File.WriteAllBytes(src, Data(5000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StreamCopy.LocalToLocalAsync(src, dst, true, null, cts.Token));
        Assert.Equal("old", File.ReadAllText(dst));
    }
}

public class ArchiveWriteBackTests
{
    private static string MakeZip(TempDir t)
    {
        string folder = t.Dir("content");
        File.WriteAllText(Path.Combine(folder, "note.txt"), "first");
        string zip = t.Combine("a.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
        return zip;
    }

    private static string ReadEntry(string zip, string entry)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_saved_copy_is_noticed_and_put_back()
    {
        using var t = new TempDir();
        string zip = MakeZip(t);
        string temp = t.File("tmp/note.txt", "first");
        var wb = new ArchiveWriteBack();
        wb.Track(temp, zip, "note.txt");

        Assert.Null(wb.Changed(temp));                    // untouched
        File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddMinutes(1));
        Assert.Null(wb.Changed(temp));                    // touched only: the same contents

        File.WriteAllText(temp, "second");
        var changed = wb.Changed(temp);
        Assert.NotNull(changed);
        wb.WriteBack(changed!);
        Assert.Equal("second", ReadEntry(zip, "note.txt"));
        Assert.Null(wb.Changed(temp));                    // put back: nothing more to do
    }

    [Fact]
    public void Declining_waits_for_the_next_save()
    {
        using var t = new TempDir();
        string zip = MakeZip(t);
        string temp = t.File("tmp/note.txt", "first");
        var wb = new ArchiveWriteBack();
        wb.Track(temp, zip, "note.txt");
        File.WriteAllText(temp, "second");
        wb.Accept(wb.Changed(temp)!);
        Assert.Null(wb.Changed(temp));
        File.WriteAllText(temp, "third");
        Assert.NotNull(wb.Changed(temp));
        Assert.Equal("first", ReadEntry(zip, "note.txt"));
    }

    [Fact]
    public void Untracked_files_are_ignored()
    {
        using var t = new TempDir();
        Assert.Null(new ArchiveWriteBack().Changed(t.File("x.txt", "x")));
    }
}

[Collection("VerifyCopies")]
public class ArchiveVerifyTests
{
    [Fact]
    public async Task Copies_into_an_archive_are_checked_when_verify_is_on()
    {
        using var t = new TempDir();
        string folder = t.Dir("content");
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "x");
        string zip = t.Combine("a.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
        string src = t.File("in.txt", "copied in");

        bool before = AppOptions.VerifyCopies;
        AppOptions.VerifyCopies = true;
        try
        {
            string target = new ArchivePath(zip, "in.txt").FullUri;
            var op = FileOperation.ArchiveTransfer(false, new[] { new TransferItem(src, ArchivePath.RootUriFor(zip), target, false) }, "copy");
            await op.ExecuteAsync(new Progress<TransferProgress>(), CancellationToken.None);

            // The check reads the entry back from the archive; a different file would fail it.
            Assert.Null(Record.Exception(() => FileUtility.VerifyArchiveAdds(zip, new[] { new ArchiveAdd("in.txt", src, false, DateTime.Now) }, default)));
            string other = t.File("other.txt", "not the same");
            Assert.Throws<IOException>(() => FileUtility.VerifyArchiveAdds(zip, new[] { new ArchiveAdd("in.txt", other, false, DateTime.Now) }, default));
        }
        finally { AppOptions.VerifyCopies = before; }
    }
}
