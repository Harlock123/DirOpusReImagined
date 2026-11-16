using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DirOpusReImagined
{
    /// <summary>
    /// Owns where Configuration.xml lives.
    ///
    /// The search order used to be spelled out in three places (MainWindow's finder, MainWindow's
    /// writable-path helper, and DisplayScaling's pre-startup lookup), and the settings dialog
    /// ignored all of them and used the bare relative name — which resolves against the process
    /// working directory. Launched from an IDE that happens to be the project folder, so it looked
    /// fine; launched from anywhere else it silently read and wrote a different file than the one
    /// the app had actually loaded, so saved settings vanished and the dialog showed stale values.
    ///
    /// Everything now resolves through here. Deliberately free of Avalonia types: DisplayScaling
    /// calls it from Main before the windowing platform exists.
    /// </summary>
    public static class ConfigFile
    {
        public const string FileName = "Configuration.xml";

        /// <summary>
        /// Where the config is looked for, in order: the working directory, then next to the
        /// executable, then the per-platform user config location. Order is load-bearing — a config
        /// sitting beside a portable copy of the app must win over the one in the user profile.
        /// </summary>
        public static string[] SearchPaths() => new[]
        {
            Path.Combine(Environment.CurrentDirectory, FileName),
            Path.Combine(AppContext.BaseDirectory, FileName),
            GetWritablePath(),
        };

        /// <summary>The first search location that actually exists, or null when there is no config yet.</summary>
        public static string? Find()
        {
            foreach (string path in SearchPaths())
            {
                try
                {
                    if (File.Exists(path)) return path;
                }
                catch
                {
                    // An unreadable or malformed path should not stop the remaining candidates.
                }
            }
            return null;
        }

        /// <summary>
        /// The per-platform location the app may always write to, whether or not a file is there yet:
        /// <list type="bullet">
        /// <item>macOS: ~/Library/Application Support/dori/</item>
        /// <item>Windows: %APPDATA%\dori\</item>
        /// <item>Linux/Unix: ~/.config/dori/</item>
        /// </list>
        /// </summary>
        public static string GetWritablePath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(home, "Library", "Application Support", "dori", FileName);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dori", FileName);

            return Path.Combine(home, ".config", "dori", FileName);
        }

        /// <summary>
        /// The path to read from and write to: the existing config if there is one, otherwise the
        /// writable location it would be created in. Use this anywhere a single path is needed for
        /// both, so a read and the write that follows it can never disagree.
        /// </summary>
        public static string Resolve() => Find() ?? GetWritablePath();

        /// <summary>Creates the parent directory for <paramref name="path"/> if it does not exist.</summary>
        public static void EnsureDirectory(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Reads and writes single top-level settings (&lt;Name&gt;value&lt;/Name&gt;) in the config file.</summary>
    public static class ConfigSettings
    {
        /// <summary>The setting's text, or null when it isn't there (or the file can't be read).</summary>
        public static string? Load(string name, string? path = null)
        {
            try
            {
                path ??= ConfigFile.Resolve();
                if (!System.IO.File.Exists(path)) return null;
                return System.Xml.Linq.XDocument.Load(path).Root?.Element(name)?.Value;
            }
            catch { return null; }
        }

        /// <summary>Sets the setting, keeping everything else in the file. Best effort: a failure is logged, not thrown.</summary>
        public static void Save(string name, string value, string? path = null)
        {
            try
            {
                path ??= ConfigFile.Resolve();
                var doc = System.IO.File.Exists(path)
                    ? System.Xml.Linq.XDocument.Load(path)
                    : new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Settings"));
                var root = doc.Root!;
                var el = root.Element(name);
                if (el == null) { el = new System.Xml.Linq.XElement(name); root.Add(el); }
                el.Value = value;
                ConfigFile.EnsureDirectory(path);
                doc.Save(path);
            }
            catch (Exception ex) { CrashLog.Write("Saving setting " + name, ex); }
        }
    }
}
