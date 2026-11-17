using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DirOpusReImagined
{
    /// <summary>
    /// Lets other programs open folders in DORI.
    /// <list type="bullet">
    /// <item>Linux: a <c>dori.desktop</c> entry in ~/.local/share/applications that handles folders
    /// (so "Open With → DORI" appears), and optionally DORI as the default for folders through
    /// <c>xdg-mime</c>; the previous default is remembered and put back when this is removed.</item>
    /// <item>Windows: "Open in DORI" on the right-click menu of folders, folder backgrounds and drives,
    /// for this user only (HKEY_CURRENT_USER\Software\Classes).</item>
    /// <item>macOS: not offered; <c>open -a</c> and the Dock work as they do for any app.</item>
    /// </list>
    /// </summary>
    public static class DesktopIntegration
    {
        public const string DesktopFileName = "dori.desktop";
        private const string PreviousDefaultSetting = "PreviousFolderHandler";

        public static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        /// <summary>The program to run: this executable (the published single file, or the apphost while developing).</summary>
        public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DirOpusReImagined");

        // ---- Linux ----

        public static string ApplicationsFolder()
        {
            string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(string.IsNullOrEmpty(dataHome) ? Path.Combine(home, ".local", "share") : dataHome, "applications");
        }

        /// <summary>The text of the .desktop entry for <paramref name="exe"/>.</summary>
        public static string DesktopEntry(string exe, string? icon)
        {
            var sb = new StringBuilder();
            sb.Append("[Desktop Entry]\n");
            sb.Append("Type=Application\n");
            sb.Append("Name=DORI\n");
            sb.Append("GenericName=File Manager\n");
            sb.Append("Comment=Two-panel file manager (DirOpusReImagined)\n");
            sb.Append("Exec=").Append(QuoteExec(exe)).Append(" %U\n");
            if (!string.IsNullOrEmpty(icon)) sb.Append("Icon=").Append(icon).Append('\n');
            sb.Append("Terminal=false\n");
            sb.Append("Categories=System;FileTools;FileManager;Utility;\n");
            sb.Append("MimeType=inode/directory;\n");
            sb.Append("StartupNotify=true\n");
            return sb.ToString();
        }

        /// <summary>Quotes an Exec= program path as the desktop entry spec asks (and escapes what it lists).</summary>
        public static string QuoteExec(string path)
        {
            if (path.IndexOfAny(new[] { ' ', '\t', '"', '\'', '\\', '>', '<', '~', '|', '&', ';', '$', '*', '?', '#', '(', ')', '`' }) < 0)
                return path;
            var sb = new StringBuilder("\"");
            foreach (char c in path)
            {
                if (c is '"' or '`' or '$' or '\\') sb.Append('\\');
                sb.Append(c);
            }
            // In a .desktop file a backslash is itself escaped once more when the key is read.
            return sb.Append('"').ToString().Replace("\\", "\\\\");
        }

        public static bool IsInstalled()
        {
            if (OperatingSystem.IsLinux()) return File.Exists(Path.Combine(ApplicationsFolder(), DesktopFileName));
            if (OperatingSystem.IsWindows()) return WindowsIsInstalled();
            return false;
        }

        /// <summary>Whether DORI is the default program for folders (Linux).</summary>
        public static bool IsDefault()
            => OperatingSystem.IsLinux() && Run("xdg-mime", out var o, "query", "default", "inode/directory") && o.Trim() == DesktopFileName;

        /// <summary>Adds the entry (Linux) or the right-click items (Windows); with <paramref name="makeDefault"/>, makes DORI the default for folders (Linux).</summary>
        public static void Install(bool makeDefault)
        {
            string exe = ExecutablePath;
            if (OperatingSystem.IsWindows()) { WindowsInstall(exe); return; }
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Only Linux and Windows are supported.");

            string apps = ApplicationsFolder();
            Directory.CreateDirectory(apps);
            // DORI has no icon file of its own yet, so the desktop's standard file manager icon stands in.
            const string icon = "system-file-manager";
            File.WriteAllText(Path.Combine(apps, DesktopFileName), DesktopEntry(exe, icon));
            Run("update-desktop-database", out _, apps);

            if (makeDefault && !IsDefault())
            {
                if (Run("xdg-mime", out var previous, "query", "default", "inode/directory") && previous.Trim().Length > 0)
                    ConfigSettings.Save(PreviousDefaultSetting, previous.Trim());
                if (!Run("xdg-mime", out _, "default", DesktopFileName, "inode/directory"))
                    throw new IOException("xdg-mime couldn't make DORI the default for folders. Is xdg-utils installed?");
            }
        }

        /// <summary>Takes it all away again, putting back the previous default program for folders.</summary>
        public static void Remove()
        {
            if (OperatingSystem.IsWindows()) { WindowsRemove(); return; }
            if (!OperatingSystem.IsLinux()) return;
            if (IsDefault())
            {
                string? previous = ConfigSettings.Load(PreviousDefaultSetting);
                if (!string.IsNullOrEmpty(previous) && previous != DesktopFileName)
                    Run("xdg-mime", out _, "default", previous, "inode/directory");
            }
            string file = Path.Combine(ApplicationsFolder(), DesktopFileName);
            if (File.Exists(file)) File.Delete(file);
            Run("update-desktop-database", out _, ApplicationsFolder());
        }

        // ---- Windows ----

        private static readonly string[] WindowsKeys =
        {
            @"Software\Classes\Directory\shell\DORI",
            @"Software\Classes\Directory\Background\shell\DORI",
            @"Software\Classes\Drive\shell\DORI",
        };

        /// <summary>The command Explorer runs: the folder is %V (the folder right-clicked, or the one whose background was).</summary>
        public static string WindowsCommand(string exe) => $"\"{exe}\" \"%V\"";

        private static bool WindowsIsInstalled()
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowsKeys[0]);
            return key != null;
        }

        private static void WindowsInstall(string exe)
        {
            if (!OperatingSystem.IsWindows()) return;
            foreach (var path in WindowsKeys)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
                key.SetValue("", "Open in DORI");
                key.SetValue("Icon", exe);
                using var command = key.CreateSubKey("command");
                command.SetValue("", WindowsCommand(exe));
            }
        }

        private static void WindowsRemove()
        {
            if (!OperatingSystem.IsWindows()) return;
            foreach (var path in WindowsKeys)
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }

        private static bool Run(string exe, out string output, params string[] args)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return false;
                var read = p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(10000)) return false;
                output = read.Result;
                return p.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
