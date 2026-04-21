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
}

public sealed record FileEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime LastModified,
    FileEntryFlags Flags,
    string AttributeString = ""
);
