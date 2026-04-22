using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed class RcloneDaemon : IDisposable
{
    private const int LogRingCapacity = 500;

    private readonly string _binary;
    private readonly ConcurrentQueue<string> _log = new();
    private Process? _process;

    public RcloneDaemon(string binaryPath)
    {
        _binary = binaryPath;
        User = Guid.NewGuid().ToString("N");
        Password = Guid.NewGuid().ToString("N");
    }

    public int Port { get; private set; }
    public string User { get; }
    public string Password { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public bool IsRunning => _process is { HasExited: false };

    public string[] RecentLog => _log.ToArray();

    public void Start()
    {
        if (IsRunning) return;

        Port = FindFreePort();

        var psi = new ProcessStartInfo
        {
            FileName = _binary,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("rcd");
        psi.ArgumentList.Add($"--rc-addr=127.0.0.1:{Port}");
        psi.ArgumentList.Add($"--rc-user={User}");
        psi.ArgumentList.Add($"--rc-pass={Password}");

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _process.ErrorDataReceived  += (_, e) => AppendLog(e.Data);

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start rclone daemon");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
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

    private void AppendLog(string? line)
    {
        if (line is null) return;
        _log.Enqueue(line);
        while (_log.Count > LogRingCapacity && _log.TryDequeue(out _)) { }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
