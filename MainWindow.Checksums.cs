using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>Opening the Checksums window from a panel (right-click → Checksums…).</summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Opens the Checksums window for a panel's selection (or the row under the cursor if nothing
        /// is selected). Selecting only checksum files (.sha256, SHA256SUMS…) verifies them instead.
        /// </summary>
        private void OpenChecksums(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (string.IsNullOrEmpty(folder)) return;

            var picked = grid.SelectedItems.OfType<AFileEntry>().ToList();
            if (picked.Count == 0 && grid.CursorItem is AFileEntry cursor) picked.Add(cursor);
            var selection = picked.Where(f => !string.IsNullOrEmpty(f.Name) && f.Name != "..")
                                  .Select(f => (f.Name, f.Typ)).ToList();
            if (selection.Count == 0)
            {
                _ = new MessageBox("Select the files (or folders) to checksum first.", "Checksums").ShowDialog(this);
                return;
            }

            var win = new ChecksumWindow(folder, selection) { Saved = RefreshPanelsShowing };
            win.Show(this);
        }
    }
}
