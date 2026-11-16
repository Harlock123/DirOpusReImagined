using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>
/// Disk images (.iso) browsed like archives, read-only: CD and DVD images in ISO 9660 (with Joliet
/// long names) or UDF, through DiscUtils. A DVD image carrying both reads its UDF side, which has
/// the full names. Files are read straight out of the image; nothing is unpacked first.
/// </summary>
public static class DiscImage
{
    private static readonly string[] Extensions = { ".iso" };

    public static bool IsDiscImage(string path)
    {
        foreach (var e in Extensions)
            if (path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Opens the image's file system (UDF when it has one, else ISO 9660). The caller disposes both.</summary>
    private static (Stream File, DiscFileSystem Fs) Open(string imagePath)
    {
        var file = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (UdfReader.Detect(file))
            {
                file.Position = 0;
                return (file, new UdfReader(file));
            }
            file.Position = 0;
            if (CDReader.Detect(file))
            {
                file.Position = 0;
                return (file, new CDReader(file, joliet: true));
            }
            throw new InvalidDataException($"{Path.GetFileName(imagePath)} isn't a CD or DVD image DORI can read (ISO 9660 or UDF).");
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Every file and folder in the image: its path ('/'-separated, no leading slash), whether it is a folder, size and date.</summary>
    public static List<(string Key, bool IsDir, long Size, DateTime Modified)> List(string imagePath)
    {
        var (file, fs) = Open(imagePath);
        using (file)
        using (fs)
        {
            var list = new List<(string, bool, long, DateTime)>();
            var pending = new Stack<DiscDirectoryInfo>();
            pending.Push(fs.Root);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var f in dir.GetFiles())
                    list.Add((Key(f.FullName), false, f.Length, Safe(() => f.LastWriteTime)));
                foreach (var d in dir.GetDirectories())
                {
                    list.Add((Key(d.FullName), true, 0, Safe(() => d.LastWriteTime)));
                    pending.Push(d);
                }
            }
            return list;
        }
    }

    /// <summary>A stream of one file in the image; disposing it closes the image.</summary>
    public static Stream OpenFile(string imagePath, string key)
    {
        var (file, fs) = Open(imagePath);
        try
        {
            string discPath = "\\" + key.Replace('/', '\\');
            if (!fs.FileExists(discPath))
            {
                // ISO 9660 names may carry a ";1" version the listing didn't show.
                if (fs.FileExists(discPath + ";1")) discPath += ";1";
                else throw new FileNotFoundException($"Not in the image: {key}");
            }
            return new Owner(fs.OpenFile(discPath, FileMode.Open, FileAccess.Read), fs, file);
        }
        catch
        {
            fs.Dispose();
            file.Dispose();
            throw;
        }
    }

    /// <summary>"\DOCS\README.TXT;1" → "DOCS/README.TXT".</summary>
    internal static string Key(string fullName)
    {
        string k = fullName.Replace('\\', '/').Trim('/');
        int semi = k.LastIndexOf(';');
        if (semi > 0 && semi > k.LastIndexOf('/') && int.TryParse(k[(semi + 1)..], out _)) k = k[..semi];
        if (k.EndsWith('.') && !k.EndsWith("..")) k = k.TrimEnd('.');   // ISO 9660 writes "NAME." for names without an extension
        return k;
    }

    private static DateTime Safe(Func<DateTime> get)
    {
        try { return get(); } catch { return DateTime.MinValue; }
    }

    private sealed class Owner : Stream
    {
        private readonly Stream _inner;
        private readonly IDisposable _fs, _file;
        public Owner(Stream inner, IDisposable fs, IDisposable file) { _inner = inner; _fs = fs; _file = file; }
        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _inner.Dispose(); _fs.Dispose(); _file.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
