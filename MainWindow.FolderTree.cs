using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// The folder tree beside each panel (Ctrl/Cmd+Shift+T, or Tree in the panel's tab bar). It starts
    /// at the drives (or / and the home folder), opens a folder's subfolders only when it is expanded,
    /// and follows the panel, opening the way down to the folder shown. Picking a folder in it takes
    /// the panel there. Linked folders are shown but never opened by following. Local folders only.
    /// </summary>
    public partial class MainWindow
    {
        private (double Width, double Height) _lastPanelSize;
        private bool _treeSyncing;

        /// <summary>The tree takes a third of the panel, between 120 and 260 pixels, plus its 4-pixel gap.</summary>
        private double TreeWidth(TreeView tree)
        {
            if (!tree.IsVisible) return 0;
            double w = Math.Clamp(_lastPanelSize.Width / 3, 120, 260);
            tree.Width = w;
            return w + 4;
        }

        private void InitFolderTree()
        {
            LPtreeToggle.Click += (_, _) => ToggleFolderTree(true);
            RPtreeToggle.Click += (_, _) => ToggleFolderTree(false);
            foreach (var (tree, isLeft) in new[] { (LPtree, true), (RPtree, false) })
            {
                var t = tree;
                bool left = isLeft;
                t.SelectionChanged += (_, _) =>
                {
                    if (_treeSyncing || t.SelectedItem is not TreeViewItem { Tag: string path }) return;
                    string shown = (left ? LPpath.Text : RPpath.Text) ?? "";
                    if (!PathsEqual(path, shown)) NavigatePanelTo(left ? "LP" : "RP", path);
                };
                if (string.Equals(ConfigSettings.Load(left ? "LeftFolderTree" : "RightFolderTree"), "true", StringComparison.OrdinalIgnoreCase))
                    Dispatcher.UIThread.Post(() => SetFolderTree(left, true), DispatcherPriority.Background);
            }
        }

        private void ToggleFolderTree(bool isLeft) => SetFolderTree(isLeft, !(isLeft ? LPtree : RPtree).IsVisible);

        private void SetFolderTree(bool isLeft, bool show)
        {
            var tree = isLeft ? LPtree : RPtree;
            tree.IsVisible = show;
            if (show && tree.ItemCount == 0) FillTreeRoots(tree);
            if (_lastPanelSize.Width > 0)
            {
                var grid = isLeft ? LPgrid : RPgrid;
                grid.SetGridSize((int)(_lastPanelSize.Width - 8 - TreeWidth(tree)), (int)_lastPanelSize.Height - 58);
            }
            ConfigSettings.Save(isLeft ? "LeftFolderTree" : "RightFolderTree", show ? "true" : "false", _configFilePath ?? ConfigFile.Resolve());
            if (show) FollowInTree(isLeft);
        }

        private void FillTreeRoots(TreeView tree)
        {
            var roots = new List<(string Name, string Path)>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) roots.Add(("🏠 " + Path.GetFileName(home.TrimEnd('/', '\\')), home));
            if (OperatingSystem.IsWindows())
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.IsReady) roots.Add((d.Name.TrimEnd('\\') + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : $"  {d.VolumeLabel}"), d.RootDirectory.FullName)); }
                    catch { }
                }
            }
            else roots.Add(("/", "/"));
            foreach (var (name, path) in roots) tree.Items.Add(MakeNode(name, path));
        }

        private TreeViewItem MakeNode(string name, string path)
        {
            var node = new TreeViewItem { Header = name, Tag = path };
            node.Items.Add(new TreeViewItem { Header = "…" });   // stands in until it is opened
            node.PropertyChanged += (_, e) =>
            {
                if (e.Property == TreeViewItem.IsExpandedProperty && node.IsExpanded) LoadChildren(node);
            };
            return node;
        }

        /// <summary>Lists a node's subfolders the first time it opens (hidden ones only with Show Hidden).</summary>
        private void LoadChildren(TreeViewItem node)
        {
            if (node.Tag is not string path) return;
            if (node.Items.Count == 1 && node.Items[0] is TreeViewItem { Tag: null }) node.Items.Clear();
            else if (node.Items.Count > 0) return;   // loaded before
            bool showHidden = ChkShowHidden?.IsChecked ?? false;
            try
            {
                var dirs = new DirectoryInfo(path).EnumerateDirectories()
                    .Where(d => showHidden || (!d.Name.StartsWith('.') && !d.Attributes.HasFlag(FileAttributes.Hidden)))
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.System) || !OperatingSystem.IsWindows())
                    .OrderBy(d => d.Name, FileUtility.NameComparer);
                foreach (var d in dirs)
                {
                    var child = MakeNode(d.Name, d.FullName);
                    if (LocalFileProvider.IsLink(d.FullName)) child.Header = d.Name + "  ↪";
                    node.Items.Add(child);
                }
            }
            catch { /* unreadable: no children */ }
        }

        /// <summary>Opens the way down to the panel's folder and selects it, without moving the panel.</summary>
        private void FollowInTree(bool isLeft)
        {
            var tree = isLeft ? LPtree : RPtree;
            if (!tree.IsVisible) return;
            string path = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (ProviderRegistry.For(path) is not LocalFileProvider) return;

            var roots = tree.Items.OfType<TreeViewItem>().ToList();
            string? rootPath = FolderTreePaths.RootOf(path, roots.Select(r => (string)r.Tag!));
            var node = roots.FirstOrDefault(r => (string)r.Tag! == rootPath);
            if (node == null) return;

            _treeSyncing = true;
            try
            {
                foreach (var step in FolderTreePaths.StepsBelow(rootPath!, path))
                {
                    node.IsExpanded = true;
                    LoadChildren(node);
                    var next = node.Items.OfType<TreeViewItem>().FirstOrDefault(c =>
                        c.Tag is string p && string.Equals(Path.GetFileName(p), step, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                    if (next == null) break;   // hidden, or just made: stop at the nearest one shown
                    node = next;
                }
                tree.SelectedItem = node;
                var shown = node;
                Dispatcher.UIThread.Post(() => shown.BringIntoView(), DispatcherPriority.Background);
            }
            finally { _treeSyncing = false; }
        }
    }
}
