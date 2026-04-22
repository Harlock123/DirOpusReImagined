using System;
using System.Collections.Generic;
using System.IO;

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
        {
            var di = new DirectoryInfo(path);
            return BuildEntry(di.FullName, di.Name, true, 0, di.LastWriteTime, di.Attributes);
        }
        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return BuildEntry(fi.FullName, fi.Name, false, fi.Length, fi.LastWriteTime, fi.Attributes);
        }
        return null;
    }

    public IEnumerable<FileEntry> EnumerateDirectories(string path)
    {
        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            FileEntry? entry = null;
            try
            {
                var di = new DirectoryInfo(dir);
                entry = BuildEntry(di.FullName, di.Name, true, 0, di.LastWriteTime, di.Attributes);
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
                entry = BuildEntry(fi.FullName, fi.Name, false, fi.Length, fi.LastWriteTime, fi.Attributes);
            }
            catch { }
            if (entry is not null) yield return entry;
        }
    }

    private static FileEntry BuildEntry(string path, string name, bool isDir, long size, DateTime mtime, FileAttributes attrs)
        => new(path, name, isDir, size, mtime, ToFlags(attrs), GetAbbreviatedAttributes(attrs));

    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path) => File.OpenWrite(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void CopyFile(string src, string dst, bool overwrite) => File.Copy(src, dst, overwrite);
    public void MoveFile(string src, string dst) => File.Move(src, dst);
    public void MoveDirectory(string src, string dst) => Directory.Move(src, dst);

    public long GetDirectorySize(string path, bool recursive)
    {
        long size = 0;
        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(path, "*", option))
            {
                try { size += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch { }
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

    private static string GetAbbreviatedAttributes(FileAttributes attributes)
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
        if ((attributes & FileAttributes.ReparsePoint)    != 0) s += "RP-";
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
