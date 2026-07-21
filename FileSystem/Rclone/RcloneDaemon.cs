using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed class RcloneDaemon : IDisposable
{
    private const int LogTailLines = 500;

    private readonly string? _binary;
    private readonly string _logPath;
    private readonly bool _attached;
    private Process? _process;
    private int _pid;

    /// <summary>Creates a daemon we will launch ourselves.</summary>
    public RcloneDaemon(string binaryPath)
    {
        _binary = binaryPath;
        _logPath = RcloneDaemonStore.LogPath();
        User = Guid.NewGuid().ToString("N");
        Password = Guid.NewGuid().ToString("N");
    }

    private RcloneDaemon(int pid, int port, string user, string pass, string logPath)
    {
        _attached = true;
        _pid = pid;
        Port = port;
        User = user;
        Password = pass;
        _logPath = logPath;
    }

    /// <summary>Re-attach to an already-running daemon we started in a previous launch.</summary>
    internal static RcloneDaemon Attach(DaemonRecord rec)
        => new(rec.Pid, rec.Port, rec.User, rec.Pass, RcloneDaemonStore.LogPath());

    public int Port { get; private set; }
    public int Pid => _attached ? _pid : (_process?.Id ?? 0);
    public string User { get; }
    public string Password { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public bool IsRunning => _attached ? ProcessIsRclone(_pid) : _process is { HasExited: false };

    /// <summary>Tail of the shared daemon log file (works for both launched and attached daemons).</summary>
    public string[] RecentLog
    {
        get
        {
            try
            {
                if (!File.Exists(_logPath)) return Array.Empty<string>();
                // Read tolerant of the daemon writing concurrently.
                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var all = sr.ReadToEnd().Split('\n');
                return all.Length <= LogTailLines ? all : all.Skip(all.Length - LogTailLines).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }
    }

    public void Start()
    {
        if (IsRunning) return;

        Port = FindFreePort();

        // Redirect stdout/stderr to our own (drained) pipes rather than inheriting the parent's.
        // Inheriting would keep the parent's console/pipe open after we exit (and tie the two
        // processes together); our own pipes get an empty EOF instead. All real logging goes to the
        // --log-file, so the daemon never writes to these pipes and thus never dies on a broken pipe
        // when the app closes — which is what lets a "kept warm" daemon outlive the app.
        var psi = new ProcessStartInfo
        {
            FileName = _binary!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("rcd");
        psi.ArgumentList.Add($"--rc-addr=127.0.0.1:{Port}");
        psi.ArgumentList.Add($"--rc-user={User}");
        psi.ArgumentList.Add($"--rc-pass={Password}");
        psi.ArgumentList.Add($"--log-file={_logPath}");

        try { Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!); } catch { }

        _process = new Process { StartInfo = psi };
        // Discard whatever trickles to the pipes so their buffers never fill and block the daemon.
        _process.OutputDataReceived += static (_, _) => { };
        _process.ErrorDataReceived  += static (_, _) => { };
        if (!_process.Start())
            throw new InvalidOperationException("Failed to start rclone daemon");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
        if (_attached)
        {
            TryKillPid(_pid);
            _pid = 0;
            return;
        }
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch { }
        try { _process.Dispose(); } catch { }
        _process = null;
    }

    public void Dispose() => Stop();

    /// <summary>True if <paramref name="pid"/> names a live process that is an rclone binary.</summary>
    public static bool ProcessIsRclone(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.Contains("rclone", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Kill a PID only if it is still a live rclone process (never touches foreign PIDs).</summary>
    public static void TryKillPid(int pid)
    {
        if (!ProcessIsRclone(pid)) return;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(2000);
        }
        catch { }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
