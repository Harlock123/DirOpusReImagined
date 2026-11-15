using System;

namespace DirOpusReImagined.FileSystem;

[Flags]
public enum FileEntryFlags
{
    None     = 0,
    Hidden   = 1 << 0,
    ReadOnly = 1 << 1,
    System   = 1 << 2,
    Archive  = 1 << 3,
    Symlink  = 1 << 4,
}

public sealed record FileEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime LastModified,
    FileEntryFlags Flags,
    string AttributeString = "",
    string? LinkTarget = null
)
{
    /// <summary>
    /// True for a symbolic link (or a Windows junction). <see cref="LinkTarget"/> is the target as
    /// written in the link, which may be relative to the link's folder. Code that walks folder trees
    /// must not descend into a linked folder: it may lead back to a parent and never end.
    /// </summary>
    public bool IsLink => LinkTarget != null;
}
