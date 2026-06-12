using System;
using System.Collections.Generic;

namespace DirOpusReImagined.FileSystem.Rclone;

/// <summary>
/// Short-lived cache of cloud directory listings. Each cloud listing costs two network round-trips
/// (dirs + files) and the UI re-lists on every navigation and post-operation refresh, so caching the
/// rows for a few seconds makes repeat visits and refreshes instant. The TTL bounds how stale the
/// view can get from changes made outside the app; in-app mutations call <see cref="Clear"/> so the
/// user's own changes show immediately rather than waiting for expiry.
/// </summary>
internal static class RcloneListCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly object _gate = new();
    private static readonly Dictionary<string, Entry> _map = new();

    private readonly record struct Entry(List<FileEntry> Rows, DateTime StoredUtc);

    public static bool TryGet(string key, out List<FileEntry> rows)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var e) && DateTime.UtcNow - e.StoredUtc < Ttl)
            {
                rows = e.Rows;
                return true;
            }
            _map.Remove(key); // drop if stale
            rows = default!;
            return false;
        }
    }

    public static void Set(string key, List<FileEntry> rows)
    {
        lock (_gate) _map[key] = new Entry(rows, DateTime.UtcNow);
    }

    public static void Clear()
    {
        lock (_gate) _map.Clear();
    }
}
