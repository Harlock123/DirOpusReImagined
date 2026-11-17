using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

/// <summary>
/// Share links and mounting, through the rclone daemon DORI already runs:
/// <list type="bullet">
/// <item><b>Copy Share Link</b>: a link anyone can open (rclone's <c>operations/publiclink</c>), for
/// the providers that have them (Google Drive, OneDrive, Dropbox, Box, pCloud, Mega and more).</item>
/// <item><b>Mount as Drive</b>: the remote as a folder other programs can open (<c>mount/mount</c>),
/// in ~/DORI Mounts/NAME (or a free drive letter on Windows), until it's unmounted or DORI closes.
/// Needs FUSE: fuse3 on Linux, macFUSE or FUSE-T on macOS, WinFsp on Windows.</item>
/// </list>
/// </summary>
public static class CloudExtras
{
    /// <summary>rclone's request for a public link to <paramref name="cloudUri"/>.</summary>
    public static Dictionary<string, object> LinkRequest(string cloudUri)
    {
        var cp = CloudPath.Parse(cloudUri);
        return new Dictionary<string, object> { ["fs"] = cp.Fs, ["remote"] = cp.Path };
    }

    public static async Task<string> PublicLinkAsync(string cloudUri)
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        using var doc = await client.PostAsync("operations/publiclink", LinkRequest(cloudUri)).ConfigureAwait(false);
        if (doc.RootElement.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } link) return link;
        throw new IOException("rclone gave no link. This cloud may not offer share links.");
    }

    /// <summary>Why mounting can't work here, or null when the FUSE layer it needs is installed.</summary>
    public static string? MountProblem()
    {
        if (OperatingSystem.IsLinux())
            return new[] { "/usr/bin/fusermount3", "/bin/fusermount3", "/usr/bin/fusermount", "/bin/fusermount" }.Any(File.Exists)
                ? null : "Mounting needs FUSE (the fuse3 package), which isn't installed.";
        if (OperatingSystem.IsMacOS())
            return Directory.Exists("/Library/Filesystems/macfuse.fs") || File.Exists("/usr/local/lib/libfuse-t.dylib") || File.Exists("/opt/homebrew/lib/libfuse-t.dylib")
                ? null : "Mounting needs macFUSE or FUSE-T, which isn't installed.";
        if (OperatingSystem.IsWindows())
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Directory.Exists(Path.Combine(pf86, "WinFsp")) || Directory.Exists(Path.Combine(pf, "WinFsp"))
                ? null : "Mounting needs WinFsp (winfsp.dev), which isn't installed.";
        }
        return "Mounting isn't available on this system.";
    }

    /// <summary>Where a remote is mounted: an empty folder ~/DORI Mounts/NAME, or on Windows the last free drive letter.</summary>
    public static string MountPointFor(string remote, Func<string, bool>? driveUsed = null)
    {
        if (OperatingSystem.IsWindows())
        {
            driveUsed ??= d => Directory.Exists(d + "\\");
            for (char c = 'Z'; c >= 'D'; c--)
                if (!driveUsed($"{c}:")) return $"{c}:";
            throw new IOException("There's no free drive letter to mount it on.");
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "DORI Mounts", remote);
    }

    public static Dictionary<string, object> MountRequest(string remote, string mountPoint) => new()
    {
        ["fs"] = remote + ":",
        ["mountPoint"] = mountPoint,
        // Files written through the mount are kept on disk until uploaded, so ordinary programs can save into it.
        ["vfsOpt"] = new Dictionary<string, object> { ["CacheMode"] = "writes" },
    };

    public static async Task<string> MountAsync(string remote)
    {
        if (MountProblem() is { } problem) throw new IOException(problem);
        string mountPoint = MountPointFor(remote);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(mountPoint);
            if (Directory.EnumerateFileSystemEntries(mountPoint).Any())
                throw new IOException($"{mountPoint} isn't empty (is it mounted already?).");
        }
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        using var _ = await client.PostAsync("mount/mount", MountRequest(remote, mountPoint)).ConfigureAwait(false);
        return mountPoint;
    }

    public static async Task UnmountAsync(string mountPoint)
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        using var _ = await client.PostAsync("mount/unmount", new Dictionary<string, object> { ["mountPoint"] = mountPoint }).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            try { if (!Directory.EnumerateFileSystemEntries(mountPoint).Any()) Directory.Delete(mountPoint); } catch { }
    }

    /// <summary>What rclone has mounted now: (remote, mount point).</summary>
    public static async Task<List<(string Fs, string MountPoint)>> ListMountsAsync()
    {
        if (!RcloneService.IsRunning) return new();
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        using var doc = await client.PostAsync("mount/listmounts").ConfigureAwait(false);
        return ParseMounts(doc.RootElement);
    }

    public static List<(string Fs, string MountPoint)> ParseMounts(JsonElement root)
    {
        var list = new List<(string, string)>();
        if (root.TryGetProperty("mountPoints", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var m in arr.EnumerateArray())
            {
                string fs = m.TryGetProperty("Fs", out var f) ? f.GetString() ?? "" : "";
                string mp = m.TryGetProperty("MountPoint", out var p) ? p.GetString() ?? "" : "";
                if (mp.Length > 0) list.Add((fs, mp));
            }
        return list;
    }
}
