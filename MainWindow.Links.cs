using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>
    /// Symbolic-link commands from a panel's right-click menu: go to where a link points, and make
    /// links in the other panel's folder to the selected items (as Directory Opus's MakeLink did).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Follows the link under the cursor (or the first selected one): a link to a folder opens
        /// that folder; a link to a file opens the file's folder with the cursor on it.
        /// </summary>
        private void GoToLinkTarget(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            var item = (grid.CursorItem as AFileEntry) ?? grid.SelectedItems.OfType<AFileEntry>().FirstOrDefault();

            if (item?.LinkTarget == null)
            {
                _ = new MessageBox("Put the cursor on a symbolic link first (its Attributes show LNK).", "Go to Link Target").ShowDialog(this);
                return;
            }

            string? target = LocalFileProvider.ResolveLink(FileUtility.JoinPanelPath(folder, item.Name));
            if (target == null)
            {
                _ = new MessageBox($"\"{item.Name}\" can't be followed.", "Go to Link Target").ShowDialog(this);
                return;
            }

            string side = isLeft ? "LP" : "RP";
            if (Directory.Exists(target))
            {
                NavigatePanelTo(side, target);
            }
            else if (File.Exists(target))
            {
                string parent = Path.GetDirectoryName(target) ?? target;
                PutCursorOnNextListing(isLeft, parent, Path.GetFileName(target), isDir: false);
                NavigatePanelTo(side, parent);
            }
            else
            {
                _ = new MessageBox($"\"{item.Name}\" points to\n{target}\nwhich doesn't exist (the link is broken).",
                                   "Go to Link Target").ShowDialog(this);
            }
        }

        /// <summary>
        /// Makes a symbolic link in the other panel's folder for each selected item (or the one under
        /// the cursor), pointing to it by full path. A name already taken gets a number added.
        /// Local folders only: cloud storage and archives have no symbolic links.
        /// </summary>
        private async void MakeLinksInOtherPanel(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string from = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            string to = (isLeft ? RPpath.Text : LPpath.Text) ?? "";

            var picked = grid.SelectedItems.OfType<AFileEntry>().ToList();
            if (picked.Count == 0 && grid.CursorItem is AFileEntry cursor) picked.Add(cursor);
            picked = picked.Where(p => !string.IsNullOrEmpty(p.Name) && p.Name != "..").ToList();
            if (picked.Count == 0)
            {
                await new MessageBox("Select what to link to first.", "Make Link").ShowDialog(this);
                return;
            }
            if (ProviderRegistry.For(from) is not LocalFileProvider || ProviderRegistry.For(to) is not LocalFileProvider)
            {
                await new MessageBox("Symbolic links can only be made between folders on this computer — " +
                                     "not in cloud storage or inside archives.", "Make Link").ShowDialog(this);
                return;
            }

            var problems = new List<string>();
            foreach (var item in picked)
            {
                string target = Path.GetFullPath(FileUtility.JoinPanelPath(from, item.Name));
                string name = ArchiveCreator.UniqueName(item.Name, n =>
                {
                    string p = FileUtility.JoinPanelPath(to, n);
                    return File.Exists(p) || Directory.Exists(p) || LocalFileProvider.IsLink(p);
                });
                try
                {
                    LocalFileProvider.CreateLink(FileUtility.JoinPanelPath(to, name), target, item.Typ);
                }
                catch (Exception ex)
                {
                    problems.Add($"{item.Name}: {ex.Message}");
                }
            }

            RefreshPanelsShowing(to);
            if (problems.Count > 0)
                await new MessageBox($"{problems.Count} link(s) couldn't be made:\n\n" + string.Join("\n", problems.Take(10)),
                                     "Make Link").ShowDialog(this);
        }
    }
}
