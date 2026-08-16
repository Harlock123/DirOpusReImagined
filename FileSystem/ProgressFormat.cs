using System;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Shared human-readable formatting for transfer progress (bytes, speed, ETA), so the
/// operations window and any other progress UI render numbers identically.
/// </summary>
public static class ProgressFormat
{
    /// <summary>Formats a byte count as "512 B", "1.4 MB", etc.</summary>
    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} {units[i]}" : $"{v:0.0} {units[i]}";
    }

    /// <summary>Formats a duration as "3s", "2m 5s", or "1h 12m".</summary>
    public static string Eta(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    /// <summary>The "done / total   speed/s   ETA" detail line for an in-flight transfer.</summary>
    public static string Stats(TransferProgress p)
    {
        string s;
        if (p.BytesTotal > 0)
            s = $"{Bytes(p.BytesDone)} / {Bytes(p.BytesTotal)}";
        else if (p.BytesDone > 0)
            s = Bytes(p.BytesDone);
        else
            s = "";

        if (p.BytesPerSecond > 0)
            s += $"   {Bytes((long)p.BytesPerSecond)}/s";

        if (p.Eta is { } eta)
            s += $"   ETA {Eta(eta)}";

        return s;
    }
}
