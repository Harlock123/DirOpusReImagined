
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DirOpusReImagined;

public static class ExecutableDetector
{
    public static bool IsExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path)) return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return IsExecutableOnWindows(path);
        }
        else
        {
            return IsExecutableOnUnix(path);
        }
    }

    // ---------- Windows ----------
    private static bool IsExecutableOnWindows(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // PATHEXT lists extensions treated as executable when invoked without extension.
        // Include common ones for robustness.
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? "")
            .Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();

        // Fallback common executable/script extensions if PATHEXT not set
        string[] defaultExts = { ".exe", ".com", ".bat", ".cmd", ".vbs", ".js", ".ps1" };

        if (pathExt.Count == 0)
        {
            return defaultExts.Contains(ext);
        }

        return pathExt.Contains(ext);
    }

    // ---------- Linux/macOS ----------
    private static bool IsExecutableOnUnix(string path)
    {
        // Must be a regular file
        if (!IsRegularFile(path)) return false;

        // Check POSIX execute bits
        if (!HasAnyExecuteBit(path)) return false;

        // Optional: For scripts, you might also verify the shebang is present
        // but usually execute bit is sufficient for "is executable".
        return true;
    }

    private static bool IsRegularFile(string path)
    {
        // Use stat to confirm S_IFREG
        if (Stat(path, out var st) != 0) return false;
        const uint S_IFMT = 0xF000;
        const uint S_IFREG = 0x8000;
        return ((st.st_mode & S_IFMT) == S_IFREG);
    }

    private static bool HasAnyExecuteBit(string path)
    {
        if (Stat(path, out var st) != 0) return false;

        const uint S_IXUSR = 0x40; // 0100
        const uint S_IXGRP = 0x08; // 0010
        const uint S_IXOTH = 0x01; // 0001

        return (st.st_mode & (S_IXUSR | S_IXGRP | S_IXOTH)) != 0;
    }

    // P/Invoke to stat on Unix
    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int stat(string path, out stat_struct buf);

    private static int Stat(string path, out stat_struct st) => stat(path, out st);

    // 64-bit friendly stat structure (Linux/macOS commonly compatible layout for .NET P/Invoke scenarios)
    // Note: Layout can vary across platforms; for production, consider using Mono.Posix.NETStandard
    // (Mono.Posix) to avoid layout pitfalls.
    [StructLayout(LayoutKind.Sequential)]
    private struct stat_struct
    {
        public ulong st_dev;
        public ulong st_ino;
        public uint st_mode;
        public uint st_nlink;
        public uint st_uid;
        public uint st_gid;
        public ulong st_rdev;
        public long st_size;
        public long st_blksize;
        public long st_blocks;

        public long st_atime;
        public long st_atimensec;
        public long st_mtime;
        public long st_mtimensec;
        public long st_ctime;
        public long st_ctimensec;

        // Some platforms have different field orders/sizes.
        // If you hit issues, use Mono.Posix or RuntimeInformation checks with separate structs.
    }
}