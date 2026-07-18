using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DirOpusReImagined.SystemInfo
{
    /// <summary>
    /// Opens a native terminal / shell window in a given local directory, per platform:
    /// Windows Terminal (falling back to PowerShell, then cmd), Terminal.app on macOS,
    /// and the first available emulator on Linux. No external process is launched other
    /// than the terminal itself.
    /// </summary>
    public static class TerminalLauncher
    {
        /// <summary>
        /// Attempts to open a terminal at <paramref name="directory"/>. When
        /// <paramref name="command"/> is supplied (the user's configured terminal) it is tried
        /// first, with <c>%DIR%</c> in <paramref name="argsTemplate"/> replaced by the folder;
        /// if it can't start, the built-in per-OS detection is used as a fallback.
        /// Returns <c>null</c> on success, or a human-readable error message on failure.
        /// </summary>
        public static string? OpenAt(string directory, string? command = null, string? argsTemplate = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return "No folder is available for the active panel.";

            if (!Directory.Exists(directory))
                return $"This folder no longer exists:\n{directory}";

            // On Windows a trailing "\" corrupts a quoted path argument, so normalize once here.
            var dir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? NormalizeWindowsDir(directory)
                : directory;

            // Configured terminal wins; a failure falls through to auto-detection below.
            if (!string.IsNullOrWhiteSpace(command) && TryStartConfigured(dir, command!, argsTemplate))
                return null;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return OpenWindows(dir);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return OpenMac(dir);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return OpenLinux(dir);

                return "Opening a terminal is not supported on this platform.";
            }
            catch (Exception ex)
            {
                return $"Could not open a terminal:\n{ex.Message}";
            }
        }

        /// <summary>
        /// Launches the user-configured terminal. Args are tokenized (respecting double quotes),
        /// each token has <c>%DIR%</c> replaced with the folder, and they're passed via
        /// ArgumentList so trailing backslashes and spaces are escaped correctly.
        /// </summary>
        private static bool TryStartConfigured(string dir, string command, string? argsTemplate)
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                WorkingDirectory = dir,
                // Required for Windows Terminal's app-execution alias; harmless elsewhere.
                UseShellExecute = true
            };

            foreach (var token in TokenizeArguments(argsTemplate ?? string.Empty))
                psi.ArgumentList.Add(token.Replace("%DIR%", dir));

            return TryStart(psi);
        }

        /// <summary>Splits a command-line argument string into tokens, honoring "double quotes".</summary>
        private static IEnumerable<string> TokenizeArguments(string args)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in args)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        private static string? OpenWindows(string dir)
        {
            // A trailing "\" would escape the closing quote when Windows Terminal's -d
            // argument is built, producing an invalid directory (error 0x8007010b), so
            // normalize it and let ArgumentList apply the correct escaping.
            dir = NormalizeWindowsDir(dir);

            // Windows Terminal is an app-execution alias, so it resolves only with
            // UseShellExecute = true; PowerShell and cmd are the fallbacks.
            var wt = new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = true };
            wt.ArgumentList.Add("-d");
            wt.ArgumentList.Add(dir);
            if (TryStart(wt))
                return null;

            if (TryStart(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    WorkingDirectory = dir,
                    UseShellExecute = true
                }))
                return null;

            if (TryStart(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = dir,
                    UseShellExecute = true
                }))
                return null;

            return "No terminal (Windows Terminal, PowerShell, or cmd) could be launched.";
        }

        /// <summary>
        /// Strips a trailing separator from a Windows path, but keeps the mandatory
        /// backslash on a bare drive root (e.g. "C:\" stays "C:\", not "C:").
        /// </summary>
        private static string NormalizeWindowsDir(string dir)
        {
            dir = dir.Trim().Trim('"');

            var trimmed = dir.TrimEnd('\\', '/');

            // "C:" on its own is the current directory of that drive, not its root — restore the "\".
            if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
                return trimmed + "\\";

            return trimmed.Length == 0 ? dir : trimmed;
        }

        private static string? OpenMac(string dir)
        {
            var psi = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add("Terminal");
            psi.ArgumentList.Add(dir);

            return TryStart(psi) ? null : "Could not launch Terminal.app.";
        }

        private static string? OpenLinux(string dir)
        {
            // Tried in order. WorkingDirectory is also set so emulators that inherit the
            // parent CWD still open in the right place even without an explicit flag.
            var candidates = new (string Exe, string[] Args)[]
            {
                ("x-terminal-emulator", Array.Empty<string>()),
                ("gnome-terminal",      new[] { $"--working-directory={dir}" }),
                ("konsole",             new[] { "--workdir", dir }),
                ("xfce4-terminal",      new[] { $"--working-directory={dir}" }),
                ("mate-terminal",       new[] { $"--working-directory={dir}" }),
                ("tilix",               new[] { $"--working-directory={dir}" }),
                ("alacritty",           new[] { "--working-directory", dir }),
                ("kitty",               new[] { "--directory", dir }),
                ("terminator",          new[] { $"--working-directory={dir}" }),
                ("xterm",               Array.Empty<string>()),
            };

            foreach (var (exe, args) in candidates)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = dir,
                    UseShellExecute = false
                };
                foreach (var arg in args) psi.ArgumentList.Add(arg);

                if (TryStart(psi)) return null;
            }

            return "No supported terminal emulator was found on your PATH.";
        }

        private static bool TryStart(ProcessStartInfo psi)
        {
            try
            {
                return Process.Start(psi) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
