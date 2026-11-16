using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Buffered, cancellable stream copy that reports byte-level <see cref="TransferProgress"/>.
/// Used for the legs no remote job owns: local→local copies, and as the default for any
/// provider that doesn't override the upload/download seams on <see cref="IFileProvider"/>.
/// </summary>
internal static class StreamCopy
{
    private const int BufferSize = 81920;
    private const int ReportEveryMs = 100;

    /// <summary>The ending of the file a copy is written into until it is complete.</summary>
    public const string PartialSuffix = ".dori-part";

    /// <summary>A cancelled or failed copy keeps what it wrote when it got at least this far, so it can carry on.</summary>
    internal const long KeepPartialFrom = 4L * 1024 * 1024;

    /// <summary>Where a copy to <paramref name="dst"/> is written until it is complete.</summary>
    public static string PartialPathFor(string dst) => dst + PartialSuffix;

    /// <summary>
    /// Copies a file on the local disk. The bytes go into <c>name.dori-part</c> first, which is
    /// renamed to <paramref name="dst"/> only once complete, so a half-copied file never sits under
    /// the real name. When a copy is cancelled or fails after 4 MB, that part file is kept, and
    /// the next copy of the same file to the same place (Retry, or copying it again) carries on
    /// from where it stopped, after checking the part still matches the start of the source.
    /// </summary>
    public static async Task LocalToLocalAsync(string src, string dst, bool overwrite,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!overwrite && File.Exists(dst))
            throw new IOException($"Destination exists: {dst}");

        long total = TryLength(src);
        string partial = PartialPathFor(dst);
        long resumeAt = ResumablePrefix(src, partial, total);

        var outStream = new FileStream(partial, resumeAt > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        bool done = false;
        try
        {
            await using (var inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            await using (outStream)
            {
                if (resumeAt > 0)
                {
                    inStream.Seek(resumeAt, SeekOrigin.Begin);
                    outStream.Seek(resumeAt, SeekOrigin.Begin);
                    outStream.SetLength(resumeAt);
                }
                await PumpAsync(inStream, outStream, total, NameOf(src), progress, ct, resumeAt).ConfigureAwait(false);
            }
            File.Move(partial, dst, overwrite: true);
            done = true;
        }
        finally
        {
            if (!done)
            {
                // Keep a large part to carry on from; a small one isn't worth leaving behind.
                try { if (TryLength(partial) < KeepPartialFrom) File.Delete(partial); } catch { }
            }
        }
    }

    /// <summary>
    /// How many bytes of an earlier, unfinished copy at <paramref name="partial"/> can be kept: its
    /// length when it is no longer than the source and its first and last megabyte match the
    /// source's bytes at the same places, otherwise 0 (start again).
    /// </summary>
    internal static long ResumablePrefix(string src, string partial, long sourceLength)
    {
        try
        {
            if (!File.Exists(partial)) return 0;
            long have = new FileInfo(partial).Length;
            if (have <= 0 || have > sourceLength) return 0;
            using var a = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var b = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read);
            const int Probe = 1024 * 1024;
            if (!SameBytes(a, b, 0, (int)Math.Min(Probe, have))) return 0;
            long tail = Math.Max(0, have - Probe);
            if (tail > 0 && !SameBytes(a, b, tail, (int)(have - tail))) return 0;
            return have;
        }
        catch { return 0; }
    }

    private static bool SameBytes(Stream a, Stream b, long offset, int count)
    {
        var x = new byte[count];
        var y = new byte[count];
        a.Seek(offset, SeekOrigin.Begin);
        b.Seek(offset, SeekOrigin.Begin);
        a.ReadExactly(x, 0, count);
        b.ReadExactly(y, 0, count);
        return x.AsSpan().SequenceEqual(y);
    }

    public static async Task ProviderToLocalAsync(IFileProvider src, string srcPath, string localDst,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        long total = SafeSize(src, srcPath);
        await using var inStream = src.OpenRead(srcPath);
        await using var outStream = new FileStream(localDst, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await PumpAsync(inStream, outStream, total, NameOf(srcPath), progress, ct).ConfigureAwait(false);
    }

    public static async Task LocalToProviderAsync(IFileProvider dst, string localSrc, string dstPath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        long total = TryLength(localSrc);
        await using var inStream = new FileStream(localSrc, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var outStream = dst.OpenWrite(dstPath);
        await PumpAsync(inStream, outStream, total, NameOf(localSrc), progress, ct).ConfigureAwait(false);
    }

    private static async Task PumpAsync(Stream inStream, Stream outStream, long total, string name,
        IProgress<TransferProgress>? progress, CancellationToken ct, long alreadyDone = 0)
    {
        var buffer = new byte[BufferSize];
        long copied = alreadyDone;
        var sw = Stopwatch.StartNew();
        long lastReportMs = 0;

        int n;
        while ((n = await inStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
        {
            await outStream.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            copied += n;

            long nowMs = sw.ElapsedMilliseconds;
            if (nowMs - lastReportMs >= ReportEveryMs)
            {
                lastReportMs = nowMs;
                progress?.Report(new TransferProgress(name, 0, 0, copied, total, SpeedOf(copied - alreadyDone, nowMs)));
            }
        }

        await outStream.FlushAsync(ct).ConfigureAwait(false);
        progress?.Report(new TransferProgress(name, 0, 0, copied, total > 0 ? total : copied,
            SpeedOf(copied - alreadyDone, sw.ElapsedMilliseconds)));
    }

    private static double SpeedOf(long bytes, long elapsedMs)
        => elapsedMs > 0 ? bytes / (elapsedMs / 1000.0) : 0;

    private static long TryLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static long SafeSize(IFileProvider provider, string path)
    {
        try { return provider.Stat(path)?.Size ?? 0; } catch { return 0; }
    }

    private static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
