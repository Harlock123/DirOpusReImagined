using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed class RcloneFileProvider : IFileProvider
{
    public bool CanHandle(string path) => CloudPath.IsCloudUri(path);
    public bool IsRemote => true;

    /// <summary>How often to poll rclone for transfer stats/status during a copy job.</summary>
    private const int PollIntervalMs = 300;

    /// <summary>Total attempts for a directory listing before surfacing the error to the UI.</summary>
    private const int ListMaxAttempts = 3;
    private const int ListRetryDelayMs = 250;

    public bool FileExists(string path)
    {
        if (!CloudPath.IsCloudUri(path)) return false;
        try
        {
            var raw = StatRaw(CloudPath.Parse(path));
            return raw is not null && !raw.Value.GetProperty("IsDir").GetBoolean();
        }
        catch { return false; }
    }

    public bool DirectoryExists(string path)
    {
        if (!CloudPath.IsCloudUri(path)) return false;
        var cp = CloudPath.Parse(path);
        if (string.IsNullOrEmpty(cp.Path)) return true;
        try
        {
            var raw = StatRaw(cp);
            return raw is not null && raw.Value.GetProperty("IsDir").GetBoolean();
        }
        catch { return false; }
    }

    public FileEntry? Stat(string path)
    {
        if (!CloudPath.IsCloudUri(path)) return null;
        var cp = CloudPath.Parse(path);
        try
        {
            var raw = StatRaw(cp);
            return raw is null ? null : ToEntry(cp, raw.Value);
        }
        catch { return null; }
    }

    public IEnumerable<FileEntry> EnumerateDirectories(string path)
        => ListEntries(path, dirsOnly: true);

    public IEnumerable<FileEntry> EnumerateFiles(string path)
        => ListEntries(path, filesOnly: true);

    public Stream OpenRead(string path)
    {
        var cp = CloudPath.Parse(path);
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-" + SafeName(cp));

        Post("operations/copyfile", new Dictionary<string, object>
        {
            ["srcFs"]     = cp.Fs,
            ["srcRemote"] = cp.Path,
            ["dstFs"]     = Path.GetDirectoryName(tmp) ?? "",
            ["dstRemote"] = Path.GetFileName(tmp),
        }).Dispose();

        return new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
    }

    public Stream OpenWrite(string path)
    {
        var cp = CloudPath.Parse(path);
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-" + SafeName(cp));
        return new UploadOnCloseStream(tmp, cp);
    }

    public void CreateDirectory(string path)
    {
        var cp = CloudPath.Parse(path);
        if (string.IsNullOrEmpty(cp.Path)) return;
        Post("operations/mkdir", FsRemote(cp)).Dispose();
        RcloneListCache.Clear();
    }

    public void DeleteFile(string path)
    {
        var cp = CloudPath.Parse(path);
        Post("operations/deletefile", FsRemote(cp)).Dispose();
        RcloneListCache.Clear();
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var cp = CloudPath.Parse(path);
        var endpoint = recursive ? "operations/purge" : "operations/rmdir";
        Post(endpoint, FsRemote(cp)).Dispose();
        RcloneListCache.Clear();
    }

    public void CopyFile(string src, string dst, bool overwrite)
    {
        var s = CloudPath.Parse(src);
        var d = CloudPath.Parse(dst);
        Post("operations/copyfile", SrcDst(s, d)).Dispose();
        RcloneListCache.Clear();
    }

    /// <summary>
    /// Server-side cloud copy with live progress. Fires operations/copyfile as an async rclone
    /// job, then polls core/stats + job/status until it finishes, translating each stats
    /// snapshot into a <see cref="TransferProgress"/>. Cancellation stops the in-flight job.
    /// Handles same-remote and cross-remote (e.g. gdrive→dropbox) copies — rclone routes bytes
    /// server-side either way, so nothing flows through this machine.
    /// </summary>
    public Task CopyFileAsync(string src, string dst, bool overwrite,
                              IProgress<TransferProgress>? progress, CancellationToken ct = default)
    {
        var s = CloudPath.Parse(src);
        var d = CloudPath.Parse(dst);
        return RunCopyJobAsync(SrcDst(s, d), NameOfCloud(d), progress, ct);
    }

    /// <summary>
    /// Cross-provider download: copies a cloud file straight to a local path via a single rclone
    /// job, so the (slow) network transfer reports real progress — unlike streaming through the
    /// temp-file round-trip in OpenRead.
    /// </summary>
    public Task CopyToLocalAsync(string src, string localDst,
                                 IProgress<TransferProgress>? progress, CancellationToken ct = default)
    {
        var s = CloudPath.Parse(src);
        var body = new Dictionary<string, object>
        {
            ["srcFs"]     = s.Fs,
            ["srcRemote"] = s.Path,
            ["dstFs"]     = Path.GetDirectoryName(localDst) ?? "",
            ["dstRemote"] = Path.GetFileName(localDst),
        };
        return RunCopyJobAsync(body, Path.GetFileName(localDst), progress, ct);
    }

    /// <summary>
    /// Cross-provider upload: copies a local file straight to this remote via a single rclone job,
    /// reporting real network progress (vs. the upload-on-close temp stream in OpenWrite).
    /// </summary>
    public Task CopyFromLocalAsync(string localSrc, string dst, bool overwrite,
                                   IProgress<TransferProgress>? progress, CancellationToken ct = default)
    {
        var d = CloudPath.Parse(dst);
        var body = new Dictionary<string, object>
        {
            ["srcFs"]     = Path.GetDirectoryName(localSrc) ?? "",
            ["srcRemote"] = Path.GetFileName(localSrc),
            ["dstFs"]     = d.Fs,
            ["dstRemote"] = d.Path,
        };
        return RunCopyJobAsync(body, NameOfCloud(d), progress, ct);
    }

    private static string NameOfCloud(CloudPath d)
        => string.IsNullOrEmpty(d.Path) ? d.Remote : Path.GetFileName(d.Path);

    /// <summary>
    /// Fires operations/copyfile as an async rclone job and polls core/stats + job/status until it
    /// finishes, translating each snapshot into a <see cref="TransferProgress"/>. Cancellation stops
    /// the in-flight job. Works for cloud→cloud, cloud→local, and local→cloud (rclone addresses the
    /// local filesystem as a remote), so the slow leg always reports real bytes.
    /// </summary>
    private async Task RunCopyJobAsync(Dictionary<string, object> body, string fallbackName,
                                       IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);

        var group = "diropus/" + Guid.NewGuid().ToString("N");
        var jobid = await client.StartAsyncJobAsync("operations/copyfile", body, group, ct)
            .ConfigureAwait(false);

        try
        {
            while (true)
            {
                // Stats are best-effort: a hiccup reading them shouldn't abort the copy,
                // so only cancellation propagates out of this block.
                try
                {
                    var stats = await client.GetStatsAsync(group, ct).ConfigureAwait(false);
                    progress?.Report(new TransferProgress(
                        CurrentFile:    string.IsNullOrEmpty(stats.CurrentFile) ? fallbackName : stats.CurrentFile!,
                        FileIndex:      1,
                        FileCount:      1,
                        BytesDone:      stats.Bytes,
                        BytesTotal:     stats.TotalBytes,
                        BytesPerSecond: stats.Speed));
                }
                catch (OperationCanceledException) { throw; }
                catch { /* ignore transient stats errors */ }

                var status = await client.GetJobStatusAsync(jobid, ct).ConfigureAwait(false);
                if (status.Finished)
                {
                    if (!status.Success)
                        throw new IOException($"rclone copy failed: {status.Error}");

                    // Emit a final 100% snapshot so the bar lands cleanly on completion.
                    long total = 0;
                    try
                    {
                        var fin = await client.GetStatsAsync(group, CancellationToken.None).ConfigureAwait(false);
                        total = fin.TotalBytes > 0 ? fin.TotalBytes : fin.Bytes;
                    }
                    catch { }
                    progress?.Report(new TransferProgress(fallbackName, 1, 1, total, total, 0));
                    RcloneListCache.Clear(); // job wrote to a remote (cloud→cloud / local→cloud)
                    return;
                }

                await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Best-effort stop of the running rclone job (don't reuse the cancelled token), then rethrow.
            try { await client.StopJobAsync(jobid, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public void MoveFile(string src, string dst)
    {
        var s = CloudPath.Parse(src);
        var d = CloudPath.Parse(dst);
        Post("operations/movefile", SrcDst(s, d)).Dispose();
        RcloneListCache.Clear();
    }

    public void MoveDirectory(string src, string dst)
    {
        var s = CloudPath.Parse(src);
        var d = CloudPath.Parse(dst);
        Post("sync/move", new Dictionary<string, object>
        {
            ["srcFs"] = $"{s.Fs}{s.Path}",
            ["dstFs"] = $"{d.Fs}{d.Path}",
            ["createEmptySrcDirs"] = true,
        }).Dispose();
        RcloneListCache.Clear();
    }

    public long GetDirectorySize(string path, bool recursive)
    {
        var cp = CloudPath.Parse(path);
        try
        {
            if (recursive)
            {
                using var doc = Post("operations/size", new Dictionary<string, object>
                {
                    ["fs"] = $"{cp.Fs}{cp.Path}",
                });
                return doc.RootElement.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.Number
                    ? b.GetInt64() : 0L;
            }

            long total = 0;
            foreach (var f in EnumerateFiles(path)) total += f.Size;
            return total;
        }
        catch { return 0; }
    }

    private List<FileEntry> ListEntries(string path, bool dirsOnly = false, bool filesOnly = false)
    {
        if (!CloudPath.IsCloudUri(path)) return new List<FileEntry>();
        var cp = CloudPath.Parse(path);

        // Serve repeat listings (revisits, post-operation refreshes) from the short-TTL cache.
        var cacheKey = $"{cp.Fs} {cp.Path} {(dirsOnly ? 'd' : '-')}{(filesOnly ? 'f' : '-')}";
        if (RcloneListCache.TryGet(cacheKey, out var cached)) return cached;

        // ListWithRetry throws on persistent failure (after retries) rather than returning nothing,
        // so a transient network/daemon hiccup surfaces as a "couldn't load" error in the UI instead
        // of an empty grid. A genuinely empty folder still lists fine. Failures are not cached.
        var result = new List<FileEntry>();
        using (var doc = ListWithRetry(cp, dirsOnly, filesOnly))
        {
            if (doc.RootElement.TryGetProperty("list", out var list))
            {
                foreach (var el in list.EnumerateArray())
                {
                    FileEntry? entry;
                    try { entry = ToEntry(cp, el); }
                    catch { entry = null; }
                    if (entry is not null) result.Add(entry);
                }
            }
        }

        RcloneListCache.Set(cacheKey, result);
        return result;
    }

    private static JsonDocument ListWithRetry(CloudPath cp, bool dirsOnly, bool filesOnly)
    {
        var opt = new Dictionary<string, object>();
        if (dirsOnly)  opt["dirsOnly"]  = true;
        if (filesOnly) opt["filesOnly"] = true;

        var body = new Dictionary<string, object>
        {
            ["fs"]     = cp.Fs,
            ["remote"] = cp.Path,
            ["opt"]    = opt,
        };

        Exception? last = null;
        for (int attempt = 1; attempt <= ListMaxAttempts; attempt++)
        {
            try
            {
                return Post("operations/list", body);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < ListMaxAttempts) Thread.Sleep(ListRetryDelayMs);
            }
        }

        throw new IOException(
            $"Could not list {cp.FullUri} after {ListMaxAttempts} attempts: {last?.Message}", last);
    }

    public string? ComputeHash(string path)
    {
        if (!CloudPath.IsCloudUri(path)) return null;
        var cp = CloudPath.Parse(path);
        try
        {
            // Ask rclone for the file's MD5 (many backends expose one server-side, so no download).
            using var doc = Post("operations/stat", new Dictionary<string, object>
            {
                ["fs"]     = cp.Fs,
                ["remote"] = cp.Path,
                ["opt"]    = new Dictionary<string, object>
                {
                    ["showHash"]  = true,
                    ["hashTypes"] = new[] { "md5" },
                },
            });

            if (!doc.RootElement.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null)
                return null;
            if (!item.TryGetProperty("Hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in hashes.EnumerateObject())
            {
                if (string.Equals(prop.Name, "md5", StringComparison.OrdinalIgnoreCase))
                {
                    var v = prop.Value.GetString();
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
            return null;
        }
        catch { return null; }
    }

    private JsonElement? StatRaw(CloudPath cp)
    {
        using var doc = Post("operations/stat", FsRemote(cp));
        if (!doc.RootElement.TryGetProperty("item", out var item)) return null;
        if (item.ValueKind == JsonValueKind.Null) return null;
        return item.Clone();
    }

    private static Dictionary<string, object> FsRemote(CloudPath cp) => new()
    {
        ["fs"]     = cp.Fs,
        ["remote"] = cp.Path,
    };

    private static Dictionary<string, object> SrcDst(CloudPath s, CloudPath d) => new()
    {
        ["srcFs"]     = s.Fs,
        ["srcRemote"] = s.Path,
        ["dstFs"]     = d.Fs,
        ["dstRemote"] = d.Path,
    };

    private static FileEntry ToEntry(CloudPath parent, JsonElement item)
    {
        var name = item.TryGetProperty("Name", out var np) ? np.GetString() ?? "" : "";
        var relPath = item.TryGetProperty("Path", out var pp) ? pp.GetString() ?? name : name;
        var isDir = item.TryGetProperty("IsDir", out var idp) && idp.GetBoolean();

        long size = 0;
        if (item.TryGetProperty("Size", out var sp) && sp.ValueKind == JsonValueKind.Number)
            size = sp.GetInt64();

        DateTime modTime = DateTime.UnixEpoch;
        if (item.TryGetProperty("ModTime", out var mt) && mt.ValueKind == JsonValueKind.String
            && DateTime.TryParse(mt.GetString(), out var parsed))
            modTime = parsed;

        var flags = name.StartsWith('.') ? FileEntryFlags.Hidden : FileEntryFlags.None;
        var attr = isDir ? "DIR" : "FILE";
        var fullUri = new CloudPath(parent.Remote, relPath).FullUri;

        return new FileEntry(fullUri, name, isDir, isDir ? 0 : size, modTime, flags, attr);
    }

    private static JsonDocument Post(string endpoint, object? body = null)
    {
        return Task.Run(async () =>
        {
            var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
            return await client.PostAsync(endpoint, body).ConfigureAwait(false);
        }).GetAwaiter().GetResult();
    }

    private static string SafeName(CloudPath cp)
    {
        var name = string.IsNullOrEmpty(cp.Path) ? cp.Remote : Path.GetFileName(cp.Path);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "cloudfile" : name;
    }

    private sealed class UploadOnCloseStream : FileStream
    {
        private readonly CloudPath _dst;
        private bool _uploaded;

        public UploadOnCloseStream(string tempPath, CloudPath dst)
            : base(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)
        {
            _dst = dst;
        }

        protected override void Dispose(bool disposing)
        {
            var tempPath = Name;
            base.Dispose(disposing);

            if (!_uploaded && disposing)
            {
                _uploaded = true;
                try
                {
                    Post("operations/copyfile", new Dictionary<string, object>
                    {
                        ["srcFs"]     = Path.GetDirectoryName(tempPath) ?? "",
                        ["srcRemote"] = Path.GetFileName(tempPath),
                        ["dstFs"]     = _dst.Fs,
                        ["dstRemote"] = _dst.Path,
                    }).Dispose();
                    RcloneListCache.Clear();
                }
                catch { }
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
