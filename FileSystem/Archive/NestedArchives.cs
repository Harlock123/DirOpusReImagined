using System;
using System.Collections.Generic;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>
/// Archives opened from inside other archives (a .zip inside a .tar.gz). The inner one is taken
/// out to a temporary copy and browsed like any archive; this remembers where each copy came from,
/// so going up from its root returns to the outer archive's folder instead of the temp folder, and
/// the path can be shown as the outer archive's.
/// </summary>
public static class NestedArchives
{
    private static readonly Dictionary<string, string> OuterByCopy =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that the archive at <paramref name="copyFsPath"/> is a copy of <paramref name="outerEntryUri"/> (an archive:// entry).</summary>
    public static void Register(string copyFsPath, string outerEntryUri)
    {
        lock (OuterByCopy) OuterByCopy[copyFsPath] = outerEntryUri;
    }

    /// <summary>The archive:// entry the copy came from, or null when <paramref name="copyFsPath"/> isn't a nested copy.</summary>
    public static string? OuterOf(string copyFsPath)
    {
        lock (OuterByCopy) return OuterByCopy.TryGetValue(copyFsPath, out var o) ? o : null;
    }

    /// <summary>
    /// Where a path inside a nested copy really is, for people to read: "archive:///a/outer.zip!/inner.zip!/docs".
    /// Paths that aren't inside a nested copy come back as they are.
    /// </summary>
    public static string Describe(string uri)
    {
        if (!ArchivePath.IsArchiveUri(uri)) return uri;
        var ap = ArchivePath.Parse(uri);
        string? outer = OuterOf(ap.ArchiveFsPath);
        if (outer == null) return uri;
        return Describe(outer) + ArchivePath.Marker + ap.EntryPath;
    }
}
