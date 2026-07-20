using System;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>
/// A location inside an archive, encoded as a virtual URI:
/// <code>archive://&lt;archive-file-path&gt;!/&lt;entry/path&gt;</code>
/// The <c>!/</c> marker separates the on-disk archive file from the entry path within it
/// (a widely used convention). An empty entry path is the archive's root. This mirrors
/// <see cref="DirOpusReImagined.FileSystem.Rclone.CloudPath"/> so the rest of the app can
/// treat archive locations like any other provider path.
/// </summary>
public readonly record struct ArchivePath(string ArchiveFsPath, string EntryPath)
{
    public const string Scheme = "archive://";

    /// <summary>The separator between the archive file path and the inner entry path.</summary>
    public const string Marker = "!/";

    public string FullUri => $"{Scheme}{ArchiveFsPath}{Marker}{EntryPath}";

    /// <summary>True when this location is the archive's top level (no entry path).</summary>
    public bool IsRoot => string.IsNullOrEmpty(EntryPath);

    public ArchivePath WithEntry(string newEntry) => new(ArchiveFsPath, newEntry.Trim('/'));

    /// <summary>Returns the child location by appending <paramref name="child"/> to the entry path.</summary>
    public ArchivePath Join(string child)
    {
        var trimmed = child.Trim('/');
        var combined = string.IsNullOrEmpty(EntryPath) ? trimmed : $"{EntryPath.TrimEnd('/')}/{trimmed}";
        return new ArchivePath(ArchiveFsPath, combined);
    }

    /// <summary>
    /// The parent location. Going up from an inner folder yields its parent entry; going up from
    /// the archive root yields the on-disk folder that contains the archive file (as a plain path),
    /// so the caller can leave the archive naturally.
    /// </summary>
    public string ParentUri()
    {
        if (!IsRoot)
        {
            var trimmed = EntryPath.TrimEnd('/');
            var slash = trimmed.LastIndexOf('/');
            var parentEntry = slash < 0 ? "" : trimmed.Substring(0, slash);
            return new ArchivePath(ArchiveFsPath, parentEntry).FullUri;
        }

        // At the archive root: step out to the real directory containing the archive file.
        return System.IO.Path.GetDirectoryName(ArchiveFsPath) ?? ArchiveFsPath;
    }

    public static bool IsArchiveUri(string uri)
        => !string.IsNullOrEmpty(uri)
           && uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static ArchivePath Parse(string uri)
    {
        if (!IsArchiveUri(uri))
            throw new ArgumentException($"Not an archive URI: {uri}", nameof(uri));

        var rest = uri.Substring(Scheme.Length);
        var marker = rest.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0)
            return new ArchivePath(rest, "");

        var archive = rest.Substring(0, marker);
        // The entry path is always '/'-separated internally; the transfer layer may concatenate a
        // platform separator (e.g. '\' on Windows), so normalize it here. The archive file path
        // portion is left untouched — on Windows it legitimately contains backslashes.
        var entry = rest.Substring(marker + Marker.Length).Replace('\\', '/').Trim('/');
        return new ArchivePath(archive, entry);
    }

    /// <summary>Archive file extensions the app treats as browsable (double-click enters them).</summary>
    private static readonly string[] Extensions =
        { ".zip", ".7z", ".rar", ".tar", ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".gz" };

    /// <summary>True if <paramref name="fileName"/> looks like a supported archive by extension.</summary>
    public static bool IsArchiveFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var lower = fileName.ToLowerInvariant();
        foreach (var ext in Extensions)
            if (lower.EndsWith(ext, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Builds the archive URI for browsing into the archive file at <paramref name="fsPath"/>.</summary>
    public static string RootUriFor(string fsPath) => new ArchivePath(fsPath, "").FullUri;
}
