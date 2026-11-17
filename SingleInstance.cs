using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined
{
    /// <summary>
    /// Folders opened from elsewhere (a file manager's "Open with", Explorer's right-click entry,
    /// or <c>dori /some/folder</c> in a terminal) go to the DORI that is already running, which
    /// opens each in a new tab, instead of starting a second copy. The running copy listens on a
    /// named pipe that only this user can open (a socket in the temp folder on Linux and macOS).
    /// Starting DORI with no folder, or with <c>--new-window</c>, always starts a new copy.
    /// </summary>
    public static class SingleInstance
    {
        public const string NewWindowSwitch = "--new-window";

        /// <summary>Switches DORI reads that take a value, so the value isn't mistaken for a folder.</summary>
        private static readonly HashSet<string> SwitchesWithValue = new(StringComparer.OrdinalIgnoreCase) { "--scale", "/scale" };

        /// <summary>Folders and files named on the command line, made absolute ("file://" URIs are accepted too).</summary>
        public static List<string> PathsFrom(string[] args, string? currentDir = null)
        {
            var paths = new List<string>();
            currentDir ??= Environment.CurrentDirectory;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (SwitchesWithValue.Contains(a)) { i++; continue; }
                if (a.StartsWith("--") || a.StartsWith("/scale=", StringComparison.OrdinalIgnoreCase)) continue;
                if (a.Length == 0 || (a.StartsWith('-') && a.Length > 1 && !File.Exists(a) && !Directory.Exists(a))) continue;
                string p = a;
                if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(p, UriKind.Absolute, out var uri))
                    p = uri.LocalPath;
                try { p = Path.GetFullPath(p, currentDir); } catch { continue; }
                paths.Add(p);
            }
            return paths;
        }

        /// <summary>The pipe's name: the same for every DORI this user runs, different for other users.</summary>
        public static string PipeName()
        {
            string who = Environment.UserName + "|" + Environment.UserDomainName;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(who));
            return "dori-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        /// <summary>
        /// Hands the paths to a running DORI. True when one took them (this copy should then
        /// exit); false when none is running, so this copy should open them itself.
        /// </summary>
        public static bool TrySend(IReadOnlyList<string> paths, int timeoutMs = 400)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                client.Connect(timeoutMs);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                foreach (var p in paths) writer.WriteLine(p);
                writer.WriteLine();             // a blank line ends the list
                using var reader = new StreamReader(client, Encoding.UTF8, false, 256, leaveOpen: true);
                var reply = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                return reply == "ok";
            }
            catch
            {
                return false;
            }
        }

        private static CancellationTokenSource? _serverCts;

        /// <summary>Starts listening for paths from later copies. <paramref name="onPaths"/> runs on a worker thread.</summary>
        public static void StartServer(Action<IReadOnlyList<string>> onPaths)
        {
            if (_serverCts != null) return;
            // On Linux and macOS a new listener would quietly take the name from one already running,
            // so a second copy (started with --new-window, or with no folder) leaves it alone.
            if (AnotherIsListening()) return;
            _serverCts = new CancellationTokenSource();
            var ct = _serverCts.Token;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    NamedPipeServerStream server;
                    try
                    {
                        server = new NamedPipeServerStream(PipeName(), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                                           PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    }
                    catch (IOException)
                    {
                        return;   // another DORI already listens; this one simply doesn't
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write("Single instance", ex);
                        return;
                    }

                    try
                    {
                        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                        using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                        var paths = new List<string>();
                        while (paths.Count < 100)
                        {
                            string? line = await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                            if (string.IsNullOrEmpty(line)) break;
                            if (line.Length < 4096) paths.Add(line);
                        }
                        using (var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                            await writer.WriteLineAsync("ok").ConfigureAwait(false);
                        if (paths.Count > 0) onPaths(paths);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                    catch (Exception) { /* a client that went away; wait for the next */ }
                    finally
                    {
                        try { server.Dispose(); } catch { }
                    }
                }
            }, ct);
        }

        /// <summary>Whether a DORI already listens (it is sent nothing, and does nothing).</summary>
        public static bool AnotherIsListening()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                client.Connect(150);
                return true;
            }
            catch { return false; }
        }

        public static void StopServer()
        {
            _serverCts?.Cancel();
            _serverCts = null;
        }

        /// <summary>Paths given when this copy started (or none), for the main window to open once it is up.</summary>
        public static List<string> StartupPaths { get; set; } = new();
    }
}
