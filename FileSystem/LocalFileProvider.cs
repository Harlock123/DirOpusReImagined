using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

public sealed class LocalFileProvider : IFileProvider
{
    public bool CanHandle(string path) => true;
    public bool IsRemote => false;

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileEntry? Stat(string path)
    {
        if (Directory.Exists(path))
            return BuildEntry(new DirectoryInfo(path), true, 0);
        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return BuildEntry(fi, false, fi.Length);
        }
        // A broken link: File.Exists and Directory.Exists follow it and find nothing, but the link
        // itself is there and should be listed (and can be deleted, moved or fixed).
        var link = new FileInfo(path);
        if (link.LinkTarget != null) return BuildEntry(link, false, link.Length);
        return null;
    }

    public IEnumerable<FileEntry> EnumerateDirectories(string path)
    {
        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            FileEntry? entry = null;
            try
            {
                entry = BuildEntry(new DirectoryInfo(dir), true, 0);
            }
            catch { }
            if (entry is not null) yield return entry;
        }
    }

    public IEnumerable<FileEntry> EnumerateFiles(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path))
        {
            FileEntry? entry = null;
            try
            {
                var fi = new FileInfo(file);
                entry = BuildEntry(fi, false, fi.Length);
            }
            catch { }
            if (entry is not null) yield return entry;
        }
    }

    /// <summary>
    /// The entry for a file or folder. A link's size and date are the link's own (as <c>ls -l</c>
    /// shows them), and <see cref="FileEntry.LinkTarget"/> holds where it points.
    /// </summary>
    private static FileEntry BuildEntry(FileSystemInfo info, bool isDir, long size)
    {
        string? target = null;
        try { target = info.LinkTarget; } catch { /* not readable: treat as a plain entry */ }

        var attrs = info.Attributes;
        var flags = ToFlags(attrs);
        if (target != null) flags |= FileEntryFlags.Symlink;
        string attrText = GetAbbreviatedAttributes(attrs, isLink: target != null);
        return new FileEntry(info.FullName, info.Name, isDir, size, info.LastWriteTime, flags, attrText, target);
    }

    /// <summary>True if <paramref name="path"/> is itself a symbolic link (or junction), whether or not its target exists.</summary>
    public static bool IsLink(string path)
    {
        try { return new FileInfo(path).LinkTarget != null; }
        catch { return false; }
    }

    /// <summary>
    /// The full path a link points to (resolving a relative target against the link's folder), or
    /// null if <paramref name="path"/> isn't a link.
    /// </summary>
    public static string? ResolveLink(string path)
    {
        try
        {
            string? target = new FileInfo(path).LinkTarget;
            if (target == null) return null;
            string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            return Path.GetFullPath(Path.Combine(folder, target));
        }
        catch { return null; }
    }

    /// <summary>
    /// Makes a symbolic link at <paramref name="linkPath"/> pointing to <paramref name="target"/>
    /// (written as given, so a relative target stays relative). On Windows this needs Developer
    /// Mode or administrator rights; the exception says so.
    /// </summary>
    public static void CreateLink(string linkPath, string target, bool isDirectory)
    {
        try
        {
            if (isDirectory) Directory.CreateSymbolicLink(linkPath, target);
            else File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
        {
            throw new IOException(
                "Windows only lets programs make symbolic links with Developer Mode turned on " +
                "(Settings → System → For developers) or when running as administrator. " + ex.Message, ex);
        }
    }

    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path) => File.OpenWrite(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void CopyFile(string src, string dst, bool overwrite) => File.Copy(src, dst, overwrite);

    public Task CopyFileAsync(string src, string dst, bool overwrite,
                              IProgress<TransferProgress>? progress, CancellationToken ct = default)
        => StreamCopy.LocalToLocalAsync(src, dst, overwrite, progress, ct);
    public void MoveFile(string src, string dst) => File.Move(src, dst);
    public void MoveDirectory(string src, string dst) => Directory.Move(src, dst);

    public string? ComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var md5 = System.Security.Cryptography.MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(stream));
        }
        catch { return null; }
    }

    /// <summary>
    /// Total size of the files in a folder (and below it, if <paramref name="recursive"/>). Linked
    /// folders inside aren't followed — a link back to a parent would never end, and a link
    /// elsewhere isn't this folder's content. (<see cref="SearchOption.AllDirectories"/> does follow
    /// them, which is why this walks the tree itself.)
    /// </summary>
    public long GetDirectorySize(string path, bool recursive)
    {
        long size = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try { size += new FileInfo(file).Length; }
                    catch { }
                }
                if (!recursive) break;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    if (!IsLink(sub)) pending.Push(sub);
            }
            catch { /* unreadable folder: count what we can */ }
        }
        return size;
    }

    private static FileEntryFlags ToFlags(FileAttributes a)
    {
        var f = FileEntryFlags.None;
        if (a.HasFlag(FileAttributes.Hidden))    f |= FileEntryFlags.Hidden;
        if (a.HasFlag(FileAttributes.ReadOnly))  f |= FileEntryFlags.ReadOnly;
        if (a.HasFlag(FileAttributes.System))    f |= FileEntryFlags.System;
        if (a.HasFlag(FileAttributes.Archive))   f |= FileEntryFlags.Archive;
        return f;
    }

    private static string GetAbbreviatedAttributes(FileAttributes attributes, bool isLink = false)
    {
        string s = string.Empty;
        s += (attributes & FileAttributes.ReadOnly) != 0 ? "RO-" : "RW-";
        s += (attributes & FileAttributes.Hidden)   != 0 ? "H-"  : "V-";
        if ((attributes & FileAttributes.System)          != 0) s += "S-";
        if ((attributes & FileAttributes.Directory)       != 0) s += "D-";
        if ((attributes & FileAttributes.Archive)         != 0) s += "A-";
        if ((attributes & FileAttributes.Device)          != 0) s += "DEV-";
        if ((attributes & FileAttributes.Normal)          != 0) s += "N-";
        if ((attributes & FileAttributes.Temporary)       != 0) s += "T-";
        if ((attributes & FileAttributes.SparseFile)      != 0) s += "SF-";
        if (isLink) s += "LNK-";
        else if ((attributes & FileAttributes.ReparsePoint) != 0) s += "RP-";
        if ((attributes & FileAttributes.Compressed)      != 0) s += "C-";
        if ((attributes & FileAttributes.Offline)         != 0) s += "O-";
        if ((attributes & FileAttributes.NotContentIndexed) != 0) s += "NCI-";
        if ((attributes & FileAttributes.Encrypted)       != 0) s += "E-";
        if ((attributes & FileAttributes.IntegrityStream) != 0) s += "IS-";
        if ((attributes & FileAttributes.NoScrubData)     != 0) s += "NSD-";
        if (s.EndsWith("-")) s = s.Substring(0, s.Length - 1);
        return s.Trim();
    }
}
