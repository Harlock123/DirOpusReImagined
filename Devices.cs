using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>A drive or mounted volume worth showing: where it is, its name, size and free space, and whether it can be ejected.</summary>
    public sealed record DeviceInfo(string Root, string Label, DriveType Type, long Total, long Free, bool CanEject, string? Device)
    {
        public long Used => Math.Max(0, Total - Free);
        public double UsedFraction => Total > 0 ? (double)Used / Total : 0;

        /// <summary>"USB stick (/run/media/me/USB)" or "/" or "C:\ (Windows)".</summary>
        public string Display => string.IsNullOrEmpty(Label) || Label == Root ? Root : $"{Label}   ({Root})";

        public string Kind => Type switch
        {
            DriveType.Removable => "Removable",
            DriveType.Network => "Network",
            DriveType.CDRom => "Optical",
            DriveType.Ram => "Memory",
            _ => CanEject ? "Removable" : "Fixed",
        };
    }

    /// <summary>
    /// Drives and mounted volumes for the drive menu and the Drives and Devices window, and ejecting
    /// them: <c>udisksctl</c> (unmount, then power off) or <c>gio mount -e</c> on Linux,
    /// <c>diskutil eject</c> on macOS, and the shell's Eject verb on Windows.
    /// </summary>
    public static class Devices
    {
        private static readonly string[] UnixUserMounts = { "/Volumes/", "/media/", "/mnt/", "/run/media/" };
        private static readonly string[] SystemMounts = { "/proc", "/sys", "/dev", "/run/", "/boot", "/snap/", "/var/lib/", "/System/" };

        public static List<DeviceInfo> List()
        {
            var devices = new List<DeviceInfo>();
            var mounts = OperatingSystem.IsLinux() ? LinuxMountDevices() : new Dictionary<string, string>();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { return devices; }
            foreach (var d in drives)
            {
                try
                {
                    if (!IsUserDrive(d)) continue;
                    string root = d.Name;
                    if (devices.Any(x => x.Root == root)) continue;   // the same place mounted twice
                    string label = "";
                    try { label = d.VolumeLabel; } catch { }
                    if (string.IsNullOrEmpty(label) && root != "/" && !OperatingSystem.IsWindows())
                        label = Path.GetFileName(root.TrimEnd('/'));
                    mounts.TryGetValue(root.TrimEnd('/').Length == 0 ? "/" : root.TrimEnd('/'), out string? device);
                    // A network share that has gone away can stall a size query, so shares show no sizes.
                    bool network = d.DriveType == DriveType.Network;
                    devices.Add(new DeviceInfo(root, label, d.DriveType, network ? 0 : d.TotalSize, network ? 0 : d.AvailableFreeSpace,
                                               CanEject(root, d.DriveType), device));
                }
                catch { /* a drive that errors while being looked at is left out */ }
            }
            return devices;
        }

        /// <summary>
        /// Whether a drive is worth offering. On Windows that's any ready fixed, removable, network
        /// or optical drive. On Unix it's the root plus real user mounts (under /Volumes, /media,
        /// /mnt, /run/media) and network shares, not system volumes like /dev or /System/Volumes/*.
        /// </summary>
        public static bool IsUserDrive(DriveInfo d)
        {
            if (!d.IsReady) return false;
            if (OperatingSystem.IsWindows())
                return d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network or DriveType.CDRom;
            return IsUserMount(d.Name, d.DriveType);
        }

        public static bool IsUserMount(string name, DriveType type)
        {
            if (name == "/") return true;
            // System mounts (/proc/sys/fs/binfmt_misc is an autofs that .NET calls "network") are never drives.
            if (SystemMounts.Any(p => name.StartsWith(p, StringComparison.Ordinal)) && !name.StartsWith("/run/media/", StringComparison.Ordinal))
                return false;
            if (type == DriveType.Network) return true;
            return UnixUserMounts.Any(p => name.StartsWith(p, StringComparison.Ordinal));
        }

        /// <summary>Drives a person plugs in and takes out: USB sticks, card readers, discs, and anything mounted under /media or /Volumes.</summary>
        public static bool CanEject(string root, DriveType type)
        {
            if (OperatingSystem.IsWindows()) return type is DriveType.Removable or DriveType.CDRom;
            if (root == "/") return false;
            if (OperatingSystem.IsMacOS()) return root.StartsWith("/Volumes/", StringComparison.Ordinal);
            return type is DriveType.Removable or DriveType.CDRom
                   || root.StartsWith("/media/", StringComparison.Ordinal) || root.StartsWith("/run/media/", StringComparison.Ordinal);
        }

        /// <summary>Mount point → device (/dev/sdb1), from /proc/mounts.</summary>
        internal static Dictionary<string, string> LinuxMountDevices()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var line in File.ReadAllLines("/proc/mounts"))
                {
                    var parts = line.Split(' ');
                    if (parts.Length < 2 || !parts[0].StartsWith("/dev/")) continue;
                    map[UnescapeMount(parts[1])] = parts[0];
                }
            }
            catch { }
            return map;
        }

        /// <summary>/proc/mounts writes spaces, tabs and backslashes in octal ("\040").</summary>
        public static string UnescapeMount(string s)
        {
            if (!s.Contains('\\')) return s;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && IsOctal(s, i + 1))
                {
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 3), 8));
                    i += 3;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static bool IsOctal(string s, int at)
            => at + 3 <= s.Length && s.Substring(at, 3).All(c => c is >= '0' and <= '7');

        /// <summary>
        /// The roots of drives that can be ejected, found cheaply (no size queries, which can hang
        /// on a network share that has gone away), for spotting one being plugged in.
        /// </summary>
        public static HashSet<string> EjectableRoots()
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    foreach (var mount in LinuxMountDevices().Keys)
                        if (CanEject(mount + "/", DriveType.Unknown)) roots.Add(mount + "/");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    foreach (var dir in System.IO.Directory.GetDirectories("/Volumes"))
                        if (new DirectoryInfo(dir).LinkTarget == null) roots.Add(dir + "/");
                }
                else
                {
                    foreach (var d in DriveInfo.GetDrives())
                        if (d.DriveType is DriveType.Removable or DriveType.CDRom && d.IsReady) roots.Add(d.Name);
                }
            }
            catch { }
            return roots;
        }

        /// <summary>The drive (from <see cref="List"/>) whose root is <paramref name="root"/>, compared without a trailing slash.</summary>
        public static DeviceInfo? Find(string root)
            => List().FirstOrDefault(d => d.Root.TrimEnd('/', '\\') == root.TrimEnd('/', '\\'));

        /// <summary>Ejects (or unmounts) a drive. Returns null when it went, else what went wrong.</summary>
        public static string? Eject(DeviceInfo d)
        {
            if (OperatingSystem.IsWindows())
            {
                string letter = d.Root.TrimEnd('\\');
                string script = $"(New-Object -ComObject Shell.Application).Namespace(17).ParseName('{letter.Replace("'", "''")}').InvokeVerb('Eject')";
                return Run("powershell", out var err, "-NoProfile", "-NonInteractive", "-Command", script) ? null : Fail(err);
            }
            if (OperatingSystem.IsMacOS())
                return Run("diskutil", out var err, "eject", d.Root) ? null : Fail(err);

            string mount = d.Root.TrimEnd('/');
            string udErr = "";
            if (d.Device != null && Run("udisksctl", out udErr, "unmount", "-b", d.Device))
            {
                // Powering off makes a USB stick safe to pull out; a disc or card reader may not allow it, which is fine.
                string? whole = WholeDisk(d.Device);
                if (whole != null) Run("udisksctl", out _, "power-off", "-b", whole);
                return null;
            }
            if (Run("gio", out var gioErr, "mount", "-e", mount)) return null;
            if (Run("umount", out var umErr, mount)) return null;
            return Fail(d.Device != null ? udErr : gioErr.Length > 0 ? gioErr : umErr);
        }

        /// <summary>/dev/sdb1 → /dev/sdb, /dev/mmcblk0p1 → /dev/mmcblk0, /dev/nvme0n1p2 → /dev/nvme0n1.</summary>
        public static string? WholeDisk(string device)
        {
            if (!device.StartsWith("/dev/")) return null;
            string name = device[5..];
            if (name.StartsWith("mmcblk") || name.StartsWith("nvme"))
            {
                int p = name.LastIndexOf('p');
                return p > 0 && p + 1 < name.Length && name[(p + 1)..].All(char.IsDigit) ? "/dev/" + name[..p] : device;
            }
            if (name.StartsWith("sd") || name.StartsWith("vd") || name.StartsWith("hd"))
                return "/dev/" + name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return null;
        }

        private static string Fail(string err) => string.IsNullOrWhiteSpace(err) ? "the system wouldn't eject it (is a program using it?)" : err.Trim();

        private static bool Run(string exe, out string error, params string[] args)
        {
            error = "";
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.StandardOutput.ReadToEndAsync();
                var err = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } error = "it took too long"; return false; }
                error = err.Result;
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Watches for removable drives being plugged in (every few seconds, off the UI thread) and
    /// raises <see cref="Added"/> with each new one's root. Drives already there when it starts
    /// are not reported.
    /// </summary>
    public sealed class DriveWatcher : IDisposable
    {
        public const string Setting = "AskOnNewDrive";
        private readonly System.Threading.Timer _timer;
        private HashSet<string>? _known;
        private int _busy;

        /// <summary>Raised on a worker thread with the new drive's root.</summary>
        public event Action<string>? Added;

        public DriveWatcher(TimeSpan every)
        {
            _timer = new System.Threading.Timer(_ => Look(), null, TimeSpan.Zero, every);
        }

        private void Look()
        {
            if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var now = Devices.EjectableRoots();
                if (_known != null)
                    foreach (var root in now)
                        if (!_known.Contains(root)) Added?.Invoke(root);
                _known = now;
            }
            catch { }
            finally { System.Threading.Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Whether to offer to open a drive when it's plugged in (on unless turned off).</summary>
        public static bool AskOnPlugIn
        {
            get => !string.Equals(ConfigSettings.Load(Setting), "false", StringComparison.OrdinalIgnoreCase);
            set => ConfigSettings.Save(Setting, value ? "true" : "false");
        }

        public void Dispose() => _timer.Dispose();
    }
}
