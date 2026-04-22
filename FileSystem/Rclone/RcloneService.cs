using System;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

public static class RcloneService
{
    private static readonly object _gate = new();

    private static Task<RcloneClient>? _init;
    private static RcloneBinaryManager? _binaryManager;
    private static RcloneDaemon? _daemon;
    private static RcloneClient? _client;

    public static RcloneDaemon? Daemon => _daemon;
    public static string? BinaryPath { get; private set; }
    public static bool IsRunning => _daemon?.IsRunning ?? false;

    public static bool IsInstalled()
    {
        if (!string.IsNullOrEmpty(BinaryPath)) return true;
        return new RcloneBinaryManager().FindInstalled() is not null;
    }

    public static async Task InstallAsync(IProgress<double>? progress = null)
    {
        var bm = new RcloneBinaryManager();
        var path = await bm.InstallAsync(progress).ConfigureAwait(false);
        lock (_gate) { BinaryPath = path; }
    }

    public static Task<RcloneClient> GetClientAsync()
    {
        lock (_gate)
        {
            _init ??= InitializeAsync();
        }
        return _init;
    }

    public static void Shutdown()
    {
        RcloneClient? client;
        RcloneDaemon? daemon;
        lock (_gate)
        {
            client = _client;
            daemon = _daemon;
            _client = null;
            _daemon = null;
            _binaryManager = null;
            _init = null;
            BinaryPath = null;
        }
        try { client?.Dispose(); } catch { }
        try { daemon?.Dispose(); } catch { }
    }

    private static async Task<RcloneClient> InitializeAsync()
    {
        try
        {
            _binaryManager = new RcloneBinaryManager();
            var found = _binaryManager.FindInstalled();
            if (found is null)
            {
                throw new InvalidOperationException(
                    "rclone is not installed. Open the rclone diagnostics dialog to install it (%RCLONEDIAG% button).");
            }
            BinaryPath = found;

            _daemon = new RcloneDaemon(BinaryPath);
            _daemon.Start();

            var client = new RcloneClient(_daemon.BaseUrl, _daemon.User, _daemon.Password);
            var ready = await client.WaitForReadyAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (!ready)
            {
                client.Dispose();
                _daemon.Stop();
                throw new InvalidOperationException(
                    "rclone daemon did not become ready within 10 seconds. Check diagnostics for details.");
            }

            _client = client;
            return client;
        }
        catch
        {
            // Reset so the next call retries from scratch instead of returning the failed task forever.
            lock (_gate)
            {
                _init = null;
            }
            throw;
        }
    }
}
