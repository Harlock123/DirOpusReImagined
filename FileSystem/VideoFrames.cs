using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Frames from videos, for thumbnails and the preview, made by <c>ffmpeg</c> when it is installed
/// (on the PATH, or where Homebrew puts it on macOS). Without ffmpeg, videos keep their generic icon
/// and the preview shows their details as before; nothing is bundled.
/// </summary>
public static class VideoFrames
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpg", "mpeg", "ts", "m2ts", "mts", "3gp", "ogv", "vob",
    };

    /// <summary>How long one frame may take before ffmpeg is stopped (a damaged file can hang it).</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>At most this many ffmpeg runs at once, so a folder of videos doesn't start dozens.</summary>
    private static readonly SemaphoreSlim Slots = new(2, 2);

    private static readonly Lazy<string?> Ffmpeg = new(FindFfmpeg);

    /// <summary>Where ffmpeg is, or null when it isn't installed.</summary>
    public static string? FfmpegPath => Ffmpeg.Value;

    public static bool Available => FfmpegPath != null;

    public static bool IsVideoFile(string name)
    {
        string ext = Path.GetExtension(name ?? "").TrimStart('.');
        return ext.Length > 0 && VideoExtensions.Contains(ext);
    }

    private static string? FindFfmpeg()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(OperatingSystem.IsMacOS() ? new[] { "/opt/homebrew/bin", "/usr/local/bin" } : Array.Empty<string>());
        foreach (var dir in dirs)
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// A PNG of a frame from the video at <paramref name="localPath"/>, <paramref name="width"/>
    /// pixels wide (0 keeps the video's size), or null when there's no ffmpeg or no frame. The frame
    /// is a few seconds in, past black openings; a shorter video gives its first frame. ffmpeg turns
    /// videos recorded sideways upright by itself.
    /// </summary>
    public static async Task<byte[]?> GrabPngAsync(string localPath, int width, CancellationToken ct = default)
    {
        if (FfmpegPath == null || !File.Exists(localPath)) return null;
        await Slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var at in new[] { "5", "1", "0" })
            {
                var png = await RunAsync(Arguments(localPath, at, width), ct).ConfigureAwait(false);
                if (png is { Length: > 0 }) return png;
            }
            return null;
        }
        finally { Slots.Release(); }
    }

    internal static List<string> Arguments(string path, string at, int width)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-ss", at, "-i", path, "-frames:v", "1", "-an", "-sn" };
        if (width > 0) { args.Add("-vf"); args.Add($"scale={width}:-2"); }
        args.AddRange(new[] { "-f", "image2pipe", "-vcodec", "png", "-" });
        return args;
    }

    private static async Task<byte[]?> RunAsync(List<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(FfmpegPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p == null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, timeout.Token);
            var drainErrors = p.StandardError.ReadToEndAsync(timeout.Token);
            await copy.ConfigureAwait(false);
            await drainErrors.ConfigureAwait(false);
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return p.ExitCode == 0 && ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            ct.ThrowIfCancellationRequested();
            return null;   // timed out
        }
    }
}
