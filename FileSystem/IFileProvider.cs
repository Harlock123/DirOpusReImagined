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

    /// <summary>
    /// Uploads a local file into this provider (cross-provider local→remote leg), reporting
    /// progress. The default streams the bytes; remote providers override to drive a native
    /// transfer so the slow network leg reports real progress instead of the temp-file copy.
    /// </summary>
    Task CopyFromLocalAsync(string localSrc, string dst, bool overwrite,
                            IProgress<TransferProgress>? progress, CancellationToken ct = default)
        => StreamCopy.LocalToProviderAsync(this, localSrc, dst, progress, ct);

    /// <summary>
    /// Downloads a file from this provider to a local path (cross-provider remote→local leg),
    /// reporting progress. The default streams the bytes; remote providers override for native progress.
    /// </summary>
    Task CopyToLocalAsync(string src, string localDst,
                          IProgress<TransferProgress>? progress, CancellationToken ct = default)
        => StreamCopy.ProviderToLocalAsync(this, src, localDst, progress, ct);

    long GetDirectorySize(string path, bool recursive);
}
