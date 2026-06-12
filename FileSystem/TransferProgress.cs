using System;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// A snapshot of an in-flight transfer, reported via <see cref="IProgress{T}"/> from the
/// provider layer up to the UI. Immutable so it can be marshalled across threads freely.
/// </summary>
/// <param name="CurrentFile">Name of the file currently being transferred (for the detail line).</param>
/// <param name="FileIndex">1-based index of the current file within the batch.</param>
/// <param name="FileCount">Total number of files in the batch (0 if unknown).</param>
/// <param name="BytesDone">Bytes transferred so far across the whole batch.</param>
/// <param name="BytesTotal">Total bytes to transfer across the whole batch (0 if unknown).</param>
/// <param name="BytesPerSecond">Current transfer speed in bytes/sec (0 if unknown).</param>
public readonly record struct TransferProgress(
    string CurrentFile,
    int FileIndex,
    int FileCount,
    long BytesDone,
    long BytesTotal,
    double BytesPerSecond)
{
    /// <summary>True when a meaningful total is known, so a determinate bar can be shown.</summary>
    public bool HasTotal => BytesTotal > 0;

    /// <summary>Completed fraction in [0,1], or 0 when the total is unknown.</summary>
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1) : 0;

    /// <summary>Estimated time remaining, or null when it cannot be computed.</summary>
    public TimeSpan? Eta
    {
        get
        {
            if (BytesTotal <= 0 || BytesPerSecond <= 0) return null;
            var remaining = BytesTotal - BytesDone;
            if (remaining <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(remaining / BytesPerSecond);
        }
    }
}
