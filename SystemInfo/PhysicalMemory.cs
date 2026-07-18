using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DirOpusReImagined.SystemInfo
{
    /// <summary>
    /// Cross-platform lookup of the machine's total physical RAM.
    ///
    /// Replaces the ComputerInfo NuGet package, which shelled out to <c>wmic</c>
    /// on Windows. <c>wmic</c> is deprecated and absent on current Windows 11
    /// builds, so that call threw at startup. Each platform is queried through a
    /// native API instead — no external process is launched.
    /// </summary>
    public static class PhysicalMemory
    {
        /// <summary>
        /// Total installed physical memory, in bytes. Returns 0 if it cannot be
        /// determined so callers can degrade gracefully.
        /// </summary>
        public static ulong GetTotalBytes()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetWindows();
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetLinux();
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return GetMacOs();
                }
            }
            catch
            {
                // Fall through to the runtime-based fallback below.
            }

            // Fallback for unrecognised platforms or a failed native query.
            // TotalAvailableMemoryBytes equals physical RAM on an unconstrained host.
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return available > 0 ? (ulong)available : 0UL;
        }

        #region Windows

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static ulong GetWindows()
        {
            var status = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };

            return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : 0UL;
        }

        #endregion

        #region Linux

        private static ulong GetLinux()
        {
            // /proc/meminfo reports MemTotal in kibibytes, e.g. "MemTotal: 16311596 kB".
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && ulong.TryParse(parts[1], out var kib))
                {
                    return kib * 1024UL;
                }

                break;
            }

            return 0UL;
        }

        #endregion

        #region macOS

        [DllImport("libc", SetLastError = true)]
        private static extern int sysctlbyname(
            string name,
            ref ulong oldp,
            ref IntPtr oldlenp,
            IntPtr newp,
            IntPtr newlen);

        private static ulong GetMacOs()
        {
            ulong value = 0;
            var len = (IntPtr)sizeof(ulong);

            // hw.memsize is the total physical memory in bytes.
            return sysctlbyname("hw.memsize", ref value, ref len, IntPtr.Zero, IntPtr.Zero) == 0
                ? value
                : 0UL;
        }

        #endregion
    }
}
