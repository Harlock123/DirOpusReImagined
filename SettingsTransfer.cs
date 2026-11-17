using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>
    /// All of DORI's settings in one file, and where they live.
    /// <list type="bullet">
    /// <item><b>Export</b> writes a .zip of Configuration.xml (buttons and banks, shortcuts,
    /// layouts, filters, columns, theme…), Labels.xml, Rules.xml, SyncJobs.xml, the bookmarks and,
    /// if asked, the disk catalogues.</item>
    /// <item><b>Import</b> puts such a zip's files in place of the current ones, keeping a backup.</item>
    /// <item><b>Portable</b>: settings beside the program (found first, so a copy of DORI on a USB
    /// stick carries its own), or in the user's folder (~/.config/dori, %APPDATA%\dori,
    /// ~/Library/Application Support/dori).</item>
    /// </list>
    /// DORI writes its settings as it closes, so an import or move is done after it has closed
    /// (<see cref="RunPending"/>), and DORI then starts again.
    /// </summary>
    public static class SettingsTransfer
    {
        public static readonly string[] Files = { ConfigFile.FileName, LabelStore.FileName, "Rules.xml", "SyncJobs.xml" };
        public const string CataloguesFolder = "Catalogues";
        public const string BookmarksFile = "BOOKMARKS.MD";

        /// <summary>The folder the settings are in now.</summary>
        public static string CurrentFolder => Path.GetDirectoryName(ConfigFile.Resolve()) ?? ".";
        public static string ProgramFolder => AppContext.BaseDirectory.TrimEnd('/', '\\');
        public static string UserFolder => Path.GetDirectoryName(ConfigFile.GetWritablePath()) ?? ".";

        public static bool IsPortable => Same(CurrentFolder, ProgramFolder);

        private static bool Same(string a, string b)
            => string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
                             OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        /// <summary>Writes the settings in <paramref name="settingsFolder"/> (and the bookmarks in <paramref name="programFolder"/>) to a zip. Returns how many files went in.</summary>
        public static int Export(string zipPath, string settingsFolder, string programFolder, bool catalogues)
        {
            string temp = zipPath + ".dori-part";
            int count = 0;
            try
            {
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    foreach (var name in Files)
                    {
                        string p = Path.Combine(settingsFolder, name);
                        if (File.Exists(p)) { zip.CreateEntryFromFile(p, "settings/" + name); count++; }
                    }
                    string bookmarks = Path.Combine(programFolder, BookmarksFile);
                    if (File.Exists(bookmarks)) { zip.CreateEntryFromFile(bookmarks, "program/" + BookmarksFile); count++; }
                    string cats = Path.Combine(settingsFolder, CataloguesFolder);
                    if (catalogues && Directory.Exists(cats))
                        foreach (var f in Directory.GetFiles(cats))
                        { zip.CreateEntryFromFile(f, $"settings/{CataloguesFolder}/{Path.GetFileName(f)}"); count++; }
                }
                File.Move(temp, zipPath, overwrite: true);
            }
            finally { try { File.Delete(temp); } catch { } }
            return count;
        }

        /// <summary>What a settings zip holds, or why it isn't one.</summary>
        public static (List<string> Entries, string? Problem) Inspect(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var names = zip.Entries.Select(e => e.FullName).Where(n => !n.EndsWith('/')).ToList();
                if (!names.Contains("settings/" + ConfigFile.FileName))
                    return (names, "It isn't a DORI settings file (there's no Configuration.xml in it).");
                if (names.Any(n => n.Contains("..") || Path.IsPathRooted(n)))
                    return (names, "It holds file names that point outside its folders, so it isn't used.");
                return (names, null);
            }
            catch (Exception ex) { return (new List<string>(), "It couldn't be read: " + ex.Message); }
        }

        /// <summary>
        /// Puts a settings zip's files in place, after saving the files they replace to
        /// settings-backup-DATE.zip in <paramref name="settingsFolder"/>. Returns the backup's path.
        /// </summary>
        public static string Import(string zipPath, string settingsFolder, string programFolder)
        {
            var (entries, problem) = Inspect(zipPath);
            if (problem != null) throw new InvalidDataException(problem);
            Directory.CreateDirectory(settingsFolder);
            string backup = Path.Combine(settingsFolder, $"settings-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            Export(backup, settingsFolder, programFolder, catalogues: true);

            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                string? target = e.FullName.StartsWith("settings/") ? Path.Combine(settingsFolder, e.FullName["settings/".Length..])
                               : e.FullName == "program/" + BookmarksFile ? Path.Combine(programFolder, BookmarksFile)
                               : null;
                if (target == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temp = target + ".dori-part";
                e.ExtractToFile(temp, overwrite: true);
                File.Move(temp, target, overwrite: true);
            }
            return backup;
        }

        /// <summary>
        /// Moves the settings from one folder to another (portable ↔ user folder). Files already at
        /// the target are first saved in a backup zip there. The old Configuration.xml is renamed
        /// (Configuration.xml.moved), so it isn't found ahead of the new one.
        /// </summary>
        public static void Move(string from, string to)
        {
            if (Same(from, to)) return;
            Directory.CreateDirectory(to);
            if (Files.Any(f => File.Exists(Path.Combine(to, f))))
                Export(Path.Combine(to, $"settings-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip"), to, ProgramFolder, catalogues: false);
            foreach (var name in Files)
            {
                string src = Path.Combine(from, name);
                if (File.Exists(src)) File.Copy(src, Path.Combine(to, name), overwrite: true);
            }
            string cats = Path.Combine(from, CataloguesFolder);
            if (Directory.Exists(cats))
            {
                Directory.CreateDirectory(Path.Combine(to, CataloguesFolder));
                foreach (var f in Directory.GetFiles(cats)) File.Copy(f, Path.Combine(to, CataloguesFolder, Path.GetFileName(f)), overwrite: true);
            }
            string oldConfig = Path.Combine(from, ConfigFile.FileName);
            if (File.Exists(oldConfig)) File.Move(oldConfig, oldConfig + ".moved", overwrite: true);
        }

        /// <summary>Whether DORI can write beside its program (it can't from a read-only place like Program Files or a mounted disk image).</summary>
        public static bool CanWrite(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".dori-write-test-" + Guid.NewGuid().ToString("N")[..8]);
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // ---- Done after DORI has closed and saved ----

        private static Action? _pending;

        /// <summary>Remembers an import or move to do once DORI has closed.</summary>
        public static void AfterExit(Action action) => _pending = action;

        public static bool HasPending => _pending != null;

        /// <summary>Runs the remembered import or move, then starts DORI again. Called as the app exits.</summary>
        public static void RunPending()
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;
            try { action(); }
            catch (Exception ex) { CrashLog.Write("Settings move or import", ex); }
            try
            {
                if (Environment.ProcessPath is { } exe)
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = ProgramFolder });
            }
            catch { /* started by hand instead */ }
        }
    }
}
