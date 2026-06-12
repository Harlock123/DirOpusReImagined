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

    public static async Task LocalToLocalAsync(string src, string dst, bool overwrite,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!overwrite && File.Exists(dst))
            throw new IOException($"Destination exists: {dst}");

        long total = TryLength(src);
        await using var inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var outStream = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await PumpAsync(inStream, outStream, total, NameOf(src), progress, ct).ConfigureAwait(false);
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
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long copied = 0;
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
                progress?.Report(new TransferProgress(name, 0, 0, copied, total, SpeedOf(copied, nowMs)));
            }
        }

        await outStream.FlushAsync(ct).ConfigureAwait(false);
        progress?.Report(new TransferProgress(name, 0, 0, copied, total > 0 ? total : copied,
            SpeedOf(copied, sw.ElapsedMilliseconds)));
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
