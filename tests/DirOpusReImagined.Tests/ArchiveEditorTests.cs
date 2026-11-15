using System.Formats.Tar;
using System.IO.Compression;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

/// <summary>Changing .zip, .tar and .tar.gz archives; each test runs on all three.</summary>
public class ArchiveEditorTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    public static TheoryData<string> Formats => new() { "a.zip", "a.tar", "a.tar.gz" };

    /// <summary>An archive holding readme.txt, docs/a.md, docs/sub/b.md and an empty folder.</summary>
    private async Task<string> MakeArchive(string name)
    {
        var src = _t.Dir("src-" + name);
        _t.File($"src-{name}/readme.txt", "readme");
        _t.File($"src-{name}/docs/a.md", "A");
        _t.File($"src-{name}/docs/sub/b.md", "B");
        _t.Dir($"src-{name}/empty");
        var format = ArchiveCreator.FormatFromName(name)!.Value;
        var r = await ArchiveCreator.CreateAsync(_t.Path, name,
            new[] { Path.Combine(src, "readme.txt"), Path.Combine(src, "docs"), Path.Combine(src, "empty") }, format);
        return r.ArchivePath;
    }

    /// <summary>Entry key → contents ("/" for a folder), read with plain .NET, not DORI's reader.</summary>
    private static Dictionary<string, string> Contents(string archive)
    {
        var result = new Dictionary<string, string>();
        if (archive.EndsWith(".zip"))
        {
            using var z = ZipFile.OpenRead(archive);
            foreach (var e in z.Entries)
            {
                if (e.FullName.EndsWith('/')) { result[e.FullName.TrimEnd('/')] = "/"; continue; }
                using var r = new StreamReader(e.Open());
                result[e.FullName] = r.ReadToEnd();
            }
            return result;
        }
        using var fs = File.OpenRead(archive);
        using Stream s = archive.EndsWith(".gz") ? new GZipStream(fs, CompressionMode.Decompress) : fs;
        using var tr = new TarReader(s);
        while (tr.GetNextEntry() is { } e)
        {
            string key = ArchiveEditor.NormalizeKey(e.Name);
            if (e.EntryType == TarEntryType.Directory) { result[key] = "/"; continue; }
            using var r = new StreamReader(e.DataStream!);
            result[key] = r.ReadToEnd();
        }
        return result;
    }

    private static string Uri(string archive, string entry) => new ArchivePath(archive, entry).FullUri;

    [Theory, MemberData(nameof(Formats))]
    public async Task Adds_a_file_and_a_folder_keeping_everything_else(string name)
    {
        var archive = await MakeArchive(name);
        var newFile = _t.File("new.txt", "new");
        _t.File("newdir/x.txt", "x");
        _t.Dir("newdir/emptyinside");

        var edit = new ArchiveEdit();
        edit.Adds.AddRange(ArchiveEditor.PlanAdds(new[]
        {
            (newFile, "docs/new.txt", false),
            (_t.Combine("newdir"), "newdir", true),
        }));
        ArchiveEditor.Apply(archive, edit);

        var c = Contents(archive);
        Assert.Equal("readme", c["readme.txt"]);
        Assert.Equal("B", c["docs/sub/b.md"]);
        Assert.Equal("new", c["docs/new.txt"]);
        Assert.Equal("x", c["newdir/x.txt"]);
        Assert.Equal("/", c["newdir/emptyinside"]);
        Assert.Equal("/", c["empty"]);
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task Adding_an_existing_name_replaces_it(string name)
    {
        var archive = await MakeArchive(name);
        var edit = new ArchiveEdit();
        edit.Adds.Add(new ArchiveAdd("readme.txt", _t.File("r2.txt", "replaced"), false, DateTime.Now));
        ArchiveEditor.Apply(archive, edit);

        var c = Contents(archive);
        Assert.Equal("replaced", c["readme.txt"]);
        Assert.Single(c.Keys, k => k == "readme.txt");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task Removes_a_file_and_a_whole_folder(string name)
    {
        var archive = await MakeArchive(name);
        var edit = new ArchiveEdit();
        edit.Removes.Add("readme.txt");
        edit.Removes.Add("docs");
        ArchiveEditor.Apply(archive, edit);

        Assert.Equal(new[] { "empty" }, Contents(archive).Keys);
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task Renames_files_and_folders_including_a_swap(string name)
    {
        var archive = await MakeArchive(name);
        var edit = new ArchiveEdit();
        edit.Renames["docs"] = "documents";             // a folder: its contents follow
        edit.Renames["readme.txt"] = "empty-was";       // swap names with the empty folder…
        edit.Renames["empty"] = "readme-was";
        ArchiveEditor.Apply(archive, edit);

        var c = Contents(archive);
        Assert.Equal("A", c["documents/a.md"]);
        Assert.Equal("B", c["documents/sub/b.md"]);
        Assert.Equal("readme", c["empty-was"]);
        Assert.Equal("/", c["readme-was"]);
        Assert.DoesNotContain(c.Keys, k => k.StartsWith("docs"));
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task Dori_reads_the_changed_archive(string name)
    {
        var archive = await MakeArchive(name);
        var provider = (ArchiveFileProvider)ProviderRegistry.For(ArchivePath.RootUriFor(archive));
        Assert.True(provider.FileExists(Uri(archive, "readme.txt")));   // cache the listing

        var edit = new ArchiveEdit();
        edit.Renames["readme.txt"] = "READ.txt";
        ArchiveEditor.Apply(archive, edit);

        Assert.False(provider.FileExists(Uri(archive, "readme.txt")));  // not served from a stale cache
        Assert.True(provider.FileExists(Uri(archive, "READ.txt")));
        using var r = new StreamReader(provider.OpenRead(Uri(archive, "READ.txt")));
        Assert.Equal("readme", r.ReadToEnd());
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task A_cancelled_edit_leaves_the_archive_exactly_as_it_was(string name)
    {
        var archive = await MakeArchive(name);
        var before = File.ReadAllBytes(archive);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var edit = new ArchiveEdit();
        edit.Removes.Add("docs");
        Assert.ThrowsAny<OperationCanceledException>(() => ArchiveEditor.Apply(archive, edit, ct: cts.Token));

        Assert.Equal(before, File.ReadAllBytes(archive));
        Assert.Single(Directory.GetFiles(_t.Path, "*" + Path.GetExtension(name)).Where(f => f.Contains(".dori-edit-")).DefaultIfEmpty(), f => f == null);
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task Provider_write_methods_work(string name)
    {
        var archive = await MakeArchive(name);
        var p = ProviderRegistry.For(ArchivePath.RootUriFor(archive));

        using (var w = new StreamWriter(p.OpenWrite(Uri(archive, "docs/written.txt")))) w.Write("written");
        p.CreateDirectory(Uri(archive, "made"));
        p.MoveFile(Uri(archive, "docs/a.md"), Uri(archive, "made/a.md"));
        p.DeleteDirectory(Uri(archive, "docs/sub"), recursive: true);

        var c = Contents(archive);
        Assert.Equal("written", c["docs/written.txt"]);
        Assert.Equal("A", c["made/a.md"]);
        Assert.False(c.ContainsKey("docs/a.md"));
        Assert.False(c.ContainsKey("docs/sub/b.md"));
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task The_normal_copy_pipeline_can_copy_into_an_archive(string name)
    {
        var archive = await MakeArchive(name);
        var file = _t.File("in.txt", "copied in");
        var dest = Uri(archive, "docs");
        var items = new List<TransferItem> { new(file, dest, Uri(archive, "docs/in.txt"), false) };
        await TransferCoordinator.RunAsync(items, move: false, progress: null, CancellationToken.None);

        Assert.Equal("copied in", Contents(archive)["docs/in.txt"]);
    }

    [Fact]
    public void Formats_that_cannot_be_written_say_so()
    {
        Assert.True(ArchiveEditor.CanWrite("/x/a.zip"));
        Assert.True(ArchiveEditor.CanWrite("/x/a.TGZ"));
        Assert.False(ArchiveEditor.CanWrite("/x/a.7z"));
        Assert.False(ArchiveEditor.CanWrite("/x/a.rar"));
        Assert.Contains(".7z", ArchiveEditor.WhyReadOnly("/x/a.7z"));
        Assert.Null(ArchiveEditor.WhyReadOnly("/x/a.tar"));
        var p = new ArchiveFileProvider();
        Assert.Throws<NotSupportedException>(() => p.DeleteFile(Uri("/x/a.7z", "f.txt")));
    }

    [Fact]
    public void Folder_mapping_follows_the_longest_matching_rename_and_removes_win()
    {
        var edit = new ArchiveEdit();
        edit.Renames["a"] = "x";
        edit.Renames["a/b"] = "y";
        edit.Removes.Add("gone");
        var none = new HashSet<string>();
        Assert.Equal("x/file", ArchiveEditor.MapKey("a/file", edit, none));
        Assert.Equal("y/c", ArchiveEditor.MapKey("a/b/c", edit, none));
        Assert.Equal("ab", ArchiveEditor.MapKey("ab", edit, none));            // not inside "a"
        Assert.Null(ArchiveEditor.MapKey("gone/x", edit, none));
        Assert.Null(ArchiveEditor.MapKey("x/file2", edit, new HashSet<string> { "x/file2" }));   // replaced by an add
    }

    [Fact]
    public void Planning_a_folder_skips_linked_folders_inside()
    {
        _t.File("p/real.txt", "r");
        try { Directory.CreateSymbolicLink(_t.Combine("p/loop"), _t.Combine("p")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        var adds = ArchiveEditor.PlanAdds(new[] { (_t.Combine("p"), "p", true) });
        Assert.Equal(new[] { "p", "p/real.txt" }, adds.Select(a => a.Entry).OrderBy(x => x));
    }
}
