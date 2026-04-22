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
    public const string PinnedVersion = "v1.68.2";

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
        if (File.Exists(local)) return local;

        return FindOnPath("rclone");
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
