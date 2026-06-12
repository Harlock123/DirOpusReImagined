using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

public interface IFileProvider
{
    bool CanHandle(string path);

    /// <summary>
    /// True if operations have high per-call latency (network). UI-layer code uses
    /// this to skip expensive per-item calls (counts, recursive size) and to dispatch
    /// listings to a background thread instead of freezing the UI.
    /// </summary>
    bool IsRemote { get; }

    bool FileExists(string path);
    bool DirectoryExists(string path);

    FileEntry? Stat(string path);

    IEnumerable<FileEntry> EnumerateDirectories(string path);
    IEnumerable<FileEntry> EnumerateFiles(string path);

    Stream OpenRead(string path);
    Stream OpenWrite(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);

    void CopyFile(string src, string dst, bool overwrite);
    void MoveFile(string src, string dst);
    void MoveDirectory(string src, string dst);

    /// <summary>
    /// Copies a single file, reporting byte-level progress and honoring cancellation.
    /// The default implementation falls back to the synchronous <see cref="CopyFile"/> on a
    /// background thread with no intermediate progress, so providers can opt in incrementally.
    /// Remote providers (rclone) override this to drive an async job and poll real stats.
    /// </summary>
    Task CopyFileAsync(string src, string dst, bool overwrite,
                       IProgress<TransferProgress>? progress, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            CopyFile(src, dst, overwrite);
        }, ct);

    long GetDirectorySize(string path, bool recursive);
}
