using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DirOpusReImagined;

/// <summary>
/// Moves files and folders to the operating system's trash / recycle bin (a recoverable delete),
/// with a reliable implementation per platform. Local paths only — callers must not pass cloud or
/// archive URIs. Throws on failure so the caller can surface an error rather than silently losing data.
/// </summary>
public static class TrashService
{
    /// <summary>
    /// Moves <paramref name="path"/> to the trash. Returns where it went, so it can be put back
    /// (<see cref="Restore"/>), or null when the item was trashed but its new location can't be
    /// found (it can then only be put back from the system's own trash window).
    /// </summary>
    public static TrashedItem? Trash(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        bool isDir = Directory.Exists(path);
        DateTime started = DateTime.Now;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return TrashWindows(path, isDir, started);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return TrashMac(path, isDir);
        return TrashLinux(path, isDir, started);
    }

    /// <summary>
    /// Puts a trashed item back where it came from. Never overwrites: throws if something now
    /// exists at the original path, or if the item is no longer in the trash (the trash was emptied
    /// or the item was put back another way).
    /// </summary>
    public static void Restore(TrashedItem item)
    {
        if (!File.Exists(item.TrashedPath) && !Directory.Exists(item.TrashedPath))
            throw new IOException("it is no longer in the trash");
        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
            throw new IOException($"something new is already at {item.OriginalPath}");

        string? parent = Path.GetDirectoryName(item.OriginalPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        if (Directory.Exists(item.TrashedPath)) Directory.Move(item.TrashedPath, item.OriginalPath);
        else File.Move(item.TrashedPath, item.OriginalPath);

        // The record that says where it came from goes too, so the trash doesn't list a ghost.
        if (item.InfoPath != null)
            try { File.Delete(item.InfoPath); } catch { /* the item itself is back; that's what matters */ }
    }

    /// <summary>
    /// True when the OS offers a trash / recycle bin for <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// Network locations do not have one. On Windows there is no Recycle Bin on a UNC path or a
    /// mapped network drive, and <c>SHFileOperation</c> does not fall back to a permanent delete —
    /// it makes a round of blocking network calls per item and then fails. Callers must check this
    /// before offering a "recoverable" delete, or a batch delete on a share will appear to hang.
    /// </remarks>
    public static bool IsSupported(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
        return !IsWindowsNetworkPath(path);
    }

    private static bool IsWindowsNetworkPath(string path)
    {
        // UNC — \\SERVER\SHARE or //SERVER/SHARE.
        if (path.Length >= 2 &&
            (path[0] == '\\' || path[0] == '/') &&
            (path[1] == '\\' || path[1] == '/'))
            return true;

        // A drive letter mapped to a share behaves the same way.
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch { return false; }
    }

    // ---------------- Windows: SHFileOperation with FOF_ALLOWUNDO ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;   // → Recycle Bin
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private static TrashedItem? TrashWindows(string path, bool isDir, DateTime started)
    {
        // Fail fast rather than letting SHFileOperation block on the network and then fail anyway.
        if (IsWindowsNetworkPath(path))
            throw new IOException($"Network locations have no Recycle Bin: {path}");

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            // pFrom is a double-null-terminated list; the LPWStr marshaler preserves the embedded
            // null and appends the terminating one.
            pFrom = path + "\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT),
        };
        int rc = SHFileOperation(ref op);
        if (rc != 0)
            throw new IOException($"Could not move to Recycle Bin (code {rc}): {path}");

        try { return FindInRecycleBin(path, isDir, started); }
        catch { return null; }
    }

    /// <summary>
    /// The Recycle Bin keeps each item as <c>$R…</c> beside a <c>$I…</c> record of its original
    /// path and deletion time, under <c>&lt;drive&gt;\$Recycle.Bin\&lt;user SID&gt;</c>. Only the
    /// current user's folder can be read, so every readable one is searched.
    /// </summary>
    private static TrashedItem? FindInRecycleBin(string original, bool isDir, DateTime started)
    {
        string? root = Path.GetPathRoot(original);
        if (string.IsNullOrEmpty(root)) return null;
        string bin = Path.Combine(root, "$Recycle.Bin");
        var candidates = new List<(string Info, string Path, DateTime Deleted)>();
        foreach (var dir in SafeDirectories(bin))
            foreach (var info in SafeFiles(dir, "$I*"))
            {
                try
                {
                    var rec = TrashRecords.ParseRecycleBinInfo(File.ReadAllBytes(info));
                    if (rec != null) candidates.Add((info, rec.Value.Path, rec.Value.Deleted));
                }
                catch { /* unreadable record: not ours */ }
            }

        var best = TrashRecords.PickNewest(candidates, original, started);
        if (best == null) return null;
        string trashed = Path.Combine(Path.GetDirectoryName(best.Value.Info)!, "$R" + Path.GetFileName(best.Value.Info)[2..]);
        return new TrashedItem(original, trashed, best.Value.Info, isDir);
    }

    // ---------------- macOS: Finder "delete" moves to Trash (with Put Back) ----------------

    private static TrashedItem? TrashMac(string path, bool isDir)
    {
        string esc = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        // Finder's delete returns the item in the Trash, which may have been renamed to avoid a clash.
        string script = $"tell application \"Finder\" to set t to delete (POSIX file \"{esc}\" as alias)\n" +
                        "return POSIX path of (t as alias)";
        if (!TryRun(out string output, "osascript", "-e", script))
            throw new IOException($"Could not move to Trash: {path}");
        string trashed = output.Trim().TrimEnd('/');
        return trashed.Length > 0 ? new TrashedItem(path, trashed, null, isDir) : null;
    }

    // ---------------- Linux: gio / trash-cli, else the freedesktop.org spec ----------------

    private static TrashedItem? TrashLinux(string path, bool isDir, DateTime started)
    {
        if (TryRun(out _, "gio", "trash", "--", path) || TryRun(out _, "trash-put", path))
        {
            try { return FindInFreedesktopTrash(LinuxTrashDirs(path), path, isDir, started); }
            catch { return null; }
        }
        return FreedesktopTrash(path, HomeDataDir());
    }

    private static string HomeDataDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return string.IsNullOrEmpty(dataHome) ? Path.Combine(home, ".local", "share") : dataHome;
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    /// <summary>
    /// Where gio or trash-put may have put <paramref name="path"/>: the home trash, or for another
    /// drive a <c>.Trash-&lt;uid&gt;</c> or <c>.Trash/&lt;uid&gt;</c> folder at the top of that drive.
    /// </summary>
    private static IEnumerable<string> LinuxTrashDirs(string path)
    {
        yield return Path.Combine(HomeDataDir(), "Trash");
        string uid;
        try { uid = GetUid().ToString(); } catch { yield break; }
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            yield return Path.Combine(dir, ".Trash-" + uid);
            yield return Path.Combine(dir, ".Trash", uid);
        }
    }

    /// <summary>Finds the newest record of <paramref name="original"/> trashed since <paramref name="since"/>.</summary>
    internal static TrashedItem? FindInFreedesktopTrash(IEnumerable<string> trashDirs, string original, bool isDir, DateTime since)
    {
        var candidates = new List<(string Info, string Path, DateTime Deleted)>();
        foreach (var trashDir in trashDirs)
        {
            string infoDir = Path.Combine(trashDir, "info");
            if (!Directory.Exists(infoDir)) continue;
            // A trash at the top of a drive may store paths relative to that drive's top folder.
            string topDir = Path.GetFileName(trashDir).StartsWith(".Trash-")
                ? Path.GetDirectoryName(trashDir)!
                : Path.GetDirectoryName(Path.GetDirectoryName(trashDir)!)!;
            foreach (var info in SafeFiles(infoDir, "*.trashinfo"))
            {
                try
                {
                    var rec = TrashRecords.ParseTrashInfo(File.ReadAllText(info));
                    if (rec == null) continue;
                    string p = Path.IsPathRooted(rec.Value.Path) ? rec.Value.Path : Path.Combine(topDir, rec.Value.Path);
                    candidates.Add((info, p, rec.Value.Deleted));
                }
                catch { /* unreadable record: not ours */ }
            }
        }

        var best = TrashRecords.PickNewest(candidates, original, since);
        if (best == null) return null;
        string name = Path.GetFileNameWithoutExtension(best.Value.Info);   // "x.txt.trashinfo" → "x.txt"
        string trashed = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(best.Value.Info)!)!, "files", name);
        return new TrashedItem(original, trashed, best.Value.Info, isDir);
    }

    /// <summary>The freedesktop.org trash, done by hand when neither gio nor trash-put is installed.</summary>
    internal static TrashedItem FreedesktopTrash(string path, string dataHome)
    {
        bool isDir = Directory.Exists(path);
        string trashDir = Path.Combine(dataHome, "Trash");
        string filesDir = Path.Combine(trashDir, "files");
        string infoDir = Path.Combine(trashDir, "info");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        string baseName = Path.GetFileName(path.TrimEnd('/'));
        if (string.IsNullOrEmpty(baseName)) baseName = "item";

        string dest = Path.Combine(filesDir, baseName);
        int counter = 1;
        while (File.Exists(dest) || Directory.Exists(dest))
            dest = Path.Combine(filesDir, $"{baseName}.{counter++}");
        string finalName = Path.GetFileName(dest);

        // Per spec: write the .trashinfo record (Path is percent-encoded, '/' left intact).
        string encoded = Uri.EscapeDataString(path).Replace("%2F", "/");
        string info = "[Trash Info]\n" +
                      "Path=" + encoded + "\n" +
                      "DeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\n";
        File.WriteAllText(Path.Combine(infoDir, finalName + ".trashinfo"), info);

        if (Directory.Exists(path)) Directory.Move(path, dest);
        else File.Move(path, dest);
        return new TrashedItem(path, dest, Path.Combine(infoDir, finalName + ".trashinfo"), isDir);
    }

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static bool TryRun(out string output, string exe, params string[] args)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            var read = proc.StandardOutput.ReadToEndAsync();
            proc.StandardError.ReadToEndAsync();   // drain, so a chatty tool can't block on a full pipe
            if (!proc.WaitForExit(15000)) return false;
            output = read.Result;
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}


/// <summary>
/// A file or folder in the trash: where it came from, where it is now, and (on Linux and Windows)
/// the record beside it that says where it came from.
/// </summary>
public sealed record TrashedItem(string OriginalPath, string TrashedPath, string? InfoPath, bool IsDirectory);

/// <summary>Reading the records the trash keeps about each item. Pure, so it can be tested anywhere.</summary>
public static class TrashRecords
{
    /// <summary>A freedesktop.org <c>.trashinfo</c> file: the original path (percent-decoded) and deletion time.</summary>
    public static (string Path, DateTime Deleted)? ParseTrashInfo(string content)
    {
        string? path = null;
        DateTime deleted = DateTime.MinValue;
        bool inSection = false;
        foreach (var raw in content.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line == "[Trash Info]"; continue; }
            if (!inSection) continue;
            if (line.StartsWith("Path=")) path = Uri.UnescapeDataString(line[5..]);
            else if (line.StartsWith("DeletionDate="))
                DateTime.TryParseExact(line[13..], "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeLocal, out deleted);
        }
        return path == null ? null : (path, deleted);
    }

    /// <summary>
    /// A Windows Recycle Bin <c>$I</c> record. Version 1 (Vista to 8.1) holds the path in a fixed
    /// 520-byte field; version 2 (Windows 10 on) gives its length first.
    /// </summary>
    public static (string Path, DateTime Deleted, long Size)? ParseRecycleBinInfo(byte[] data)
    {
        if (data.Length < 24) return null;
        long version = BitConverter.ToInt64(data, 0);
        long size = BitConverter.ToInt64(data, 8);
        DateTime deleted;
        try { deleted = DateTime.FromFileTime(BitConverter.ToInt64(data, 16)); }
        catch (ArgumentOutOfRangeException) { return null; }

        string path;
        if (version == 1)
        {
            if (data.Length < 24 + 2) return null;
            path = Encoding.Unicode.GetString(data, 24, Math.Min(520, data.Length - 24));
        }
        else if (version == 2)
        {
            if (data.Length < 28) return null;
            int chars = BitConverter.ToInt32(data, 24);
            if (chars <= 0 || 28 + chars * 2 > data.Length) return null;
            path = Encoding.Unicode.GetString(data, 28, chars * 2);
        }
        else return null;

        int nul = path.IndexOf('\0');
        if (nul >= 0) path = path[..nul];
        return path.Length == 0 ? null : (path, deleted, size);
    }

    /// <summary>
    /// The newest record for <paramref name="original"/> made at or after <paramref name="since"/>
    /// (with two seconds' slack, as .trashinfo times have whole seconds). Null if none.
    /// </summary>
    public static (string Info, string Path, DateTime Deleted)? PickNewest(
        IEnumerable<(string Info, string Path, DateTime Deleted)> records, string original, DateTime since)
    {
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string want = original.TrimEnd('/', '\\');
        var match = records
            .Where(r => string.Equals(r.Path.TrimEnd('/', '\\'), want, cmp))
            .Where(r => r.Deleted >= since.AddSeconds(-2))
            .OrderByDescending(r => r.Deleted)
            .ThenByDescending(r => r.Info, StringComparer.Ordinal)
            .FirstOrDefault();
        return match.Info == null ? null : match;
    }
}
