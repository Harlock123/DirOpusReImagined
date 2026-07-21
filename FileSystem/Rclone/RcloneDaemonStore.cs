using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DirOpusReImagined.FileSystem.Rclone;

/// <summary>One rclone daemon we launched, enough to reconnect to it or kill it later.</summary>
internal sealed record DaemonRecord(int Pid, int Port, string User, string Pass);

/// <summary>
/// Persists the rclone daemons this app has spawned so a later launch can (a) re-attach to a
/// still-running one when "keep warm" is enabled and (b) always clean up any that leaked from a
/// crash or force-quit. Only PIDs we recorded here are ever killed — a user's own rclone processes
/// are never touched.
/// </summary>
internal static class RcloneDaemonStore
{
    private static readonly object _gate = new();

    private static string StateDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(home, "Library", "Application Support", "dori");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dori");
        return Path.Combine(home, ".config", "dori");
    }

    private static string StatePath() => Path.Combine(StateDir(), "rclone-daemons.json");

    /// <summary>Shared log file for the daemon, so a warm (parent-detached) daemon still logs.</summary>
    public static string LogPath() => Path.Combine(StateDir(), "rclone-daemon.log");

    public static List<DaemonRecord> Load()
    {
        lock (_gate)
        {
            try
            {
                var path = StatePath();
                if (!File.Exists(path)) return new List<DaemonRecord>();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<DaemonRecord>>(json) ?? new List<DaemonRecord>();
            }
            catch { return new List<DaemonRecord>(); }
        }
    }

    public static void Save(List<DaemonRecord> records)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(StateDir());
                File.WriteAllText(StatePath(), JsonSerializer.Serialize(records));
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>Replace the stored set with a single record (or clear it when null).</summary>
    public static void SetOnly(DaemonRecord? record)
        => Save(record is null ? new List<DaemonRecord>() : new List<DaemonRecord> { record });
}
