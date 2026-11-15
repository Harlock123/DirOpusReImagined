using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>A file or folder to put into an archive.</summary>
/// <param name="Entry">Where it goes in the archive, '/'-separated, e.g. "docs/report.pdf".</param>
/// <param name="Source">The file to copy in (any provider), or null for an empty folder entry.</param>
public sealed record ArchiveAdd(string Entry, string? Source, bool IsDirectory, DateTime Modified);

/// <summary>
/// A batch of changes to one archive, applied together by <see cref="ArchiveEditor.Apply"/> in a
/// single rewrite. Renames and removals name an entry or a folder (which takes everything in it).
/// Renames are applied simultaneously, so swaps (a→b, b→a) work.
/// </summary>
public sealed class ArchiveEdit
{
    public List<ArchiveAdd> Adds { get; } = new();
    public List<string> Removes { get; } = new();
    public Dictionary<string, string> Renames { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Adds.Count == 0 && Removes.Count == 0 && Renames.Count == 0;
}

/// <summary>
/// Changes the contents of .zip, .tar, .tar.gz and .tgz archives. Every change rewrites the
/// archive: the new one is written to a temporary file beside it, and only when that is complete
/// does it replace the original, so a failure or cancel never damages the archive. Batch changes
/// into one <see cref="ArchiveEdit"/> where possible — each <see cref="Apply"/> is a full rewrite.
/// 7z and RAR (and other formats) can be read but not written.
/// </summary>
public static class ArchiveEditor
{
    private enum Format { Zip, Tar, TarGz }

    private const int BufferSize = 81920;
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.Ordinal);

    private static Format? FormatOf(string archiveFsPath)
    {
        var n = archiveFsPath.ToLowerInvariant();
        if (n.EndsWith(".zip")) return Format.Zip;
        if (n.EndsWith(".tar.gz") || n.EndsWith(".tgz")) return Format.TarGz;
        if (n.EndsWith(".tar")) return Format.Tar;
        return null;
    }

    /// <summary>True if DORI can change this archive's contents.</summary>
    public static bool CanWrite(string archiveFsPath) => FormatOf(archiveFsPath) != null;

    /// <summary>Why an archive can't be changed, for messages; null if it can.</summary>
    public static string? WhyReadOnly(string archiveFsPath)
    {
        if (CanWrite(archiveFsPath)) return null;
        string ext = Path.GetExtension(archiveFsPath);
        return $"DORI can read {ext} archives but not change them — only .zip, .tar, .tar.gz and .tgz archives " +
               "can be written to. Extract the files to a normal folder first, then modify them there.";
    }

    /// <summary>The key DORI uses for an entry name: '/' separators, no leading "./" or slashes.</summary>
    public static string NormalizeKey(string rawKey)
    {
        var key = rawKey.Replace('\\', '/').Trim('/');
        while (key.StartsWith("./", StringComparison.Ordinal)) key = key[2..];
        if (key == ".") key = "";
        return key;
    }

    /// <summary>
    /// What happens to an existing entry: its new key, or null if it is removed (or replaced by an
    /// add with the same key). Folders carry their contents: renaming or removing "docs" also
    /// renames or removes "docs/a.txt".
    /// </summary>
    internal static string? MapKey(string key, ArchiveEdit edit, ISet<string> addedFiles)
    {
        foreach (var r in edit.Removes)
        {
            string rk = NormalizeKey(r);
            if (key == rk || key.StartsWith(rk + "/", StringComparison.Ordinal)) return null;
        }

        string result = key;
        if (edit.Renames.TryGetValue(key, out var exact))
        {
            result = NormalizeKey(exact);
        }
        else
        {
            // The longest folder rename that contains this entry.
            var folder = edit.Renames
                .Select(kv => (Old: NormalizeKey(kv.Key), New: NormalizeKey(kv.Value)))
                .Where(kv => key.StartsWith(kv.Old + "/", StringComparison.Ordinal))
                .OrderByDescending(kv => kv.Old.Length)
                .FirstOrDefault();
            if (folder.Old != null) result = folder.New + key[folder.Old.Length..];
        }

        // An added file with the same name replaces this one.
        return addedFiles.Contains(result) ? null : result;
    }

    /// <summary>
    /// Applies the changes in one rewrite. Throws (leaving the archive untouched) if the format
    /// can't be written, an entry can't be read, or the operation is cancelled.
    /// </summary>
    public static void Apply(string archiveFsPath, ArchiveEdit edit,
                             IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        if (edit.IsEmpty) return;
        var format = FormatOf(archiveFsPath) ?? throw new NotSupportedException(WhyReadOnly(archiveFsPath));

        lock (Locks.GetOrAdd(Path.GetFullPath(archiveFsPath), _ => new object()))
        {
            // Later adds of the same entry win; folders are kept once.
            var adds = edit.Adds
                .Select(a => a with { Entry = NormalizeKey(a.Entry) })
                .Where(a => a.Entry.Length > 0)
                .GroupBy(a => a.Entry, StringComparer.Ordinal)
                .Select(g => g.Last())
                .ToList();
            var addedFiles = new HashSet<string>(adds.Where(a => !a.IsDirectory).Select(a => a.Entry), StringComparer.Ordinal);
            var addedDirs = new HashSet<string>(adds.Where(a => a.IsDirectory).Select(a => a.Entry), StringComparer.Ordinal);

            string dir = Path.GetDirectoryName(Path.GetFullPath(archiveFsPath)) ?? ".";
            string temp = Path.Combine(dir, $".{Path.GetFileName(archiveFsPath)}.dori-edit-{Guid.NewGuid():N}.tmp");
            var counter = new Counter(progress, TotalBytes(archiveFsPath, adds), adds.Count);
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                {
                    if (format == Format.Zip)
                        RewriteZip(archiveFsPath, output, edit, adds, addedFiles, addedDirs, counter, ct);
                    else
                        RewriteTar(archiveFsPath, output, format == Format.TarGz, edit, adds, addedFiles, addedDirs, counter, ct);
                }

                ct.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(temp, File.GetUnixFileMode(archiveFsPath)); } catch { /* keep the default */ }
                }
                File.Move(temp, archiveFsPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            }

            // The listing cache is keyed on the file's time, which a quick rewrite might not change.
            (ProviderRegistry.For(ArchivePath.RootUriFor(archiveFsPath)) as ArchiveFileProvider)?.Forget(archiveFsPath);
        }
    }

    private static long TotalBytes(string archiveFsPath, List<ArchiveAdd> adds)
    {
        long total = 0;
        foreach (var a in adds)
        {
            if (a.Source == null) continue;
            try { total += ProviderRegistry.For(a.Source).Stat(a.Source)?.Size ?? 0; } catch { }
        }
        try { total += new FileInfo(archiveFsPath).Length; } catch { }
        return total;
    }

    // ---------------- zip ----------------

    private static void RewriteZip(string path, Stream output, ArchiveEdit edit, List<ArchiveAdd> adds,
                                   ISet<string> addedFiles, ISet<string> addedDirs, Counter counter, CancellationToken ct)
    {
        using var source = ZipFile.OpenRead(path);
        using var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in source.Entries)
        {
            ct.ThrowIfCancellationRequested();
            bool isDir = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            string key = NormalizeKey(entry.FullName);
            if (key.Length == 0) continue;

            string? newKey = MapKey(key, edit, addedFiles);
            if (newKey == null || newKey.Length == 0) continue;
            if (isDir && addedDirs.Contains(newKey)) continue;   // the add writes it
            // Unchanged entries keep their exact original name.
            string name = newKey == key ? entry.FullName : newKey + (isDir ? "/" : "");
            if (!written.Add(name)) continue;

            var copy = target.CreateEntry(name, isDir ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            copy.LastWriteTime = entry.LastWriteTime;
            copy.ExternalAttributes = entry.ExternalAttributes;
            if (isDir) continue;

            counter.Start(newKey);
            Stream data;
            try { data = entry.Open(); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                throw new IOException($"\"{key}\" in the archive can't be read ({ex.Message}). Encrypted archives can't be changed.", ex);
            }
            using (data)
            using (var dst = copy.Open())
                Pump(data, dst, counter, ct);
        }

        foreach (var add in adds)
        {
            ct.ThrowIfCancellationRequested();
            if (add.IsDirectory)
            {
                if (!written.Add(add.Entry + "/")) continue;
                target.CreateEntry(add.Entry + "/", CompressionLevel.NoCompression).LastWriteTime = ZipDate(add.Modified);
                continue;
            }

            var entry = target.CreateEntry(add.Entry, CompressionLevel.Optimal);
            entry.LastWriteTime = ZipDate(add.Modified);
            if (add.Source != null && ProviderRegistry.For(add.Source) is LocalFileProvider && !OperatingSystem.IsWindows())
            {
                const int S_IFREG = 0x8000;
                try { entry.ExternalAttributes = (S_IFREG | (int)File.GetUnixFileMode(add.Source)) << 16; } catch { }
            }
            counter.Start(add.Entry);
            using (var src = OpenSource(add))
            using (var dst = entry.Open())
                Pump(src, dst, counter, ct);
            counter.FileDone();
        }
    }

    // ---------------- tar / tar.gz ----------------

    private static void RewriteTar(string path, Stream output, bool gz, ArchiveEdit edit, List<ArchiveAdd> adds,
                                   ISet<string> addedFiles, ISet<string> addedDirs, Counter counter, CancellationToken ct)
    {
        // .NET can't copy an entry from a stream it can't seek into one it can't seek, and both
        // ends of a .tar.gz are gzip streams. So a .tar.gz is unpacked to a plain temporary .tar
        // first, which makes the entries being copied seekable.
        using Stream input = gz ? Decompressed(path, ct) : File.OpenRead(path);
        using var reader = new TarReader(input);

        using Stream outStream = gz ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true) : output;
        using var writer = new TarWriter(outStream, TarEntryFormat.Pax, leaveOpen: true);
        var written = new HashSet<string>(StringComparer.Ordinal);

        while (reader.GetNextEntry() is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            bool isDir = entry.EntryType == TarEntryType.Directory;
            string key = NormalizeKey(entry.Name);
            if (key.Length == 0) continue;

            string? newKey = MapKey(key, edit, addedFiles);
            if (newKey == null || newKey.Length == 0) continue;
            if (isDir && addedDirs.Contains(newKey)) continue;
            if (newKey != key) entry.Name = newKey + (isDir ? "/" : "");
            if (!written.Add(NormalizeKey(entry.Name))) continue;

            counter.Start(newKey);
            // Written as read (the data stream can't be swapped: setting it disposes the old one).
            writer.WriteEntry(entry);
            counter.Add(entry.Length);
        }

        foreach (var add in adds)
        {
            ct.ThrowIfCancellationRequested();
            if (add.IsDirectory)
            {
                if (!written.Add(add.Entry)) continue;
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, add.Entry + "/")
                {
                    ModificationTime = Offset(add.Modified),
                    Mode = (UnixFileMode)0b111_101_101,
                });
                continue;
            }

            counter.Start(add.Entry);
            using var src = OpenSource(add);
            // A tar header needs the length up front; spool anything that can't tell us.
            Stream data = src;
            FileStream? spool = null;
            if (!src.CanSeek)
            {
                spool = new FileStream(Path.Combine(Path.GetTempPath(), $"dori-spool-{Guid.NewGuid():N}"),
                                       FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.DeleteOnClose);
                Pump(src, spool, counter, ct);
                spool.Position = 0;
                data = spool;
            }
            try
            {
                var mode = (UnixFileMode)0b110_100_100;
                if (add.Source != null && ProviderRegistry.For(add.Source) is LocalFileProvider && !OperatingSystem.IsWindows())
                {
                    try { mode = File.GetUnixFileMode(add.Source); } catch { }
                }
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, add.Entry)
                {
                    ModificationTime = Offset(add.Modified),
                    Mode = mode,
                    DataStream = spool == null ? new CountingStream(data, counter, ct) : data,
                });
            }
            finally
            {
                spool?.Dispose();
            }
            counter.FileDone();
        }
    }

    // ---------------- planning ----------------

    /// <summary>
    /// The adds that put <paramref name="sources"/> into the archive at their entry paths. A folder
    /// brings everything in it (empty folders and hidden files included). Linked folders inside
    /// aren't followed (they may loop); a file link brings the file it points to, and a broken one
    /// is left out.
    /// </summary>
    public static List<ArchiveAdd> PlanAdds(IEnumerable<(string Source, string Entry, bool IsDirectory)> sources,
                                            CancellationToken ct = default)
    {
        var adds = new List<ArchiveAdd>();
        foreach (var (source, entry, isDir) in sources)
        {
            ct.ThrowIfCancellationRequested();
            var provider = ProviderRegistry.For(source);
            if (!isDir)
            {
                var stat = provider.Stat(source) ?? throw new FileNotFoundException($"\"{source}\" no longer exists.");
                adds.Add(new ArchiveAdd(entry, source, false, stat.LastModified));
                continue;
            }
            AddFolder(provider, source, NormalizeKey(entry), adds, ct);
        }
        return adds;
    }

    private static void AddFolder(IFileProvider provider, string path, string entry, List<ArchiveAdd> adds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DateTime modified = DateTime.Now;
        try { modified = provider.Stat(path)?.LastModified ?? modified; } catch { }
        adds.Add(new ArchiveAdd(entry, null, true, modified));

        foreach (var f in provider.EnumerateFiles(path))
        {
            if (f.IsLink && LocalFileProvider.ResolveLink(f.Path) is { } t && !File.Exists(t)) continue;   // broken link
            adds.Add(new ArchiveAdd(entry + "/" + f.Name, f.Path, false, f.LastModified));
        }
        foreach (var d in provider.EnumerateDirectories(path))
        {
            if (d.IsLink) continue;
            AddFolder(provider, d.Path, entry + "/" + d.Name, adds, ct);
        }
    }

    // ---------------- helpers ----------------

    /// <summary>A .tar.gz unpacked into a temporary .tar that deletes itself when closed.</summary>
    private static FileStream Decompressed(string path, CancellationToken ct)
    {
        var tar = new FileStream(Path.Combine(Path.GetTempPath(), $"dori-untar-{Guid.NewGuid():N}.tar"),
                                 FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.DeleteOnClose);
        try
        {
            using (var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
            {
                var buffer = new byte[BufferSize];
                int n;
                while ((n = gz.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    tar.Write(buffer, 0, n);
                }
            }
            tar.Position = 0;
            return tar;
        }
        catch
        {
            tar.Dispose();
            throw;
        }
    }

    private static Stream OpenSource(ArchiveAdd add)
        => add.Source == null ? Stream.Null : ProviderRegistry.For(add.Source).OpenRead(add.Source);

    private static void Pump(Stream src, Stream dst, Counter counter, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        int n;
        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            dst.Write(buffer, 0, n);
            counter.Add(n);
        }
    }

    private static DateTimeOffset Offset(DateTime t)
    {
        if (t.Year < 1971) return DateTimeOffset.UnixEpoch;
        return t.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Local)) : new DateTimeOffset(t);
    }

    private static DateTimeOffset ZipDate(DateTime t)
    {
        var o = Offset(t);
        var min = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var max = new DateTimeOffset(2107, 12, 31, 0, 0, 0, TimeSpan.Zero);
        return o < min ? min : o > max ? max : o;
    }

    /// <summary>Reports progress as bytes go by, a few times a second at most.</summary>
    private sealed class Counter(IProgress<TransferProgress>? progress, long total, int files)
    {
        private string _current = "";
        private long _done, _sinceReport;
        private int _filesDone;

        public void Start(string name) { _current = name; Report(); }
        public void FileDone() { _filesDone++; Report(); }

        public void Add(long n)
        {
            _done += n;
            _sinceReport += n;
            if (_sinceReport >= 256 * 1024) Report();
        }

        private void Report()
        {
            _sinceReport = 0;
            progress?.Report(new TransferProgress(_current, Math.Min(_filesDone + 1, Math.Max(files, 1)), files,
                                                  Math.Min(_done, Math.Max(total, _done)), Math.Max(total, _done), 0));
        }
    }

    private sealed class CountingStream(Stream inner, Counter counter, CancellationToken ct) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ct.ThrowIfCancellationRequested();
            int n = inner.Read(buffer, offset, count);
            counter.Add(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            int n = inner.Read(buffer);
            counter.Add(n);
            return n;
        }
    }
}
