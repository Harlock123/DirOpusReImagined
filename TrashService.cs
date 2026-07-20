using System;
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
    public static void Trash(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) TrashWindows(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) TrashMac(path);
        else TrashLinux(path);
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

    private static void TrashWindows(string path)
    {
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
    }

    // ---------------- macOS: Finder "delete" moves to Trash (with Put Back) ----------------

    private static void TrashMac(string path)
    {
        string esc = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string script = $"tell application \"Finder\" to delete (POSIX file \"{esc}\" as alias)";
        if (!TryRun("osascript", "-e", script))
            throw new IOException($"Could not move to Trash: {path}");
    }

    // ---------------- Linux: gio / trash-cli, else the freedesktop.org spec ----------------

    private static void TrashLinux(string path)
    {
        if (TryRun("gio", "trash", "--", path)) return;
        if (TryRun("trash-put", path)) return;
        FreedesktopTrash(path);
    }

    private static void FreedesktopTrash(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome)) dataHome = Path.Combine(home, ".local", "share");

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
    }

    private static bool TryRun(string exe, params string[] args)
    {
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
            proc.WaitForExit(15000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
