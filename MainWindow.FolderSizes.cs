using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Folder sizes in the Size column (Ctrl/Cmd+Shift+S for the selected folders, or right-click →
    /// Calculate All Folder Sizes). A size stays while the panel shows that folder, through
    /// refreshes, so Sort by Size orders the folders by it and the status bar counts selected
    /// folders. Linked folders are never walked into.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Sizes measured in the folder a panel shows: which folder, and each subfolder's bytes by name.</summary>
        private sealed class PanelFolderSizes
        {
            public string Folder = "";
            public readonly Dictionary<string, long> Bytes = new(StringComparer.Ordinal);
        }

        private readonly PanelFolderSizes _lpFolderSizes = new(), _rpFolderSizes = new();

        private PanelFolderSizes FolderSizesFor(TaiDataGrid grid, string folder)
        {
            var sizes = ReferenceEquals(grid, LPgrid) ? _lpFolderSizes : _rpFolderSizes;
            if (!PathsEqual(sizes.Folder, folder))
            {
                sizes.Folder = folder;
                sizes.Bytes.Clear();
            }
            return sizes;
        }

        /// <summary>Selected folders, or the folder under the cursor; with no folder picked, every folder.</summary>
        private void CalculatePickedFolderSizes(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            var folders = entries.Where(e => e.Typ).ToList();
            if (folders.Count == 0) { CalculateFolderSizes(isLeft ? LPgrid : RPgrid, all: true); return; }
            _ = MeasureFoldersAsync(isLeft ? LPgrid : RPgrid, folder, folders);
        }

        private void Handle_CalculateAllFolderSizes(object? sender, EventArgs e)
        {
            if (sender is TaiDataGrid grid) CalculateFolderSizes(grid, all: true);
        }

        private void CalculateFolderSizes(TaiDataGrid grid, bool all)
        {
            string folder = (ReferenceEquals(grid, LPgrid) ? LPpath.Text : RPpath.Text) ?? "";
            if (string.IsNullOrEmpty(folder)) return;
            var folders = grid.Items.OfType<AFileEntry>().Where(x => x.Typ && x.Name is not ("." or "..")).ToList();
            if (folders.Count == 0) return;
            _ = MeasureFoldersAsync(grid, folder, folders);
        }

        /// <summary>
        /// Measures the folders one at a time off the UI thread, filling in each row as it lands.
        /// Stops if the panel moves to another folder.
        /// </summary>
        private async Task MeasureFoldersAsync(TaiDataGrid grid, string folder, List<AFileEntry> rows)
        {
            var sizes = FolderSizesFor(grid, folder);
            foreach (var row in rows) row.FileSize = "…";
            grid.ReRender();

            foreach (var row in rows)
            {
                string path = FileUtility.JoinPanelPath(folder, row.Name);
                long bytes;
                try { bytes = await Task.Run(() => FileUtility.GetDirectorySizeRecursive(path)); }
                catch { bytes = -1; }

                string now = (ReferenceEquals(grid, LPgrid) ? LPpath.Text : RPpath.Text) ?? "";
                if (!PathsEqual(now, folder)) return;   // moved on; the new listing has new rows

                if (bytes >= 0) sizes.Bytes[row.Name] = bytes;
                ShowFolderSize(row, bytes);
                grid.ReRender();
                UpdateStatusBar();
            }

            // Sorted by size: put the folders in order now their sizes are known.
            var sort = ReferenceEquals(grid, LPgrid) ? _lpSort : _rpSort;
            if (sort.Key == SortKey.Size) RefreshPanel(ReferenceEquals(grid, LPgrid), manual: true);
        }

        /// <summary>Lists both panels again in their sort order (after the natural-sort option changes).</summary>
        internal void ResortPanels()
        {
            ApplyPanelSort(LPgrid);
            ApplyPanelSort(RPgrid);
        }

        private static void ShowFolderSize(AFileEntry row, long bytes)
        {
            row.FolderBytes = bytes;
            row.FileSize = bytes < 0 ? "" : bytes == 0 ? "0 b" : row.ConvertNumberToReadableString(bytes);
        }

        /// <summary>
        /// After a listing: puts the sizes measured earlier in this folder back on its rows, and when
        /// the panel is sorted by size, orders the folders by them (unmeasured folders last).
        /// </summary>
        private void ApplyFolderSizes(TaiDataGrid grid, string? folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            var sizes = FolderSizesFor(grid, folder);
            if (sizes.Bytes.Count == 0) return;

            foreach (var row in grid.Items.OfType<AFileEntry>())
                if (row.Typ && sizes.Bytes.TryGetValue(row.Name, out long b)) ShowFolderSize(row, b);

            var sort = ReferenceEquals(grid, LPgrid) ? _lpSort : _rpSort;
            if (sort.Key != SortKey.Size) return;
            grid.Items = FolderSizeOrder.Apply(grid.Items, sort.Ascending);
        }
    }

    /// <summary>Orders the folder rows of a listing by their measured size, leaving ".." first and the files where they are.</summary>
    public static class FolderSizeOrder
    {
        public static List<object> Apply(IReadOnlyList<object> items, bool ascending)
        {
            var result = new List<object>(items.Count);
            var folders = new List<AFileEntry>();
            int firstFile = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is AFileEntry { Typ: true } f && f.Name != "..") folders.Add(f);
                else if (items[i] is AFileEntry { Name: ".." }) result.Add(items[i]);
                else if (firstFile < 0) firstFile = i;
            }
            // Unmeasured folders (−1) go last either way; ties keep name order.
            var measured = folders.Where(f => f.FolderBytes >= 0);
            var ordered = (ascending ? measured.OrderBy(f => f.FolderBytes) : measured.OrderByDescending(f => f.FolderBytes))
                .Concat(folders.Where(f => f.FolderBytes < 0));
            result.AddRange(ordered);
            if (firstFile >= 0)
                for (int i = firstFile; i < items.Count; i++)
                    if (!(items[i] is AFileEntry { Typ: true } || items[i] is AFileEntry { Name: ".." })) result.Add(items[i]);
            return result;
        }
    }
}
