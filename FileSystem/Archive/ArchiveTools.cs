using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using SharpCompress.Archives;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>Finds programs on the PATH (and Homebrew's folders on macOS).</summary>
public static class ToolFinder
{
    public static string? Find(params string[] names)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(OperatingSystem.IsMacOS() ? new[] { "/opt/homebrew/bin", "/usr/local/bin" } : Array.Empty<string>())
            .Concat(OperatingSystem.IsWindows() ? new[] { @"C:\Program Files\7-Zip" } : Array.Empty<string>());
        foreach (var name in names)
        {
            string exe = OperatingSystem.IsWindows() && !name.EndsWith(".exe") ? name + ".exe" : name;
            foreach (var dir in dirs)
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim('"'), exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }
        return null;
    }
}

/// <summary>Compressing with an outside program: <c>xz</c>, for .tar.xz.</summary>
public static class ExternalCompressor
{
    private static readonly Lazy<string?> XzPath = new(() => ToolFinder.Find("xz"));
    public static string? Xz => XzPath.Value;

    /// <summary>Compresses <paramref name="input"/> with xz into <paramref name="output"/>.</summary>
    public static void XzInto(string input, Stream output, int level, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Xz ?? throw new InvalidOperationException("xz isn't installed."))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-T0", "-" + Math.Clamp(level, 0, 9), "-c", "--", input }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException("xz didn't start.");
        using var reg = ct.Register(() => { try { p.Kill(); } catch { } });
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.BaseStream.CopyTo(output);
        p.WaitForExit();
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) throw new IOException("xz failed: " + err.Result.Trim());
    }
}

/// <summary>
/// Making .7z archives with 7-Zip's command-line program (7z, 7zz or 7za). Files already on this
/// computer are handed to it where they are; files from archives or the cloud are copied to a
/// temporary folder first. A password also hides the file names (-mhe=on).
/// </summary>
public static class SevenZipTool
{
    private static readonly Lazy<string?> Found = new(() => ToolFinder.Find("7zz", "7z", "7za"));
    public static string? Path => Found.Value;

    public static int Level(CompressionLevel level) => level switch
    {
        CompressionLevel.SmallestSize => 9,
        CompressionLevel.Fastest => 1,
        CompressionLevel.NoCompression => 0,
        _ => 5,
    };

    /// <summary>7-Zip's arguments to add <paramref name="names"/> (relative to the working folder) to <paramref name="archive"/>.</summary>
    public static List<string> BuildArgs(string archive, IEnumerable<string> names, CompressionLevel level, string? password)
    {
        var args = new List<string> { "a", "-t7z", "-mx=" + Level(level), "-y", "-bso0", "-bsp0" };
        if (!string.IsNullOrEmpty(password)) { args.Add("-p" + password); args.Add("-mhe=on"); }
        args.Add(archive);
        args.Add("--");
        args.AddRange(names);
        return args;
    }

    internal static void Create(string archive, List<(IFileProvider Provider, string Source, string EntryName, bool IsDir)> items,
                                CompressionLevel level, string? password, ArchiveCreator.Counter counter,
                                List<ArchiveSkip> skipped, CancellationToken ct)
    {
        var tops = items.Select(i => i.EntryName.TrimEnd('/').Split('/')[0]).Distinct().ToList();
        string? stage = null;
        string workDir;
        bool allLocal = items.All(i => i.Provider is LocalFileProvider);
        var parents = items.Where(i => !i.EntryName.TrimEnd('/').Contains('/'))
                           .Select(i => System.IO.Path.GetDirectoryName(i.Source.TrimEnd('/', '\\')) ?? "").Distinct().ToList();
        try
        {
            if (allLocal && parents.Count == 1)
            {
                workDir = parents[0];
                foreach (var i in items.Where(i => !i.IsDir)) { counter.StartFile(i.EntryName); counter.EndFile(); }
            }
            else
            {
                stage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dori-7z-" + Guid.NewGuid().ToString("N")[..12]);
                Directory.CreateDirectory(stage);
                foreach (var i in items)
                {
                    ct.ThrowIfCancellationRequested();
                    string target = System.IO.Path.Combine(stage, i.EntryName.TrimEnd('/').Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (i.IsDir) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    counter.StartFile(i.EntryName);
                    try
                    {
                        using var src = i.Provider.OpenRead(i.Source);
                        using var dst = File.Create(target);
                        var buffer = new byte[81920];
                        int n;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0) { dst.Write(buffer, 0, n); counter.Add(n); ct.ThrowIfCancellationRequested(); }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { skipped.Add(new ArchiveSkip(i.Source, ex.Message)); }
                    counter.EndFile();
                }
                workDir = stage;
            }

            var psi = new ProcessStartInfo(Path ?? throw new InvalidOperationException("7-Zip isn't installed."))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = workDir,
            };
            foreach (var a in BuildArgs(archive, tops, level, password)) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new IOException("7-Zip didn't start.");
            p.StandardInput.Close();
            using var reg = ct.Register(() => { try { p.Kill(); } catch { } });
            var outText = p.StandardOutput.ReadToEndAsync();
            var errText = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            ct.ThrowIfCancellationRequested();
            // 0 is fine; 1 is a warning (a file that couldn't be read); 2 and up failed.
            if (p.ExitCode >= 2 || !File.Exists(archive))
                throw new IOException("7-Zip failed: " + (errText.Result + outText.Result).Trim());
            if (p.ExitCode == 1) skipped.Add(new ArchiveSkip(workDir, "7-Zip warned: " + errText.Result.Trim()));
        }
        finally
        {
            if (stage != null) try { Directory.Delete(stage, recursive: true); } catch { }
        }
    }
}

/// <summary>One problem a test of an archive found.</summary>
public sealed record ArchiveProblem(string Entry, string Problem);

/// <summary>
/// Test Archive: reads every file in an archive to its end, as extracting would, and checks
/// each against the CRC the archive records (zip, 7z and rar keep one) and its stated size.
/// </summary>
public static class ArchiveTester
{
    public sealed record Result(int Files, long Bytes, IReadOnlyList<ArchiveProblem> Problems);

    public static Result Test(string archiveFsPath, IProgress<(int Done, int Total, string Name)>? progress = null, CancellationToken ct = default)
    {
        var problems = new List<ArchiveProblem>();
        int files = 0;
        long bytes = 0;
        if (DiscImage.IsDiscImage(archiveFsPath))
        {
            var entries = DiscImage.List(archiveFsPath).Where(e => !e.IsDir).ToList();
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((files, entries.Count, e.Key));
                try
                {
                    using var s = DiscImage.OpenFile(archiveFsPath, e.Key);
                    long n = Drain(s, null, ct);
                    if (n != e.Size) problems.Add(new ArchiveProblem(e.Key, $"{n:N0} bytes read, {e.Size:N0} expected"));
                    bytes += n;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { problems.Add(new ArchiveProblem(e.Key, ex.Message)); }
                files++;
            }
            return new Result(files, bytes, problems);
        }

        IArchive archive;
        try { archive = ArchiveFactory.OpenArchive(archiveFsPath, ArchivePasswords.OptionsFor(archiveFsPath)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Compressed tars (.tar.gz, .tar.xz, .tar.zst…) are read straight through instead.
            return TestByReading(archiveFsPath, progress, ct);
        }
        using var _ = archive;
        var list = archive.Entries.Where(e => !e.IsDirectory).ToList();
        foreach (var entry in list)
        {
            ct.ThrowIfCancellationRequested();
            string key = entry.Key ?? "?";
            progress?.Report((files, list.Count, key));
            try
            {
                var crc = new Crc32();
                using var s = entry.OpenEntryStream();
                long n = Drain(s, crc, ct);
                bytes += n;
                if (entry.Size > 0 && n != entry.Size)
                    problems.Add(new ArchiveProblem(key, $"{n:N0} bytes read, {entry.Size:N0} expected"));
                else if (entry.Crc != 0 && crc.Value != (uint)entry.Crc)
                    problems.Add(new ArchiveProblem(key, $"damaged: its checksum is {crc.Value:X8}, the archive says {(uint)entry.Crc:X8}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { problems.Add(new ArchiveProblem(key, ex.Message)); }
            files++;
        }
        return new Result(files, bytes, problems);
    }

    private static Result TestByReading(string archiveFsPath, IProgress<(int Done, int Total, string Name)>? progress, CancellationToken ct)
    {
        var problems = new List<ArchiveProblem>();
        int files = 0;
        long bytes = 0;
        try
        {
            using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(archiveFsPath, ArchivePasswords.OptionsFor(archiveFsPath));
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                var e = reader.Entry;
                if (e == null || e.IsDirectory) continue;
                string key = e.Key ?? "?";
                progress?.Report((files, Math.Max(files + 1, files), key));
                try
                {
                    using var s = reader.OpenEntryStream();
                    long n = Drain(s, null, ct);
                    bytes += n;
                    if (e.Size > 0 && n != e.Size) problems.Add(new ArchiveProblem(key, $"{n:N0} bytes read, {e.Size:N0} expected"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { problems.Add(new ArchiveProblem(key, ex.Message)); }
                files++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Add(new ArchiveProblem("(the archive)", ex.Message));
        }
        return new Result(files, bytes, problems);
    }

    private static long Drain(Stream s, Crc32? crc, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            crc?.Append(buffer.AsSpan(0, n));
            total += n;
        }
        return total;
    }

    /// <summary>The CRC-32 zip and 7z use (IEEE 802.3, reflected).</summary>
    public sealed class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(i =>
        {
            uint c = (uint)i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private uint _crc = 0xFFFFFFFFu;
        public void Append(ReadOnlySpan<byte> data)
        {
            uint c = _crc;
            foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            _crc = c;
        }
        public uint Value => _crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// Extract Here: everything goes straight into the folder when the archive holds a single item
/// at its top (one folder, or one file), else into a new folder named after the archive.
/// </summary>
public static class ExtractHere
{
    /// <summary>The items at the top of an archive, from its entry keys ("a/b.txt" → "a").</summary>
    public static List<string> TopLevel(IEnumerable<string> keys)
        => keys.Select(k => k.Replace('\\', '/').TrimStart('/').TrimEnd('/'))
               .Where(k => k.Length > 0 && k != ".")
               .Select(k => k.StartsWith("./") ? k[2..] : k)
               .Select(k => k.Split('/')[0])
               .Where(k => k.Length > 0)
               .Distinct(StringComparer.Ordinal).ToList();

    /// <summary>"photos.tar.gz" → "photos".</summary>
    public static string BaseName(string archiveName)
    {
        foreach (var ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tgz", ".tbz2", ".txz", ".tzst" })
            if (archiveName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && archiveName.Length > ext.Length)
                return archiveName[..^ext.Length];
        string stem = System.IO.Path.GetFileNameWithoutExtension(archiveName);
        return stem.Length > 0 ? stem : archiveName + " (contents)";
    }

    /// <summary>Whether the contents need a folder of their own, and its (unused) name.</summary>
    public static (bool NeedsFolder, string Folder) Plan(string archiveName, IReadOnlyCollection<string> topLevel, string destFolder, Func<string, bool> exists)
    {
        if (topLevel.Count == 1) return (false, destFolder);
        string name = BaseName(archiveName);
        string candidate = System.IO.Path.Combine(destFolder, name);
        for (int n = 2; exists(candidate); n++) candidate = System.IO.Path.Combine(destFolder, $"{name} ({n})");
        return (true, candidate);
    }
}
