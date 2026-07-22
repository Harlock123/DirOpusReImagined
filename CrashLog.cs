using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace DirOpusReImagined;

/// <summary>
/// Catches exceptions that would otherwise kill the process silently and writes them to a log file.
/// </summary>
/// <remarks>
/// An exception escaping an <c>async void</c> event handler (which every Avalonia click handler is)
/// does not surface anywhere — the process just disappears, with no dialog and nothing in the UI to
/// say why. That makes a whole class of bug unreportable. Hooking the dispatcher lets us record a
/// stack trace and, for UI-thread faults, keep the app alive so the user can act on it.
/// </remarks>
public static class CrashLog
{
    private static bool _installed;
    private static readonly object _gate = new();

    /// <summary>Where crashes are recorded. Also shown to the user in the error dialog.</summary>
    public static string LogPath => Path.Combine(StateDir(), "crash.log");

    private static string StateDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(home, "Library", "Application Support", "dori");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dori");
        return Path.Combine(home, ".config", "dori");
    }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        // UI-thread faults: recoverable. Record, tell the user, and keep running — a failed delete
        // or dialog should not cost them the session.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            e.Handled = true;
            ShowLater(e.Exception);
        };

        // Faults on a background thread: the CLR is already tearing down, so only record.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain (fatal)", e.ExceptionObject as Exception);

        // A faulted Task nobody awaited. Harmless to the process, but usually a real bug.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Unobserved task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Appends an entry. Never throws — logging must not become the crash.</summary>
    public static void Write(string source, Exception? ex)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(StateDir());
                var sb = new StringBuilder();
                sb.AppendLine("=======================================================");
                sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{source}]");
                sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
                sb.AppendLine(ex?.ToString() ?? "(no exception object)");
                sb.AppendLine();
                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch { /* nothing useful left to do */ }
    }

    /// <summary>
    /// Posts the error dialog rather than showing it inline: we are inside the dispatcher's
    /// exception handler, and re-entering the UI from here can fault again.
    /// </summary>
    private static void ShowLater(Exception ex)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    new MessageBox(
                        $"Something went wrong:\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                        $"The app is still running. Details were written to:\n{LogPath}",
                        "Unexpected error").Show();
                }
                catch { }
            }, DispatcherPriority.Background);
        }
        catch { }
    }
}
