using System.Linq;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Opening Set Dates &amp; Permissions for a panel's selection (right-click menu).</summary>
    public partial class MainWindow
    {
        private void OpenSetAttributes(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (string.IsNullOrEmpty(folder)) return;

            if (ProviderRegistry.For(folder) is not LocalFileProvider)
            {
                _ = new MessageBox("Dates and permissions can only be changed for files on this computer, " +
                                   "not in cloud storage or inside archives.", "Set Dates & Permissions").ShowDialog(this);
                return;
            }

            var picked = grid.SelectedItems.OfType<AFileEntry>().ToList();
            if (picked.Count == 0 && grid.CursorItem is AFileEntry cursor) picked.Add(cursor);
            var selection = picked.Where(f => !string.IsNullOrEmpty(f.Name) && f.Name != "..")
                                  .Select(f => (FileUtility.JoinPanelPath(folder, f.Name), f.Typ)).ToList();
            if (selection.Count == 0)
            {
                _ = new MessageBox("Select the files or folders to change first.", "Set Dates & Permissions").ShowDialog(this);
                return;
            }

            new SetAttributesWindow(folder, selection, () => RefreshPanelsShowing(folder)).ShowDialog(this);
        }
    }
}
