using System.Collections.Generic;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>Comparing two files side by side (right-click → Compare Files…).</summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Compares two files selected in the active panel, or else one file from each panel (the
        /// first selected, or the one under the cursor).
        /// </summary>
        private void OpenFileCompare(bool isLeft)
        {
            var active = isLeft ? LPgrid : RPgrid;
            var other = isLeft ? RPgrid : LPgrid;
            string activeFolder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            string otherFolder = (isLeft ? RPpath.Text : LPpath.Text) ?? "";

            static List<string> Files(TaiDataGrid g) => g.SelectedItems.OfType<AFileEntry>()
                .Where(f => !f.Typ && !string.IsNullOrEmpty(f.Name) && f.Name != "..").Select(f => f.Name).ToList();
            static string? CursorFile(TaiDataGrid g) => g.CursorItem is AFileEntry { Typ: false } f && f.Name != ".." ? f.Name : null;

            var pick = TextDiff.PickFiles(Files(active), CursorFile(active), Files(other), CursorFile(other));
            if (pick == null)
            {
                _ = new MessageBox("Select two files in this panel, or one file here and one in the other panel, " +
                                   "then choose Compare Files again.", "Compare Files").ShowDialog(this);
                return;
            }

            var (a, b) = pick.Value;
            string pathA = FileUtility.JoinPanelPath(a.Active ? activeFolder : otherFolder, a.Name);
            string pathB = FileUtility.JoinPanelPath(b.Active ? activeFolder : otherFolder, b.Name);
            // Keep the left panel's file on the left when comparing across panels.
            if (!isLeft && a.Active != b.Active) (pathA, pathB) = (pathB, pathA);
            new FileCompareWindow(pathA, pathB).Show(this);
        }
    }
}
