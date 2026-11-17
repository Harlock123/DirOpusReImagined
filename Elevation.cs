using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DirOpusReImagined
{
    /// <summary>The single actions that can be run with administrator rights.</summary>
    public enum ElevatedAction { Delete, Copy, Move, Rename, NewFolder, WriteFile }

    /// <summary>
    /// Runs one file action with administrator rights, after the system asks for your password:
    /// <c>pkexec</c> on Linux, the administrator prompt (<c>osascript … with administrator privileges</c>)
    /// on macOS, and the UAC prompt on Windows. Only that action runs elevated, never DORI itself.
    /// Paths are passed as separate arguments (Linux) or quoted by the shell's own rules, never pasted
    /// into a command line unescaped.
    /// </summary>
    public static class Elevation
    {
        public static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || (OperatingSystem.IsLinux() && Pkexec != null);

        private static readonly Lazy<string?> PkexecPath = new(() =>
            new[] { "/usr/bin/pkexec", "/bin/pkexec", "/usr/local/bin/pkexec" }.FirstOrDefault(File.Exists));
        public static string? Pkexec => PkexecPath.Value;

        /// <summary>
        /// The POSIX shell script for an action; its paths come in as "$1", "$2"… so no path is ever
        /// read as part of the command. For a copy or move the last argument is the target folder.
        /// </summary>
        public static string ShellScript(ElevatedAction action) => action switch
        {
            ElevatedAction.Delete => "rm -rf -- \"$@\"",
            // -n: never replace something already there; -R copies folders; -p keeps times and modes.
            ElevatedAction.Copy => "t=\"${@: -1}\"; n=$(($#-1)); cp -Rpn -- \"${@:1:$n}\" \"$t\"/",
            ElevatedAction.Move => "t=\"${@: -1}\"; n=$(($#-1)); mv -n -- \"${@:1:$n}\" \"$t\"/",
            ElevatedAction.Rename => "[ -e \"$2\" ] && { echo \"$2 already exists\" >&2; exit 1; }; mv -n -- \"$1\" \"$2\"",
            ElevatedAction.NewFolder => "mkdir -p -- \"$1\"",
            // The new contents are in a temporary file this user wrote; cat keeps the file's owner and permissions.
            ElevatedAction.WriteFile => "cat -- \"$1\" > \"$2\"",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        /// <summary>The command line pkexec runs on Linux: bash (for the ${@: -1} forms), the script, then the paths as arguments.</summary>
        public static List<string> LinuxArgs(ElevatedAction action, IReadOnlyList<string> paths)
        {
            var args = new List<string> { "/bin/bash", "-c", ShellScript(action), "dori-admin" };
            args.AddRange(paths);
            return args;
        }

        /// <summary>A path in single quotes for sh: ' becomes '\''.</summary>
        public static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

        /// <summary>The AppleScript that runs the action as administrator on macOS.</summary>
        public static string MacScript(ElevatedAction action, IReadOnlyList<string> paths)
        {
            string command = "/bin/bash -c " + ShellQuote(ShellScript(action)) + " dori-admin " + string.Join(" ", paths.Select(ShellQuote));
            string literal = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"do shell script \"{literal}\" with administrator privileges";
        }

        /// <summary>A path as a PowerShell single-quoted string: ' becomes ''.</summary>
        public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

        /// <summary>The PowerShell commands for an action on Windows; any failure stops it with exit code 1.</summary>
        public static string PowerShellScript(ElevatedAction action, IReadOnlyList<string> paths)
        {
            var sb = new StringBuilder("$ErrorActionPreference = 'Stop'; try { ");
            switch (action)
            {
                case ElevatedAction.Delete:
                    foreach (var p in paths) sb.Append($"Remove-Item -LiteralPath {PsQuote(p)} -Recurse -Force; ");
                    break;
                case ElevatedAction.Copy:
                case ElevatedAction.Move:
                {
                    string target = paths[^1];
                    foreach (var p in paths.Take(paths.Count - 1))
                    {
                        string dest = Path.Combine(target, Path.GetFileName(p.TrimEnd('\\', '/')));
                        sb.Append($"if (Test-Path -LiteralPath {PsQuote(dest)}) {{ throw '{Path.GetFileName(dest).Replace("'", "''")} is already there' }}; ");
                        sb.Append(action == ElevatedAction.Copy
                            ? $"Copy-Item -LiteralPath {PsQuote(p)} -Destination {PsQuote(target)} -Recurse; "
                            : $"Move-Item -LiteralPath {PsQuote(p)} -Destination {PsQuote(target)}; ");
                    }
                    break;
                }
                case ElevatedAction.Rename:
                    sb.Append($"if (Test-Path -LiteralPath {PsQuote(paths[1])}) {{ throw 'Something with that name is already there' }}; ");
                    sb.Append($"Rename-Item -LiteralPath {PsQuote(paths[0])} -NewName {PsQuote(Path.GetFileName(paths[1]))}; ");
                    break;
                case ElevatedAction.NewFolder:
                    sb.Append($"New-Item -ItemType Directory -Path {PsQuote(paths[0])} | Out-Null; ");
                    break;
                case ElevatedAction.WriteFile:
                    sb.Append($"Copy-Item -LiteralPath {PsQuote(paths[0])} -Destination {PsQuote(paths[1])} -Force; ");
                    break;
            }
            sb.Append("} catch { exit 1 }");
            return sb.ToString();
        }

        /// <summary>
        /// Runs the action. Returns null when it worked, else what went wrong — including the
        /// password prompt being cancelled.
        /// </summary>
        public static Task<string?> RunAsync(ElevatedAction action, IReadOnlyList<string> paths)
            => Task.Run(() => Run(action, paths));

        private static string? Run(ElevatedAction action, IReadOnlyList<string> paths)
        {
            try
            {
                ProcessStartInfo psi;
                if (OperatingSystem.IsWindows())
                {
                    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellScript(action, paths)));
                    psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
                    {
                        UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                    };
                }
                else if (OperatingSystem.IsMacOS())
                {
                    psi = new ProcessStartInfo("osascript") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(MacScript(action, paths));
                }
                else
                {
                    if (Pkexec == null) return "pkexec isn't installed, so DORI can't ask for administrator rights.";
                    psi = new ProcessStartInfo(Pkexec) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                    foreach (var a in LinuxArgs(action, paths)) psi.ArgumentList.Add(a);
                }

                using var p = Process.Start(psi);
                if (p == null) return "it didn't start.";
                string err = "";
                if (!psi.UseShellExecute)
                {
                    var e = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.ReadToEndAsync();
                    p.WaitForExit();
                    err = e.Result.Trim();
                }
                else p.WaitForExit();

                if (p.ExitCode == 0) return null;
                // pkexec: 126 = the prompt was dismissed, 127 = not authorised. osascript: "User canceled" (-128).
                if (p.ExitCode is 126 or 127 || err.Contains("-128") || err.Contains("canceled", StringComparison.OrdinalIgnoreCase))
                    return "the password wasn't given.";
                return err.Length > 0 ? err : $"it failed (code {p.ExitCode}).";
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "the administrator prompt was cancelled.";   // UAC: ERROR_CANCELLED
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
