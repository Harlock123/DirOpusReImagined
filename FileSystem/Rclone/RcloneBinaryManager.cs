using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed class RcloneBinaryManager
{
    // Keep current: older rclone builds SIGSEGV during cgo on recent macOS, so a stale
    // pin strands users on a binary that crashes on every call.
    public const string PinnedVersion = "v1.74.1";

    private readonly string _appDataDir;

    public RcloneBinaryManager()
    {
        _appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DirOpusReImagined",
            "rclone");
    }

    public string AppDataDir => _appDataDir;

    public string? FindInstalled()
    {
        var local = Path.Combine(_appDataDir, BinaryName);

        // Prefer the bundled binary, but only if it actually runs — an older pinned rclone
        // can SIGSEGV (cgo crash) on newer macOS, in which case we fall back to a working
        // rclone on PATH instead of handing back a binary that crashes on every call.
        if (File.Exists(local) && RunsOk(local)) return local;

        var onPath = FindOnPath("rclone");
        if (onPath is not null && RunsOk(onPath)) return onPath;

        // Nothing verified clean: surface whatever exists so diagnostics can report a real
        // error rather than a misleading "not installed".
        return File.Exists(local) ? local : onPath;
    }

    /// <summary>
    /// Smoke-tests a binary with `rclone version`. False if it can't start, times out, or
    /// exits non-zero (a SIGSEGV crash exits 2), so a broken binary is never selected.
    /// </summary>
    private static bool RunsOk(string binary)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("version");

            using var p = Process.Start(psi);
            if (p is null) return false;
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> EnsureInstalledAsync(IProgress<double>? progress = null)
    {
        var existing = FindInstalled();
        if (existing is not null) return existing;

        await DownloadAsync(progress).ConfigureAwait(false);
        return Path.Combine(_appDataDir, BinaryName);
    }

    public async Task<string> InstallAsync(IProgress<double>? progress = null)
    {
        await DownloadAsync(progress).ConfigureAwait(false);
        return Path.Combine(_appDataDir, BinaryName);
    }

    private async Task DownloadAsync(IProgress<double>? progress)
    {
        var (os, arch) = CurrentPlatform();
        var zipName = $"rclone-{PinnedVersion}-{os}-{arch}.zip";
        var url = $"https://downloads.rclone.org/{PinnedVersion}/{zipName}";

        Directory.CreateDirectory(_appDataDir);
        var tmpZip = Path.Combine(_appDataDir, zipName);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (var fs = File.Create(tmpZip))
        {
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await stream.ReadAsync(buf).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        ExtractBinary(tmpZip);

        try { File.Delete(tmpZip); } catch { }
    }

    private void ExtractBinary(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.Name == BinaryName);
        if (entry is null)
            throw new InvalidDataException($"Could not find '{BinaryName}' inside rclone zip");

        var target = Path.Combine(_appDataDir, BinaryName);
        entry.ExtractToFile(target, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static string BinaryName => OperatingSystem.IsWindows() ? "rclone.exe" : "rclone";

    private static (string os, string arch) CurrentPlatform()
    {
        string os =
            OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsMacOS()   ? "osx"     :
            OperatingSystem.IsLinux()   ? "linux"   :
            throw new PlatformNotSupportedException("Unsupported OS for rclone");

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64   => "amd64",
            Architecture.X86   => "386",
            Architecture.Arm64 => "arm64",
            Architecture.Arm   => "arm",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
        };

        return (os, arch);
    }

    private static string? FindOnPath(string name)
    {
        var finder = OperatingSystem.IsWindows() ? "where" : "/usr/bin/which";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = finder,
                Arguments = name,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            if (p.ExitCode != 0) return null;
            var first = output.Split('\n')[0].Trim();
            return string.IsNullOrEmpty(first) ? null : first;
        }
        catch
        {
            return null;
        }
    }
}
