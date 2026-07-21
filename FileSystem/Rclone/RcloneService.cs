using System;
using System.Linq;
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

    /// <summary>
    /// When true, the rclone daemon is left running when the app closes and re-attached on the next
    /// launch, so the (~15-20s) cloud cold-start is paid once rather than every launch. Set from the
    /// persisted user setting at startup. Off by default. See <see cref="CleanupOrphans"/>.
    /// </summary>
    public static bool KeepWarm { get; set; }

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

    /// <summary>
    /// Kill any rclone daemons we recorded that leaked from a crash/force-quit. Always run at
    /// startup, regardless of the keep-warm setting. When keep-warm is on, the most recent recorded
    /// daemon is spared so <see cref="InitializeAsync"/> can re-attach to it. Only PIDs this app
    /// recorded are ever killed — never a user's own rclone process.
    /// </summary>
    public static void CleanupOrphans()
    {
        var records = RcloneDaemonStore.Load();
        if (records.Count == 0) return;

        DaemonRecord? spare = KeepWarm ? records[^1] : null;
        foreach (var r in records)
        {
            if (spare != null && r.Pid == spare.Pid) continue;
            RcloneDaemon.TryKillPid(r.Pid);
        }
        RcloneDaemonStore.SetOnly(spare);
    }

    public static void Shutdown()
    {
        RcloneClient? client;
        RcloneDaemon? daemon;
        bool keepWarm;
        lock (_gate)
        {
            client = _client;
            daemon = _daemon;
            keepWarm = KeepWarm;
            _client = null;
            _daemon = null;
            _binaryManager = null;
            _init = null;
            BinaryPath = null;
        }

        try { client?.Dispose(); } catch { }

        // Keep-warm: leave the daemon running (its record stays in the store for the next launch).
        if (keepWarm && daemon is { IsRunning: true })
            return;

        try { daemon?.Stop(); } catch { }
        RcloneDaemonStore.SetOnly(null);
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

            // Re-attach to a still-running daemon from a previous launch when keep-warm is on.
            if (KeepWarm)
            {
                var rec = RcloneDaemonStore.Load().LastOrDefault();
                if (rec is not null && RcloneDaemon.ProcessIsRclone(rec.Pid))
                {
                    var attached = RcloneDaemon.Attach(rec);
                    var probe = new RcloneClient(attached.BaseUrl, attached.User, attached.Password);
                    if (await probe.WaitForReadyAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false))
                    {
                        _daemon = attached;
                        _client = probe;
                        return probe;
                    }
                    probe.Dispose();
                    RcloneDaemon.TryKillPid(rec.Pid); // recorded but unresponsive — clear it out
                }
            }

            var daemon = new RcloneDaemon(BinaryPath);
            daemon.Start();

            var client = new RcloneClient(daemon.BaseUrl, daemon.User, daemon.Password);
            var ready = await client.WaitForReadyAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            if (!ready)
            {
                client.Dispose();
                daemon.Stop();
                throw new InvalidOperationException(
                    "rclone daemon did not become ready within 20 seconds. Check diagnostics for details.");
            }

            _daemon = daemon;
            _client = client;
            RcloneDaemonStore.SetOnly(new DaemonRecord(daemon.Pid, daemon.Port, daemon.User, daemon.Password));
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
