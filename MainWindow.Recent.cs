using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>
    /// The recent-folders list: every folder either panel shows is remembered (newest first), and
    /// the ◷ button beside Back/Forward (or Alt+Down in a panel) offers them in a menu. The list is
    /// shared by both panels and kept in Configuration.xml between sessions.
    /// </summary>
    public partial class MainWindow
    {
        private readonly RecentFolders _recent = new();

        private void InitRecentFolders()
        {
            LpRecentButton.Click += (_, _) => ShowRecentFolders(isLeft: true);
            RpRecentButton.Click += (_, _) => ShowRecentFolders(isLeft: false);
            Closing += (_, _) => SaveRecentFoldersToConfig();
        }

        /// <summary>Called whenever a panel finishes listing a folder.</summary>
        private void RememberRecentFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            // A local path that doesn't exist is a typo or a vanished folder, not somewhere visited.
            if (ProviderRegistry.For(path) is LocalFileProvider && !Directory.Exists(path)) return;
            _recent.Visit(path);
        }

        /// <summary>Shows the recent-folders menu under that panel's ◷ button.</summary>
        private void ShowRecentFolders(bool isLeft)
        {
            string side = isLeft ? "LP" : "RP";
            string current = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            var anchor = isLeft ? LpRecentButton : RpRecentButton;

            var items = new List<Control>();
            foreach (var path in _recent.Except(current).ToList())
            {
                // Drop local folders that have gone since they were visited.
                if (ProviderRegistry.For(path) is LocalFileProvider && !Directory.Exists(path))
                {
                    _recent.Remove(path);
                    continue;
                }
                var mi = new MenuItem { Header = MenuLabel(path) };
                ToolTip.SetTip(mi, path);
                string target = path;
                mi.Click += (_, _) => NavigatePanelTo(side, target);
                items.Add(mi);
            }

            if (items.Count == 0)
            {
                items.Add(new MenuItem { Header = "No other recent folders yet", IsEnabled = false });
            }
            else
            {
                items.Add(new Separator());
                var clear = new MenuItem { Header = "Clear recent folders" };
                clear.Click += (_, _) => _recent.Clear();
                items.Add(clear);
            }

            // Open from the button's left edge, so the menu grows into the panel rather than off
            // the side of the window.
            new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft }.ShowAt(anchor);
        }

        /// <summary>"photos   ~/Pictures/2024" — the folder's name, then where it is.</summary>
        internal static string MenuLabel(string path)
        {
            string name, parent;
            if (ArchivePath.IsArchiveUri(path) || path.Contains("://"))
            {
                string t = path.TrimEnd('/');
                int cut = t.LastIndexOfAny(new[] { '/', '!' });
                name = cut >= 0 && cut < t.Length - 1 ? t[(cut + 1)..] : t;
                return $"{name}   {path}";
            }

            string trimmed = path.TrimEnd('/', '\\');
            if (trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':')) return path;   // a root
            name = Path.GetFileName(trimmed);
            parent = Path.GetDirectoryName(trimmed) ?? "";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && parent.StartsWith(home, StringComparison.Ordinal) &&
                (parent.Length == home.Length || parent[home.Length] is '/' or '\\'))
                parent = "~" + parent[home.Length..];

            return string.IsNullOrEmpty(parent) ? name : $"{name}   {Shorten(parent)}";
        }

        /// <summary>Keeps a long folder path readable in a menu: its start and its end, with "…" between.</summary>
        internal static string Shorten(string path, int max = 44)
        {
            if (path.Length <= max) return path;
            int head = max / 3, tail = max - head - 1;
            return path[..head] + "…" + path[^tail..];
        }

        private void LoadRecentFoldersFromConfig()
        {
            try
            {
                if (_configFilePath == null || !File.Exists(_configFilePath)) return;
                _recent.AddOlder(RecentFolders.FromXml(XDocument.Load(_configFilePath).Root));
            }
            catch { /* a damaged entry just means no saved list */ }
        }

        private void SaveRecentFoldersToConfig()
        {
            try
            {
                string path = _configFilePath ?? GetWritableConfigPath();
                XDocument doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Settings"));
                var root = doc.Root ?? new XElement("Settings");
                if (doc.Root == null) doc.Add(root);

                root.Element(RecentFolders.ElementName)?.Remove();
                root.Add(_recent.ToXml());

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                doc.Save(path);
                _configFilePath = path;
            }
            catch { /* best-effort, like the other settings saved on close */ }
        }
    }
}
