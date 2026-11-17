using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Previous Versions (right-click → File Tools): older copies from snapshots, to view, compare or restore. See <see cref="Snapshots"/>.</summary>
    public partial class PreviousVersionsWindow : Window
    {
        public sealed class Row
        {
            public FileVersion Version { get; init; } = null!;
            public string When => Version.Taken == DateTime.MinValue ? "?" : Version.Taken.ToString("g");
            public string Source => Version.Source;
            public string Label => Version.Label;
            public string SizeText => Version.IsDirectory ? "folder" : ProgressFormat.Bytes(Version.Size);
            public string Changed { get; init; } = "";
            public string Tip => Version.Path;
        }

        private readonly string _path = "";
        private readonly Action<string>? _show, _view, _refresh;
        private readonly Action<string, string>? _compare;
        private readonly Func<string, Task<bool>>? _trash;

        public PreviousVersionsWindow() { InitializeComponent(); }

        public PreviousVersionsWindow(string path, Action<string> show, Action<string> view, Action<string, string> compare,
                                      Func<string, Task<bool>> trash, Action<string> refresh) : this()
        {
            _path = path;
            _show = show; _view = view; _compare = compare; _trash = trash; _refresh = refresh;
            PathText.Text = path;
            Title = "Previous Versions — " + Path.GetFileName(path.TrimEnd('/', '\\'));
            VersionList.SelectionChanged += (_, _) => UpdateButtons();
            VersionList.DoubleTapped += (_, _) => View();
            ViewButton.Click += (_, _) => View();
            CompareButton.Click += (_, _) => { if (Selected is { } v) _compare?.Invoke(v.Path, _path); };
            ShowButton.Click += (_, _) => { if (Selected is { } v) _show?.Invoke(v.Path); };
            CopyButton.Click += async (_, _) => await RestoreCopyAsync();
            RestoreButton.Click += async (_, _) => await RestoreAsync();
            CloseButton.Click += (_, _) => Close();
            Opened += async (_, _) => await LoadAsync();
        }

        private FileVersion? Selected => (VersionList.SelectedItem as Row)?.Version;

        private async Task LoadAsync()
        {
            StatusText.Text = "Looking for snapshots…";
            var versions = await Task.Run(() => Snapshots.Find(_path));
            long size = -1; DateTime modified = DateTime.MinValue;
            try { if (File.Exists(_path)) { var fi = new FileInfo(_path); size = fi.Length; modified = fi.LastWriteTime; } } catch { }
            VersionList.ItemsSource = versions.Select(v => new Row
            {
                Version = v,
                Changed = v.IsDirectory ? "" : v.Size == size && v.Modified == modified ? "same as now" : v.Modified.ToString("g"),
            }).ToList();
            StatusText.Text = versions.Count > 0
                ? $"{versions.Count} older version(s). Snapshots can't be changed; restoring copies a version back."
                : "No older versions were found. DORI looks in Btrfs snapshots made by snapper or Timeshift, ZFS snapshots, " +
                  "Time Machine backups (macOS, while the backup disk is connected) and Windows shadow copies (run DORI as administrator to see them).";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var v = Selected;
            ViewButton.IsEnabled = v is { IsDirectory: false };
            CompareButton.IsEnabled = v is { IsDirectory: false } && File.Exists(_path);
            ShowButton.IsEnabled = CopyButton.IsEnabled = v != null;
            RestoreButton.IsEnabled = v is { IsDirectory: false };
        }

        private void View()
        {
            if (Selected is { IsDirectory: false } v) _view?.Invoke(v.Path);
        }

        /// <summary>"report (2024-03-01 0930).txt" beside the current one.</summary>
        public static string CopyName(string current, DateTime taken, Func<string, bool> exists)
        {
            string dir = Path.GetDirectoryName(current.TrimEnd('/', '\\')) ?? ".";
            string name = Path.GetFileName(current.TrimEnd('/', '\\'));
            bool folder = !Path.HasExtension(name) || Directory.Exists(current);
            string stem = folder ? name : Path.GetFileNameWithoutExtension(name), ext = folder ? "" : Path.GetExtension(name);
            string candidate = Path.Combine(dir, $"{stem} ({taken:yyyy-MM-dd HHmm}){ext}");
            for (int n = 2; exists(candidate); n++) candidate = Path.Combine(dir, $"{stem} ({taken:yyyy-MM-dd HHmm}) {n}{ext}");
            return candidate;
        }

        private async Task RestoreCopyAsync()
        {
            if (Selected is not { } v) return;
            string target = CopyName(_path, v.Taken, p => File.Exists(p) || Directory.Exists(p));
            StatusText.Text = "Copying…";
            try
            {
                await Task.Run(() => CopyTree(v.Path, target));
                StatusText.Text = $"Copied to {Path.GetFileName(target)}.";
                _refresh?.Invoke(Path.GetDirectoryName(target)!);
            }
            catch (Exception ex) { StatusText.Text = "It wasn't copied: " + ex.Message; }
        }

        private async Task RestoreAsync()
        {
            if (Selected is not { IsDirectory: false } v) return;
            bool exists = File.Exists(_path);
            if (!await new MessageBox(exists
                    ? $"Put the version from {v.Taken:g} in place of the current {Path.GetFileName(_path)}? The current one goes to the trash (Ctrl/Cmd+Z brings it back)."
                    : $"Put the version from {v.Taken:g} back as {Path.GetFileName(_path)}?",
                    true, "Restore", "Cancel", "Previous Versions").ShowDialog<bool>(this)) return;
            try
            {
                if (exists && !(await _trash!(_path))) { StatusText.Text = "The current file couldn't go to the trash, so nothing was changed."; return; }
                await Task.Run(() => File.Copy(v.Path, _path, overwrite: false));
                StatusText.Text = $"Restored the version from {v.Taken:g}.";
                _refresh?.Invoke(Path.GetDirectoryName(_path)!);
            }
            catch (Exception ex) { StatusText.Text = "It wasn't restored: " + ex.Message; }
        }

        /// <summary>Copies a file, or a folder and everything in it (links are copied as nothing, never followed).</summary>
        internal static void CopyTree(string from, string to)
        {
            if (File.Exists(from)) { File.Copy(from, to, overwrite: false); return; }
            Directory.CreateDirectory(to);
            foreach (var e in new DirectoryInfo(from).EnumerateFileSystemInfos())
            {
                if (e.LinkTarget != null) continue;
                string target = Path.Combine(to, e.Name);
                if (e is DirectoryInfo) CopyTree(e.FullName, target);
                else File.Copy(e.FullName, target, overwrite: false);
            }
        }
    }
}
