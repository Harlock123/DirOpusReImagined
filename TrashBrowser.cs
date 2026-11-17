using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DirOpusReImagined
{
    /// <summary>
    /// One item in the system trash. <see cref="OriginalPath"/> is empty when the trash doesn't
    /// say where it came from (macOS keeps that in Finder's own files), and it then can't be put back here.
    /// </summary>
    public sealed record TrashEntry(string Name, string OriginalPath, string TrashedPath, string? InfoPath,
                                    DateTime Deleted, long Size, bool IsDirectory)
    {
        public bool CanRestore => OriginalPath.Length > 0;
        public TrashedItem ToItem() => new(OriginalPath, TrashedPath, InfoPath, IsDirectory);
        public string OriginalFolder => CanRestore ? Path.GetDirectoryName(OriginalPath) ?? "" : "";
    }

    /// <summary>
    /// Lists what is in the system trash and empties it: the freedesktop.org trash on Linux (the
    /// home trash and the .Trash-uid folders on other drives), the Recycle Bin on Windows (each
    /// drive's $Recycle.Bin folders this user can read) and ~/.Trash on macOS. Putting items back
    /// uses <see cref="TrashService.Restore"/>, which never overwrites.
    /// </summary>
    public static class TrashBrowser
    {
        public static List<TrashEntry> List()
        {
            var all = new List<TrashEntry>();
            try
            {
                if (OperatingSystem.IsWindows()) all.AddRange(ListWindows());
                else if (OperatingSystem.IsMacOS()) all.AddRange(ListMac(Path.Combine(Home, ".Trash")));
                else foreach (var (dir, top) in LinuxTrashDirs()) all.AddRange(ListFreedesktop(dir, top));
            }
            catch { /* an unreadable trash lists as empty rather than failing */ }
            return all.OrderByDescending(e => e.Deleted).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ---- Linux ----

        [DllImport("libc", EntryPoint = "getuid")]
        private static extern uint GetUid();

        /// <summary>The home trash, then the trash folders at the top of each mounted drive.</summary>
        private static IEnumerable<(string Dir, string? Top)> LinuxTrashDirs()
        {
            string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            yield return (Path.Combine(string.IsNullOrEmpty(dataHome) ? Path.Combine(Home, ".local", "share") : dataHome, "Trash"), null);
            string uid;
            try { uid = GetUid().ToString(); } catch { yield break; }
            foreach (var mount in Mounts())
            {
                yield return (Path.Combine(mount, ".Trash-" + uid), mount);
                yield return (Path.Combine(mount, ".Trash", uid), mount);
            }
        }

        /// <summary>Mount points of real drives, from /proc/mounts (not /proc, /sys and the like).</summary>
        internal static IEnumerable<string> Mounts()
        {
            string[] lines;
            try { lines = File.ReadAllLines("/proc/mounts"); } catch { yield break; }
            foreach (var line in lines)
            {
                var parts = line.Split(' ');
                if (parts.Length < 3 || !parts[0].StartsWith('/')) continue;   // /dev/sda1, not proc or tmpfs
                string mount = parts[1].Replace("\\040", " ");
                if (mount == "/" || mount.StartsWith("/boot")) continue;
                yield return mount;
            }
        }

        /// <summary>
        /// The items of one freedesktop.org trash folder (files/ beside info/*.trashinfo).
        /// <paramref name="top"/> is the drive the trash belongs to; its records may give paths relative to it.
        /// </summary>
        internal static List<TrashEntry> ListFreedesktop(string trashDir, string? top)
        {
            var list = new List<TrashEntry>();
            string infoDir = Path.Combine(trashDir, "info"), filesDir = Path.Combine(trashDir, "files");
            if (!Directory.Exists(filesDir)) return list;
            foreach (var item in SafeEntries(filesDir))
            {
                string name = Path.GetFileName(item);
                string info = Path.Combine(infoDir, name + ".trashinfo");
                string original = "";
                DateTime deleted = SafeTime(item);
                if (File.Exists(info))
                {
                    try
                    {
                        var rec = TrashRecords.ParseTrashInfo(File.ReadAllText(info));
                        if (rec != null)
                        {
                            original = Path.IsPathRooted(rec.Value.Path) || top == null ? rec.Value.Path : Path.Combine(top, rec.Value.Path);
                            if (rec.Value.Deleted != DateTime.MinValue) deleted = rec.Value.Deleted;
                        }
                    }
                    catch { }
                }
                bool isDir = IsRealDirectory(item);
                list.Add(new TrashEntry(original.Length > 0 ? Path.GetFileName(original.TrimEnd('/')) : name, original, item,
                                        File.Exists(info) ? info : null, deleted, isDir ? -1 : SafeLength(item), isDir));
            }
            return list;
        }

        // ---- Windows ----

        private static IEnumerable<TrashEntry> ListWindows()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string bin;
                try
                {
                    if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    bin = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                }
                catch { continue; }
                foreach (var user in SafeDirs(bin))
                    foreach (var e in ListRecycleBin(user)) yield return e;
            }
        }

        /// <summary>The items of one user's Recycle Bin folder: each $R… item and the $I… record beside it.</summary>
        internal static List<TrashEntry> ListRecycleBin(string userBin)
        {
            var list = new List<TrashEntry>();
            foreach (var info in SafeFiles(userBin, "$I*"))
            {
                try
                {
                    var rec = TrashRecords.ParseRecycleBinInfo(File.ReadAllBytes(info));
                    if (rec == null) continue;
                    string item = Path.Combine(userBin, "$R" + Path.GetFileName(info)[2..]);
                    bool isDir = Directory.Exists(item);
                    if (!isDir && !File.Exists(item)) continue;
                    list.Add(new TrashEntry(Path.GetFileName(rec.Value.Path.TrimEnd('\\')), rec.Value.Path, item, info,
                                            rec.Value.Deleted, isDir ? -1 : rec.Value.Size, isDir));
                }
                catch { }
            }
            return list;
        }

        // ---- macOS ----

        internal static List<TrashEntry> ListMac(string trash)
        {
            var list = new List<TrashEntry>();
            foreach (var item in SafeEntries(trash))
            {
                string name = Path.GetFileName(item);
                if (name == ".DS_Store" || name == ".localized") continue;
                bool isDir = IsRealDirectory(item);
                list.Add(new TrashEntry(name, "", item, null, SafeTime(item), isDir ? -1 : SafeLength(item), isDir));
            }
            return list;
        }

        // ---- Changing the trash ----

        /// <summary>Deletes a trashed item for good (and its record). Links inside are removed, never followed.</summary>
        public static void DeleteForever(TrashEntry e)
        {
            if (IsRealDirectory(e.TrashedPath)) Directory.Delete(e.TrashedPath, recursive: true);
            else if (File.Exists(e.TrashedPath) || IsLink(e.TrashedPath)) File.Delete(e.TrashedPath);
            if (e.InfoPath != null) try { File.Delete(e.InfoPath); } catch { }
        }

        /// <summary>
        /// Empties the trash. On macOS Finder is asked to do it (the Trash folder itself is
        /// protected); elsewhere each item is deleted. Returns the items that couldn't be.
        /// </summary>
        public static List<string> Empty(IEnumerable<TrashEntry> items)
        {
            var failed = new List<string>();
            if (OperatingSystem.IsMacOS() && RunFinderEmpty()) return failed;
            foreach (var e in items)
            {
                try { DeleteForever(e); }
                catch (Exception ex) { failed.Add($"{e.Name}: {ex.Message}"); }
            }
            return failed;
        }

        private static bool RunFinderEmpty()
        {
            try
            {
                var psi = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add("tell application \"Finder\" to empty trash");
                using var p = Process.Start(psi);
                return p != null && p.WaitForExit(60000) && p.ExitCode == 0;
            }
            catch { return false; }
        }

        // ---- helpers ----

        private static bool IsLink(string path)
        {
            try { return new FileInfo(path).LinkTarget != null; } catch { return false; }
        }

        /// <summary>A folder that isn't a link to one (a link is deleted as a file, never walked into).</summary>
        private static bool IsRealDirectory(string path)
        {
            try { var d = new DirectoryInfo(path); return d.Exists && d.LinkTarget == null; } catch { return false; }
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        private static DateTime SafeTime(string path)
        {
            try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
        }

        private static IEnumerable<string> SafeEntries(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeFiles(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }
    }
}
