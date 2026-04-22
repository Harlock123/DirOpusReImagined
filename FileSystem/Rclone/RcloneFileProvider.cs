using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed class RcloneFileProvider : IFileProvider
{
    public bool CanHandle(string path) => CloudPath.IsCloudUri(path);
    public bool IsRemote => true;

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
        => ListInternal(path, dirsOnly: true);

    public IEnumerable<FileEntry> EnumerateFiles(string path)
        => ListInternal(path, filesOnly: true);

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
    }

    public void DeleteFile(string path)
    {
        var cp = CloudPath.Parse(path);
        Post("operations/deletefile", FsRemote(cp)).Dispose();
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var cp = CloudPath.Parse(path);
        var endpoint = recursive ? "operations/purge" : "operations/rmdir";
        Post(endpoint, FsRemote(cp)).Dispose();
    }

    public void CopyFile(string src, string dst, bool overwrite)
    {
        var s = CloudPath.Parse(src);
        var d = CloudPath.Parse(dst);
        Post("operations/copyfile", SrcDst(s, d)).Dispose();
    }

    public void MoveFile(string src, string dst)
    {
        var s = CloudPath.Parse(src);
        var d = CloudPath.Parse(dst);
        Post("operations/movefile", SrcDst(s, d)).Dispose();
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

    private IEnumerable<FileEntry> ListInternal(string path, bool dirsOnly = false, bool filesOnly = false)
    {
        if (!CloudPath.IsCloudUri(path)) yield break;
        var cp = CloudPath.Parse(path);

        JsonDocument? doc = null;
        try
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
            doc = Post("operations/list", body);
        }
        catch
        {
            doc?.Dispose();
            yield break;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("list", out var list)) yield break;
            foreach (var el in list.EnumerateArray())
            {
                FileEntry? entry;
                try { entry = ToEntry(cp, el); }
                catch { entry = null; }
                if (entry is not null) yield return entry;
            }
        }
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
                }
                catch { }
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
