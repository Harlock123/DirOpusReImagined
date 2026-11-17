using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>The kinds of archive DORI can write.</summary>
public enum ArchiveFormat
{
    Zip,
    TarGz,
    Tar,
    /// <summary>A tar compressed with Zstandard: fast, and smaller than gzip.</summary>
    TarZst,
    /// <summary>A tar compressed with xz (made by the <c>xz</c> program, when installed): the smallest of the tars.</summary>
    TarXz,
    /// <summary>A 7-Zip archive, made by the <c>7z</c> program when installed; it can have a password.</summary>
    SevenZip,
}

/// <summary>Progress while an archive is written. Bytes are uncompressed source bytes.</summary>
public sealed record ArchiveProgress(string CurrentEntry, long BytesDone, long BytesTotal, int FilesDone, int FilesTotal);

/// <summary>An item that couldn't be added, and why.</summary>
public sealed record ArchiveSkip(string Path, string Reason);

/// <summary>What was written.</summary>
public sealed record ArchiveResult(string ArchivePath, int Files, int Folders, long Bytes, IReadOnlyList<ArchiveSkip> Skipped);

/// <summary>
/// Writes a .zip, .tar.gz or .tar from items in a panel. Sources are read through
/// <see cref="ProviderRegistry"/>, so they can be local, inside an archive, or in the cloud. The
/// archive is built in a temp file and only moved (or uploaded) to its final name once complete,
/// so a failed or cancelled run never leaves a half-written archive behind.
/// </summary>
public static class ArchiveCreator
{
    private const int BufferSize = 81920;
    private const long ReportEveryBytes = 256 * 1024;

    // Zip timestamps are MS-DOS dates: 1980-01-01 to 2107-12-31.
    private static readonly DateTimeOffset ZipMinDate = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ZipMaxDate = new(2107, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public static string Extension(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => ".zip",
        ArchiveFormat.TarGz => ".tar.gz",
        ArchiveFormat.Tar => ".tar",
        ArchiveFormat.TarZst => ".tar.zst",
        ArchiveFormat.TarXz => ".tar.xz",
        ArchiveFormat.SevenZip => ".7z",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>The format a file name's extension implies, or null if it isn't one DORI writes.</summary>
    public static ArchiveFormat? FormatFromName(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n.EndsWith(".tar.gz") || n.EndsWith(".tgz")) return ArchiveFormat.TarGz;
        if (n.EndsWith(".tar.zst") || n.EndsWith(".tzst")) return ArchiveFormat.TarZst;
        if (n.EndsWith(".tar.xz") || n.EndsWith(".txz")) return ArchiveFormat.TarXz;
        if (n.EndsWith(".7z")) return ArchiveFormat.SevenZip;
        if (n.EndsWith(".tar")) return ArchiveFormat.Tar;
        if (n.EndsWith(".zip")) return ArchiveFormat.Zip;
        return null;
    }

    /// <summary>
    /// <paramref name="name"/> with any archive extension it already has replaced by the one for
    /// <paramref name="format"/> ("photos.zip" → "photos.tar.gz"). Other extensions are kept, so
    /// "notes.txt" becomes "notes.txt.zip".
    /// </summary>
    public static string WithExtension(string name, ArchiveFormat format)
    {
        var n = name.Trim();
        foreach (var ext in new[] { ".tar.gz", ".tgz", ".tar.zst", ".tzst", ".tar.xz", ".txz", ".tar", ".zip", ".7z" })
        {
            if (n.Length > ext.Length && n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                n = n[..^ext.Length];
                break;
            }
        }
        return n + Extension(format);
    }

    /// <summary>
    /// Returns <paramref name="fileName"/>, or "name_1.ext", "name_2.ext"… if it's taken. A
    /// two-part extension like .tar.gz stays together ("a_1.tar.gz", not "a.tar_1.gz").
    /// </summary>
    public static string UniqueName(string fileName, Func<string, bool> exists)
    {
        if (!exists(fileName)) return fileName;

        string ext = new[] { ".tar.gz", ".tar.zst", ".tar.xz" }.FirstOrDefault(e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                     is { } two ? fileName[^two.Length..] : Path.GetExtension(fileName);
        string stem = fileName[..^ext.Length];

        for (int i = 1; ; i++)
        {
            string candidate = $"{stem}_{i}{ext}";
            if (!exists(candidate)) return candidate;
        }
    }

    /// <summary>Why <paramref name="fileName"/> can't be used as an archive name, or null if it can.</summary>
    public static string? ValidateName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "Enter a name for the archive.";
        if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0) return "The name can't contain / or \\.";
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The name contains characters that aren't allowed in a file name.";
        if (fileName.Trim() is "." or "..") return "Enter a name for the archive.";
        return null;
    }

    /// <summary>
    /// Builds an archive named <paramref name="archiveName"/> in <paramref name="destFolder"/>
    /// from <paramref name="sourcePaths"/> (files and folders; folders are added with everything in
    /// them, hidden files and empty folders included). Each source is stored under its own name at
    /// the top of the archive. If the name is taken a numbered one is used; the result says which.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled; nothing is left behind.</exception>
    /// <exception cref="InvalidOperationException">The destination can't be written to (e.g. inside an archive).</exception>
    public static Task<ArchiveResult> CreateAsync(
        string destFolder, string archiveName, IReadOnlyList<string> sourcePaths, ArchiveFormat format,
        CompressionLevel level = CompressionLevel.Optimal,
        IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default, string? password = null)
        => Task.Run(() => Create(destFolder, archiveName, sourcePaths, format, level, progress, ct, password), ct);

    private sealed record Item(IFileProvider Provider, string SourcePath, string EntryName, bool IsDir,
                               long Size, DateTime Modified);

    private static ArchiveResult Create(
        string destFolder, string archiveName, IReadOnlyList<string> sourcePaths, ArchiveFormat format,
        CompressionLevel level, IProgress<ArchiveProgress>? progress, CancellationToken ct, string? password = null)
    {
        if (!string.IsNullOrEmpty(password) && format is not (ArchiveFormat.Zip or ArchiveFormat.SevenZip))
            throw new ArgumentException("Only .zip and .7z archives can have a password.", nameof(password));
        if (format == ArchiveFormat.SevenZip && SevenZipTool.Path == null)
            throw new InvalidOperationException("Making .7z archives needs 7-Zip's command-line program (7z, 7zz or 7za), which isn't installed.");
        if (format == ArchiveFormat.TarXz && ExternalCompressor.Xz == null)
            throw new InvalidOperationException("Making .tar.xz archives needs the xz program, which isn't installed.");
        if (ValidateName(archiveName) is { } problem) throw new ArgumentException(problem, nameof(archiveName));
        if (sourcePaths.Count == 0) throw new ArgumentException("Nothing to archive.", nameof(sourcePaths));

        var destProvider = ProviderRegistry.For(destFolder);
        if (destProvider is ArchiveFileProvider)
            throw new InvalidOperationException("Archives are read-only, so a new archive can't be created inside one. Pick a normal folder.");

        // Plan everything first, so progress has a total and unreadable items are found up front.
        var skipped = new List<ArchiveSkip>();
        var items = new List<Item>();
        foreach (var src in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            Plan(src, skipped, items, ct);
        }

        string finalName = UniqueName(archiveName.Trim(),
            n => SafeExists(destProvider, FileUtility.JoinPanelPath(destFolder, n)));
        string finalPath = FileUtility.JoinPanelPath(destFolder, finalName);

        long total = items.Where(i => !i.IsDir).Sum(i => i.Size);
        int filesTotal = items.Count(i => !i.IsDir);

        string temp = Path.Combine(Path.GetTempPath(), $"dori-archive-{Guid.NewGuid():N}{Extension(format)}");
        try
        {
            var counter = new Counter(progress, total, filesTotal);
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                switch (format)
                {
                    case ArchiveFormat.Zip when !string.IsNullOrEmpty(password):
                        WriteAesZip(fs, items, level, password!, counter, skipped, ct);
                        break;
                    case ArchiveFormat.Zip:
                        WriteZip(fs, items, level, counter, skipped, ct);
                        break;
                    case ArchiveFormat.TarGz:
                        using (var gz = new GZipStream(fs, level, leaveOpen: true))
                            WriteTar(gz, items, counter, skipped, ct);
                        break;
                    case ArchiveFormat.Tar:
                        WriteTar(fs, items, counter, skipped, ct);
                        break;
                    case ArchiveFormat.TarZst:
                        using (var zst = new SharpCompress.Providers.Default.ZStandardCompressionProvider().CreateCompressStream(fs, ZstdLevel(level)))
                            WriteTar(zst, items, counter, skipped, ct);
                        break;
                    case ArchiveFormat.TarXz:
                    {
                        string plain = temp + ".tar";
                        try
                        {
                            using (var tarFile = new FileStream(plain, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                                WriteTar(tarFile, items, counter, skipped, ct);
                            ExternalCompressor.XzInto(plain, fs, XzLevel(level), ct);
                        }
                        finally { try { File.Delete(plain); } catch { } }
                        break;
                    }
                    case ArchiveFormat.SevenZip:
                        fs.Dispose();
                        File.Delete(temp);
                        SevenZipTool.Create(temp, items.Select(i => (i.Provider, i.SourcePath, i.EntryName, i.IsDir)).ToList(), level, password, counter, skipped, ct);
                        break;
                }
            }

            ct.ThrowIfCancellationRequested();
            Deliver(destProvider, temp, finalPath, ct);

            return new ArchiveResult(finalPath, counter.FilesDone, items.Count(i => i.IsDir), counter.BytesDone, skipped);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    internal static int ZstdLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.SmallestSize => 19,
        CompressionLevel.Fastest => 1,
        CompressionLevel.NoCompression => 1,
        _ => 6,
    };

    internal static int XzLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.SmallestSize => 9,
        CompressionLevel.Fastest => 1,
        CompressionLevel.NoCompression => 0,
        _ => 6,
    };

    private static bool SafeExists(IFileProvider p, string path)
    {
        try { return p.FileExists(path) || p.DirectoryExists(path); }
        catch { return false; }
    }

    /// <summary>Moves the finished temp file to its destination (or uploads it for a cloud folder).</summary>
    private static void Deliver(IFileProvider destProvider, string temp, string finalPath, CancellationToken ct)
    {
        if (destProvider is LocalFileProvider)
        {
            // overwrite:false — if something appeared under this name meanwhile, fail rather than clobber it.
            File.Move(temp, finalPath, overwrite: false);
            return;
        }
        destProvider.CopyFromLocalAsync(temp, finalPath, overwrite: false, progress: null, ct).GetAwaiter().GetResult();
    }

    // ---------------- planning ----------------

    private static void Plan(string sourcePath, List<ArchiveSkip> skipped, List<Item> items, CancellationToken ct)
    {
        var provider = ProviderRegistry.For(sourcePath);
        FileEntry? stat;
        try { stat = provider.Stat(sourcePath); }
        catch (Exception ex) { skipped.Add(new ArchiveSkip(sourcePath, ex.Message)); return; }

        if (stat == null) { skipped.Add(new ArchiveSkip(sourcePath, "Not found.")); return; }

        string name = stat.Name;
        if (stat.IsDirectory)
            PlanFolder(provider, sourcePath, name, stat.LastModified, skipped, items, ct);
        else
            items.Add(new Item(provider, sourcePath, name, false, stat.Size, stat.LastModified));
    }

    private static void PlanFolder(IFileProvider provider, string path, string entryName, DateTime modified,
                                   List<ArchiveSkip> skipped, List<Item> items, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // A symbolic link to a folder could loop back on itself; store nothing for it.
        if (provider is LocalFileProvider && IsLink(path))
        {
            skipped.Add(new ArchiveSkip(path, "Folder is a symbolic link; not followed."));
            return;
        }

        List<FileEntry> files, dirs;
        try
        {
            files = provider.EnumerateFiles(path).ToList();
            dirs = provider.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex)
        {
            skipped.Add(new ArchiveSkip(path, ex.Message));
            return;
        }

        // Every folder gets its own entry, so empty folders survive the round trip.
        items.Add(new Item(provider, path, entryName + "/", true, 0, modified));

        foreach (var f in files.OrderBy(f => f.Name, StringComparer.Ordinal))
            items.Add(new Item(provider, f.Path, entryName + "/" + f.Name, false, f.Size, f.LastModified));

        foreach (var d in dirs.OrderBy(d => d.Name, StringComparer.Ordinal))
            PlanFolder(provider, d.Path, entryName + "/" + d.Name, d.LastModified, skipped, items, ct);
    }

    private static bool IsLink(string path)
    {
        try { return new DirectoryInfo(path).LinkTarget != null; }
        catch { return false; }
    }

    // ---------------- writing ----------------

    private static void WriteZip(Stream output, List<Item> items, CompressionLevel level, Counter counter,
                                 List<ArchiveSkip> skipped, CancellationToken ct)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsDir)
            {
                var dirEntry = zip.CreateEntry(item.EntryName, CompressionLevel.NoCompression);
                dirEntry.LastWriteTime = ClampZipDate(item.Modified);
                continue;
            }

            Stream src;
            try { src = item.Provider.OpenRead(item.SourcePath); }
            catch (Exception ex) { skipped.Add(new ArchiveSkip(item.SourcePath, ex.Message)); continue; }

            using (src)
            {
                var entry = zip.CreateEntry(item.EntryName, level);
                entry.LastWriteTime = ClampZipDate(item.Modified);
                if (item.Provider is LocalFileProvider && !OperatingSystem.IsWindows())
                {
                    // Keep Unix permissions (e.g. the execute bit) the way Info-ZIP stores them.
                    const int S_IFREG = 0x8000;
                    try { entry.ExternalAttributes = (S_IFREG | (int)File.GetUnixFileMode(item.SourcePath)) << 16; }
                    catch { /* not critical */ }
                }
                using var dst = entry.Open();
                counter.StartFile(item.EntryName);
                Pump(src, dst, counter, ct);
                counter.EndFile();
            }
        }
    }

    /// <summary>A password-protected zip (WinZip AES-256), see <see cref="AesZipWriter"/>.</summary>
    private static void WriteAesZip(Stream output, List<Item> items, CompressionLevel level, string password, Counter counter,
                                    List<ArchiveSkip> skipped, CancellationToken ct)
    {
        using var zip = new AesZipWriter(output, password, level);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsDir) { zip.AddFolder(item.EntryName, item.Modified); continue; }

            Stream src;
            try { src = item.Provider.OpenRead(item.SourcePath); }
            catch (Exception ex) { skipped.Add(new ArchiveSkip(item.SourcePath, ex.Message)); continue; }
            using (src)
            {
                uint mode = 0;
                if (item.Provider is LocalFileProvider && !OperatingSystem.IsWindows())
                {
                    try { mode = (uint)File.GetUnixFileMode(item.SourcePath); } catch { }
                }
                counter.StartFile(item.EntryName);
                zip.AddFile(item.EntryName, src, item.Modified, mode, counter.Add, ct);
                counter.EndFile();
            }
        }
        zip.Finish();
    }

    private static void WriteTar(Stream output, List<Item> items, Counter counter,
                                 List<ArchiveSkip> skipped, CancellationToken ct)
    {
        using var tar = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsDir)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, item.EntryName)
                {
                    ModificationTime = ToOffset(item.Modified),
                    Mode = ModeFor(item, defaultMode: (UnixFileMode)0b111_101_101), // 755
                });
                continue;
            }

            Stream src;
            try { src = item.Provider.OpenRead(item.SourcePath); }
            catch (Exception ex) { skipped.Add(new ArchiveSkip(item.SourcePath, ex.Message)); continue; }

            using (src)
            {
                counter.StartFile(item.EntryName);
                // A tar header holds the file's length up front, so the data must be seekable with a
                // known length. Local files are; anything else (cloud, inside an archive) is buffered
                // to a temp file first.
                Stream data = src;
                string? spool = null;
                try
                {
                    if (!src.CanSeek)
                    {
                        spool = Path.Combine(Path.GetTempPath(), $"dori-spool-{Guid.NewGuid():N}");
                        data = new FileStream(spool, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                                              BufferSize, FileOptions.DeleteOnClose);
                        Pump(src, data, counter, ct);
                        data.Position = 0;
                    }
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, item.EntryName)
                    {
                        ModificationTime = ToOffset(item.Modified),
                        Mode = ModeFor(item, defaultMode: (UnixFileMode)0b110_100_100), // 644
                        DataStream = spool == null ? new CountingReadStream(data, counter, ct) : data,
                    };
                    tar.WriteEntry(entry);
                }
                finally
                {
                    if (spool != null) data.Dispose();
                }
                counter.EndFile();
            }
        }
    }

    private static UnixFileMode ModeFor(Item item, UnixFileMode defaultMode)
    {
        if (item.Provider is LocalFileProvider && !OperatingSystem.IsWindows())
        {
            try { return File.GetUnixFileMode(item.SourcePath.TrimEnd('/')); }
            catch { /* fall through */ }
        }
        return defaultMode;
    }

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

    private static DateTimeOffset ToOffset(DateTime t)
    {
        // Entries with no real timestamp (DateTime.MinValue, e.g. an archive root) would underflow
        // when converted from local time; store the Unix epoch instead.
        if (t.Year < 1971) return DateTimeOffset.UnixEpoch;
        return t.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Local))
            : new DateTimeOffset(t);
    }

    private static DateTimeOffset ClampZipDate(DateTime t)
    {
        var o = ToOffset(t);
        if (o < ZipMinDate) return ZipMinDate;
        if (o > ZipMaxDate) return ZipMaxDate;
        return o;
    }

    /// <summary>Counts bytes and files and reports progress at a modest rate.</summary>
    internal sealed class Counter(IProgress<ArchiveProgress>? progress, long total, int filesTotal)
    {
        private string _current = "";
        private long _sinceReport;
        public long BytesDone { get; private set; }
        public int FilesDone { get; private set; }

        public void StartFile(string name)
        {
            _current = name;
            Report();
        }

        public void Add(int n)
        {
            BytesDone += n;
            _sinceReport += n;
            if (_sinceReport >= ReportEveryBytes) Report();
        }

        public void EndFile()
        {
            FilesDone++;
            Report();
        }

        private void Report()
        {
            _sinceReport = 0;
            progress?.Report(new ArchiveProgress(_current, BytesDone, total, FilesDone, filesTotal));
        }
    }

    /// <summary>A read-only pass-through that feeds <see cref="Counter"/> and honours cancellation.</summary>
    private sealed class CountingReadStream(Stream inner, Counter counter, CancellationToken ct) : Stream
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
