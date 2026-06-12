namespace DirOpusReImagined.FileSystem;

/// <summary>
/// One top-level copy/move unit in a batch, using copy-to-folder semantics.
/// </summary>
/// <param name="Source">Absolute source path (local path or cloud:// URI).</param>
/// <param name="TargetFolder">Destination folder the item is copied/moved into.</param>
/// <param name="TargetPath">TargetFolder + item name — the final path (used for directory targets).</param>
/// <param name="IsDirectory">True if the source is a directory.</param>
public sealed record TransferItem(string Source, string TargetFolder, string TargetPath, bool IsDirectory);
