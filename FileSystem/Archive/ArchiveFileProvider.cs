using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>
/// <see cref="IFileProvider"/> that lets the app browse into archive files
/// (<c>archive://&lt;file&gt;!/&lt;entry&gt;</c>) and extract their contents through the normal
/// transfer pipeline. Reading is delegated to SharpCompress, so ZIP, 7z, RAR, TAR and the common
/// gzip/bzip2 tarballs are all listable. .zip, .tar, .tar.gz and .tgz archives can also be changed
/// (see <see cref="ArchiveEditor"/>); the others are read-only and a write throws a clear error.
/// <para>Each write method here is one full rewrite of the archive. Bulk operations (copying many
/// files in, deleting, renaming) batch their changes into one <see cref="ArchiveEdit"/> instead.</para>
/// </summary>
public sealed class ArchiveFileProvider : IFileProvider
{
    public bool CanHandle(string path) => ArchivePath.IsArchiveUri(path);

    // The archive lives on local disk; keep synchronous UI behaviour (counts, no network dispatch).
    public bool IsRemote => false;

    // ---- metadata cache: parsed entry list per archive, invalidated on the file's mtime ----

    private sealed record CachedEntry(string Key, bool IsDir, long Size, DateTime Modified);

    /// <summary>One entry in an archive, as reported by <see cref="ListEntries"/>.</summary>
    public readonly record struct ArchiveEntry(string Key, bool IsDirectory, long Size, DateTime Modified);

    /// <summary>
    /// Every entry in the archive at <paramref name="archiveFsPath"/>, flat, with '/' separators.
    ///
    /// <para>Exposed for callers that want to describe an archive rather than browse it — the
    /// preview layer, in particular. Goes through the same reader as browsing, so it inherits both
    /// the random-access/streaming fallback (compression-wrapped tarballs need the latter) and the
    /// mtime-keyed cache, which means previewing an archive the user then opens costs one read.</para>
    /// </summary>
    public IReadOnlyList<ArchiveEntry> ListEntries(string archiveFsPath) =>
        ReadEntries(archiveFsPath)
            .Select(e => new ArchiveEntry(e.Key, e.IsDir, e.Size, e.Modified))
            .ToList();

    // One provider instance serves every thread (browsing, previews, search, transfers, archive
    // creation), so the cache must be safe for concurrent use. The cached lists are never changed
    // after they're stored, so readers can share them.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, List<CachedEntry> Entries)> _cache = new();

    /// <summary>Drops the cached listing of an archive, after it has been rewritten.</summary>
    internal void Forget(string archiveFsPath) => _cache.TryRemove(archiveFsPath, out _);

    /// <summary>
    /// Normalizes an archive entry key to forward slashes with no leading "./" or surrounding
    /// slashes (tarballs commonly prefix entries with "./"). Returns "" for the archive-root entry.
    /// </summary>
    private static string NormalizeKey(string rawKey)
    {
        var key = rawKey.Replace('\\', '/').Trim('/');
        while (key.StartsWith("./", StringComparison.Ordinal))
            key = key.Substring(2);
        if (key == ".") key = "";
        return key;
    }

    /// <summary>Reads (and caches) the archive's flat entry list, normalizing keys to '/' separators.</summary>
    private List<CachedEntry> ReadEntries(string archiveFsPath)
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(archiveFsPath); }
        catch { stamp = DateTime.MinValue; }

        if (_cache.TryGetValue(archiveFsPath, out var hit) && hit.Stamp == stamp)
            return hit.Entries;

        var list = new List<CachedEntry>();
        try
        {
            // Random-access path: ZIP, 7z, RAR, plain TAR, and single-member gzip/bzip2.
            using var archive = ArchiveFactory.OpenArchive(archiveFsPath);
            foreach (var e in archive.Entries)
            {
                if (e.Key == null) continue;
                var key = NormalizeKey(e.Key);
                if (key.Length == 0) continue;
                list.Add(new CachedEntry(key, e.IsDirectory, e.IsDirectory ? 0 : e.Size,
                    e.LastModifiedTime ?? DateTime.MinValue));
            }
        }
        catch
        {
            // Streaming fallback: compression-wrapped tarballs (.tar.gz/.tgz/.tar.bz2) that the
            // random-access factory can't open. Read forward once to collect entry metadata.
            list.Clear();
            using var reader = ReaderFactory.OpenReader(archiveFsPath);
            while (reader.MoveToNextEntry())
            {
                var e = reader.Entry;
                if (e?.Key == null) continue;
                var key = NormalizeKey(e.Key);
                if (key.Length == 0) continue;
                list.Add(new CachedEntry(key, e.IsDirectory, e.IsDirectory ? 0 : e.Size,
                    e.LastModifiedTime ?? DateTime.MinValue));
            }
        }

        _cache[archiveFsPath] = (stamp, list);
        return list;
    }

    /// <summary>
    /// Yields the immediate children (files or dirs) of <paramref name="entryPath"/> within the
    /// archive, synthesizing directories that exist only implicitly in entry key prefixes.
    /// </summary>
    private IEnumerable<FileEntry> ListChildren(string archiveFsPath, string entryPath, bool wantDirs)
    {
        var entries = ReadEntries(archiveFsPath);
        string prefix = string.IsNullOrEmpty(entryPath) ? "" : entryPath.TrimEnd('/') + "/";

        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileEntry>();
        var dirs = new List<FileEntry>();

        foreach (var e in entries)
        {
            if (prefix.Length > 0 && !e.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var remainder = e.Key.Substring(prefix.Length);
            if (remainder.Length == 0) continue;

            int slash = remainder.IndexOf('/');
            if (slash < 0)
            {
                // Direct child.
                string childUri = new ArchivePath(archiveFsPath, prefix + remainder).FullUri;
                if (e.IsDir)
                {
                    if (seenDirs.Add(remainder))
                        dirs.Add(new FileEntry(childUri, remainder, true, 0, e.Modified, FileEntryFlags.None, "D"));
                }
                else
                {
                    files.Add(new FileEntry(childUri, remainder, false, e.Size, e.Modified, FileEntryFlags.None, ""));
                }
            }
            else
            {
                // Deeper entry implies an intermediate directory at this level.
                string dirName = remainder.Substring(0, slash);
                if (seenDirs.Add(dirName))
                {
                    string childUri = new ArchivePath(archiveFsPath, prefix + dirName).FullUri;
                    dirs.Add(new FileEntry(childUri, dirName, true, 0, DateTime.MinValue, FileEntryFlags.None, "D"));
                }
            }
        }

        return wantDirs ? dirs : files;
    }

    public IEnumerable<FileEntry> EnumerateDirectories(string path)
    {
        var ap = ArchivePath.Parse(path);
        return ListChildren(ap.ArchiveFsPath, ap.EntryPath, wantDirs: true);
    }

    public IEnumerable<FileEntry> EnumerateFiles(string path)
    {
        var ap = ArchivePath.Parse(path);
        return ListChildren(ap.ArchiveFsPath, ap.EntryPath, wantDirs: false);
    }

    public bool DirectoryExists(string path)
    {
        if (!ArchivePath.IsArchiveUri(path)) return false;
        var ap = ArchivePath.Parse(path);
        if (ap.IsRoot) return File.Exists(ap.ArchiveFsPath);   // the archive root is a "directory"
        string prefix = ap.EntryPath.TrimEnd('/') + "/";
        return ReadEntries(ap.ArchiveFsPath).Any(e =>
            (e.IsDir && string.Equals(e.Key, ap.EntryPath, StringComparison.Ordinal)) ||
            e.Key.StartsWith(prefix, StringComparison.Ordinal));
    }

    public bool FileExists(string path)
    {
        if (!ArchivePath.IsArchiveUri(path)) return false;
        var ap = ArchivePath.Parse(path);
        if (ap.IsRoot) return false;
        return ReadEntries(ap.ArchiveFsPath)
            .Any(e => !e.IsDir && string.Equals(e.Key, ap.EntryPath, StringComparison.Ordinal));
    }

    public FileEntry? Stat(string path)
    {
        var ap = ArchivePath.Parse(path);
        if (ap.IsRoot)
            return new FileEntry(path, Path.GetFileName(ap.ArchiveFsPath), true, 0, DateTime.MinValue, FileEntryFlags.None, "D");

        var match = ReadEntries(ap.ArchiveFsPath)
            .FirstOrDefault(e => string.Equals(e.Key, ap.EntryPath, StringComparison.Ordinal));
        if (match != null)
        {
            string name = ap.EntryPath.TrimEnd('/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            return new FileEntry(path, name, match.IsDir, match.Size, match.Modified, FileEntryFlags.None, match.IsDir ? "D" : "");
        }

        // Implicit directory (present only as a prefix of deeper entries).
        return DirectoryExists(path)
            ? new FileEntry(path, Path.GetFileName(ap.EntryPath.TrimEnd('/')), true, 0, DateTime.MinValue, FileEntryFlags.None, "D")
            : null;
    }

    public Stream OpenRead(string path)
    {
        var ap = ArchivePath.Parse(path);

        try
        {
            // Random-access path: find the entry directly and stream it, keeping the archive alive.
            var archive = ArchiveFactory.OpenArchive(ap.ArchiveFsPath);
            var entry = archive.Entries.FirstOrDefault(e =>
                e.Key != null && !e.IsDirectory &&
                string.Equals(NormalizeKey(e.Key), ap.EntryPath, StringComparison.Ordinal));

            if (entry != null)
                return new OwningStream(entry.OpenEntryStream(), archive);

            archive.Dispose();
            throw new FileNotFoundException($"Entry not found in archive: {ap.EntryPath}");
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch
        {
            // Streaming fallback for compression-wrapped tarballs: scan forward to the entry.
            var reader = ReaderFactory.OpenReader(ap.ArchiveFsPath);
            while (reader.MoveToNextEntry())
            {
                var e = reader.Entry;
                if (e?.Key == null || e.IsDirectory) continue;
                if (string.Equals(NormalizeKey(e.Key), ap.EntryPath, StringComparison.Ordinal))
                    return new OwningStream(reader.OpenEntryStream(), reader);
            }
            reader.Dispose();
            throw new FileNotFoundException($"Entry not found in archive: {ap.EntryPath}");
        }
    }

    public long GetDirectorySize(string path, bool recursive)
    {
        var ap = ArchivePath.Parse(path);
        string prefix = ap.IsRoot ? "" : ap.EntryPath.TrimEnd('/') + "/";
        return ReadEntries(ap.ArchiveFsPath)
            .Where(e => !e.IsDir && (prefix.Length == 0 || e.Key.StartsWith(prefix, StringComparison.Ordinal)))
            .Sum(e => e.Size);
    }

    // ---- writing (zip / tar / tar.gz only; each call is one rewrite of the archive) ----

    private static NotSupportedException ReadOnly()
        => new("Archives are read-only. Extract the files first, then modify them.");

    private static ArchivePath Writable(string path)
    {
        var ap = ArchivePath.Parse(path);
        if (ArchiveEditor.WhyReadOnly(ap.ArchiveFsPath) is { } why) throw new NotSupportedException(why);
        return ap;
    }

    /// <summary>
    /// A stream that becomes the entry at <paramref name="path"/> when it is closed. The bytes are
    /// held in a temporary file meanwhile, then added to the archive in one rewrite.
    /// </summary>
    public Stream OpenWrite(string path)
    {
        var ap = Writable(path);
        if (ap.IsRoot) throw new IOException("Can't write over the archive itself.");
        return new CommitOnClose(ap);
    }

    public void CreateDirectory(string path)
    {
        var ap = Writable(path);
        if (ap.IsRoot || DirectoryExists(path)) return;   // nothing to do (callers create parents freely)
        var edit = new ArchiveEdit();
        edit.Adds.Add(new ArchiveAdd(ap.EntryPath, null, true, DateTime.Now));
        ArchiveEditor.Apply(ap.ArchiveFsPath, edit);
    }

    public void DeleteFile(string path) => Remove(path);
    public void DeleteDirectory(string path, bool recursive) => Remove(path);

    private void Remove(string path)
    {
        var ap = Writable(path);
        if (ap.IsRoot) throw new IOException("Delete the archive file itself from its folder instead.");
        var edit = new ArchiveEdit();
        edit.Removes.Add(ap.EntryPath);
        ArchiveEditor.Apply(ap.ArchiveFsPath, edit);
    }

    public void CopyFile(string src, string dst, bool overwrite)
    {
        var to = Writable(dst);
        if (!overwrite && FileExists(dst)) throw new IOException($"\"{to.EntryPath}\" is already in the archive.");
        var edit = new ArchiveEdit();
        edit.Adds.Add(new ArchiveAdd(to.EntryPath, src, false, Stat(src)?.LastModified ?? DateTime.Now));
        ArchiveEditor.Apply(to.ArchiveFsPath, edit);
    }

    public void MoveFile(string src, string dst) => Move(src, dst);
    public void MoveDirectory(string src, string dst) => Move(src, dst);

    /// <summary>Within one archive, a rename. Between two archives, copy across, then remove.</summary>
    private void Move(string src, string dst)
    {
        var from = Writable(src);
        var to = Writable(dst);
        if (from.IsRoot || to.IsRoot) throw new IOException("Can't move the archive's root.");

        if (string.Equals(Path.GetFullPath(from.ArchiveFsPath), Path.GetFullPath(to.ArchiveFsPath), StringComparison.Ordinal))
        {
            var rename = new ArchiveEdit();
            rename.Renames[from.EntryPath] = to.EntryPath;
            ArchiveEditor.Apply(from.ArchiveFsPath, rename);
            return;
        }

        var add = new ArchiveEdit();
        bool isDir = DirectoryExists(src) && !FileExists(src);
        add.Adds.AddRange(ArchiveEditor.PlanAdds(new[] { (src, to.EntryPath, isDir) }));
        ArchiveEditor.Apply(to.ArchiveFsPath, add);
        Remove(src);
    }

    /// <summary>Buffers written bytes in a temp file and adds them to the archive on close.</summary>
    private sealed class CommitOnClose : FileStream
    {
        private readonly ArchivePath _target;
        private bool _committed;

        public CommitOnClose(ArchivePath target)
            : base(Path.Combine(Path.GetTempPath(), $"dori-entry-{Guid.NewGuid():N}"), FileMode.CreateNew,
                   FileAccess.Write, FileShare.None, 81920)
            => _target = target;

        protected override void Dispose(bool disposing)
        {
            if (!disposing || _committed)
            {
                base.Dispose(disposing);
                return;
            }

            _committed = true;
            string temp = Name;
            base.Dispose(true);   // close it first: Windows won't let it be read while open for writing
            try
            {
                var edit = new ArchiveEdit();
                edit.Adds.Add(new ArchiveAdd(_target.EntryPath, temp, false, DateTime.Now));
                ArchiveEditor.Apply(_target.ArchiveFsPath, edit);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* temp folder; best effort */ }
            }
        }
    }

    /// <summary>Wraps an entry stream and disposes the owning archive when the stream is closed.</summary>
    private sealed class OwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly IDisposable _owner;

        public OwningStream(Stream inner, IDisposable owner) { _inner = inner; _owner = owner; }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw ReadOnly();
        public override void Write(byte[] buffer, int offset, int count) => throw ReadOnly();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
