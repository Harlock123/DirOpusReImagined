using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>One older copy of a file or folder, kept by a snapshot or backup.</summary>
    /// <param name="Path">Where the older copy is (it is read like any file).</param>
    /// <param name="Source">"Snapper", "ZFS", "Timeshift", "Time Machine" or "Shadow Copy".</param>
    public sealed record FileVersion(string Path, DateTime Taken, string Source, string Label, bool IsDirectory, long Size, DateTime Modified);

    /// <summary>
    /// Previous Versions: older copies of a file or folder from the snapshots the system already
    /// keeps, found without changing anything:
    /// <list type="bullet">
    /// <item>Btrfs with snapper: <c>.snapshots/N/snapshot/</c> in the folder at the top of the
    /// subvolume (with the date and description from <c>info.xml</c>).</item>
    /// <item>ZFS: <c>.zfs/snapshot/NAME/</c> at the top of each dataset.</item>
    /// <item>Timeshift: <c>timeshift/snapshots/DATE/localhost/</c> (rsync mode) and
    /// <c>timeshift-btrfs/snapshots/DATE/@</c> or <c>@home</c> (Btrfs mode, while mounted).</item>
    /// <item>macOS: Time Machine backups listed by <c>tmutil listbackups</c>, while the disk is connected.</item>
    /// <item>Windows: shadow copies listed by <c>vssadmin</c>, which needs DORI to run as administrator.</item>
    /// </list>
    /// Snapshots are read-only; restoring copies the old version back.
    /// </summary>
    public static class Snapshots
    {
        /// <summary>Every older copy of <paramref name="path"/>, newest first, leaving out ones the same as the copy after them.</summary>
        public static List<FileVersion> Find(string path)
        {
            var found = new List<FileVersion>();
            try { found.AddRange(FindInFolders(path)); } catch { }
            try { if (OperatingSystem.IsMacOS()) found.AddRange(FindTimeMachine(path)); } catch { }
            try { if (OperatingSystem.IsWindows()) found.AddRange(FindShadowCopies(path)); } catch { }
            return Distinct(found);
        }

        /// <summary>Newest first, dropping a version whose size and time match the next newer one (an unchanged file across snapshots).</summary>
        public static List<FileVersion> Distinct(IEnumerable<FileVersion> versions)
        {
            var list = new List<FileVersion>();
            foreach (var v in versions.OrderByDescending(v => v.Taken))
            {
                var prev = list.LastOrDefault();
                if (prev != null && !v.IsDirectory && prev.Size == v.Size && prev.Modified == v.Modified) continue;
                list.Add(v);
            }
            return list;
        }

        /// <summary>The snapshot folders found by walking up from <paramref name="path"/> (snapper, ZFS, Timeshift).</summary>
        public static IEnumerable<FileVersion> FindInFolders(string path)
        {
            string full = System.IO.Path.GetFullPath(path).TrimEnd('/', '\\');
            if (full.Length == 0) full = "/";
            for (string? dir = System.IO.Path.GetDirectoryName(full); !string.IsNullOrEmpty(dir); dir = System.IO.Path.GetDirectoryName(dir))
            {
                string rel = System.IO.Path.GetRelativePath(dir, full);

                // snapper: DIR/.snapshots/N/snapshot/REL
                foreach (var snap in SafeDirs(System.IO.Path.Combine(dir, ".snapshots")))
                {
                    string root = System.IO.Path.Combine(snap, "snapshot");
                    var (taken, label) = SnapperInfo(snap);
                    if (Version(System.IO.Path.Combine(root, rel), taken, "Snapper", label) is { } v) yield return v;
                }

                // ZFS: DIR/.zfs/snapshot/NAME/REL
                foreach (var snap in SafeDirs(System.IO.Path.Combine(dir, ".zfs", "snapshot")))
                {
                    string name = System.IO.Path.GetFileName(snap);
                    DateTime taken = NameDate(name) ?? SafeTime(snap);
                    if (Version(System.IO.Path.Combine(snap, rel), taken, "ZFS", name) is { } v) yield return v;
                }

                // Timeshift (rsync): DIR/timeshift/snapshots/DATE/localhost/ is a copy of /.
                if (dir == "/" || dir.Length <= 3)
                    foreach (var top in new[] { System.IO.Path.Combine(dir, "timeshift", "snapshots") }.Concat(TimeshiftMounts()))
                        foreach (var snap in SafeDirs(top))
                        {
                            DateTime taken = NameDate(System.IO.Path.GetFileName(snap)) ?? SafeTime(snap);
                            string from = full.TrimStart('/');
                            foreach (var sub in new[] { "localhost", "@" })
                                if (Version(System.IO.Path.Combine(snap, sub, from), taken, "Timeshift", System.IO.Path.GetFileName(snap)) is { } v) yield return v;
                            // Btrfs mode keeps /home in a separate @home.
                            if (full.StartsWith("/home/") && Version(System.IO.Path.Combine(snap, "@home", full["/home/".Length..]), taken, "Timeshift", System.IO.Path.GetFileName(snap)) is { } h)
                                yield return h;
                        }
            }
        }

        private static IEnumerable<string> TimeshiftMounts()
        {
            if (!OperatingSystem.IsLinux()) yield break;
            foreach (var m in new[] { "/run/timeshift/backup/timeshift-btrfs/snapshots", "/run/timeshift/backup/timeshift/snapshots" })
                if (Directory.Exists(m)) yield return m;
        }

        private static FileVersion? Version(string candidate, DateTime taken, string source, string label)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var fi = new FileInfo(candidate);
                    return new FileVersion(candidate, taken, source, label, false, fi.Length, fi.LastWriteTime);
                }
                if (Directory.Exists(candidate))
                    return new FileVersion(candidate, taken, source, label, true, 0, Directory.GetLastWriteTime(candidate));
            }
            catch { }
            return null;
        }

        /// <summary>snapper's info.xml: &lt;date&gt; (UTC) and &lt;description&gt;.</summary>
        internal static (DateTime Taken, string Label) SnapperInfo(string snapDir)
        {
            string number = System.IO.Path.GetFileName(snapDir);
            try
            {
                var doc = XDocument.Load(System.IO.Path.Combine(snapDir, "info.xml"));
                var root = doc.Root!;
                string? date = root.Element("date")?.Value;
                string desc = root.Element("description")?.Value ?? "";
                if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
                    return (utc.ToLocalTime(), $"#{number}" + (desc.Length > 0 ? " " + desc : ""));
            }
            catch { }
            return (SafeTime(snapDir), "#" + number);
        }

        private static readonly Regex DateInName = new(@"(\d{4})-?(\d{2})-?(\d{2})[_T\-\s]?(\d{2})?[:\-]?(\d{2})?[:\-]?(\d{2})?", RegexOptions.Compiled);

        /// <summary>A date in a snapshot's name: "2024-01-02_03-04-05", "autosnap_2024-01-02_00:00:01_daily", "20240102".</summary>
        public static DateTime? NameDate(string name)
        {
            var m = DateInName.Match(name ?? "");
            if (!m.Success) return null;
            int G(int i, int fallback) => m.Groups[i].Success && m.Groups[i].Value.Length > 0 ? int.Parse(m.Groups[i].Value) : fallback;
            try { return new DateTime(G(1, 1), G(2, 1), G(3, 1), G(4, 0), G(5, 0), G(6, 0)); }
            catch { return null; }
        }

        // ---- macOS Time Machine ----

        private static IEnumerable<FileVersion> FindTimeMachine(string path)
        {
            if (!Run("tmutil", out var output, "listbackups")) yield break;
            string full = System.IO.Path.GetFullPath(path);
            foreach (var backup in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                DateTime taken = NameDate(System.IO.Path.GetFileName(backup)) ?? SafeTime(backup);
                // Each backup holds the volumes it copied by name; the start-up disk's data is under "<name> - Data" on newer macOS.
                foreach (var volume in SafeDirs(backup))
                {
                    string rel = full.StartsWith("/Volumes/") ? string.Join('/', full.Split('/').Skip(3)) : full.TrimStart('/');
                    if (Version(System.IO.Path.Combine(volume, rel), taken, "Time Machine", System.IO.Path.GetFileName(backup)) is { } v)
                    {
                        yield return v;
                        break;
                    }
                }
            }
        }

        // ---- Windows shadow copies ----

        private static readonly Regex ShadowVolume = new(@"Shadow Copy Volume:\s*(\\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+)", RegexOptions.Compiled);
        private static readonly Regex ShadowCreated = new(@"creation time:\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
        private static readonly Regex ShadowOriginal = new(@"Original Volume:\s*\((\w:)\)", RegexOptions.Compiled);

        /// <summary>vssadmin's list of shadow copies: each one's device, the drive it copies and when.</summary>
        public static List<(string Device, string Drive, DateTime Taken)> ParseVssadmin(string text)
        {
            var list = new List<(string, string, DateTime)>();
            foreach (var block in Regex.Split(text, @"(?=Contents of shadow copy set ID)"))
            {
                var vol = ShadowVolume.Match(block);
                var orig = ShadowOriginal.Match(block);
                if (!vol.Success || !orig.Success) continue;
                var created = ShadowCreated.Match(block);
                DateTime taken = created.Success && DateTime.TryParse(created.Groups[1].Value.Trim(), out var t) ? t : DateTime.MinValue;
                list.Add((vol.Groups[1].Value, orig.Groups[1].Value.ToUpperInvariant(), taken));
            }
            return list;
        }

        private static IEnumerable<FileVersion> FindShadowCopies(string path)
        {
            if (!Run("vssadmin", out var output, "list", "shadows")) yield break;
            string full = System.IO.Path.GetFullPath(path);
            string drive = full[..2].ToUpperInvariant();
            foreach (var (device, d, taken) in ParseVssadmin(output))
                if (d == drive && Version(device + full[2..], taken, "Shadow Copy", taken.ToString("g")) is { } v)
                    yield return v;
        }

        // ---- helpers ----

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        private static DateTime SafeTime(string path)
        {
            try { return Directory.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
        }

        private static bool Run(string exe, out string output, params string[] args)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return false;
                var read = p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(20000)) return false;
                output = read.Result;
                return p.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
