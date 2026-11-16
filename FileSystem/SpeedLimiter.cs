using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Keeps copies under the speed limit (<see cref="AppOptions.SpeedLimit"/>, bytes per second, 0
/// for none). Every local copy asks it after each block; it lets up to a second's worth through
/// at once, then makes the copy wait. Cloud transfers are limited by rclone itself, with the same
/// number (see <c>RcloneClient.ApplySpeedLimitAsync</c>).
/// </summary>
public static class SpeedLimiter
{
    private static readonly object Gate = new();
    private static double _allowance;
    private static long _lastTimestamp;

    /// <summary>Waits as long as needed for <paramref name="bytes"/> just copied to fit under the limit.</summary>
    public static Task ThrottleAsync(long bytes, CancellationToken ct)
    {
        long limit = AppOptions.SpeedLimit;
        if (limit <= 0 || bytes <= 0) return Task.CompletedTask;
        double wait = Debt(bytes, limit, Stopwatch.GetTimestamp());
        return wait > 0 ? Task.Delay(TimeSpan.FromSeconds(wait), ct) : Task.CompletedTask;
    }

    /// <summary>Seconds to wait after <paramref name="bytes"/> at <paramref name="limit"/> bytes a second; for tests, with the clock given.</summary>
    internal static double Debt(long bytes, long limit, long timestamp)
    {
        lock (Gate)
        {
            double elapsed = _lastTimestamp == 0 ? 1 : (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = timestamp;
            _allowance = Math.Min(limit, _allowance + elapsed * limit) - bytes;
            return _allowance < 0 ? -_allowance / limit : 0;
        }
    }

    internal static void Reset()
    {
        lock (Gate) { _allowance = 0; _lastTimestamp = 0; }
    }

    /// <summary>"10 MB", "512 KB" or "" for no limit.</summary>
    public static string Describe(long limit) => limit <= 0 ? "" : ProgressFormat.Bytes(limit);

    /// <summary>
    /// Reads a limit typed as "10 MB", "512K", "1.5 MB" (per second). Blank, "0", "none", "off" and
    /// "no limit" mean no limit (0).
    /// </summary>
    public static bool TryParse(string? text, out long limit)
    {
        limit = 0;
        string t = (text ?? "").Trim().ToLowerInvariant();
        if (t.EndsWith("/s")) t = t[..^2].Trim();
        if (t is "" or "0" or "none" or "off" or "no limit" or "unlimited") return true;
        return FileSplitter.TryParseSize(t, out limit) && limit > 0;
    }
}
