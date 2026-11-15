using System.IO;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>Opening Find Duplicates from a panel, and the panel actions its window uses.</summary>
    public partial class MainWindow
    {
        private void OpenDuplicates(bool isLeft)
        {
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            string other = (isLeft ? RPpath.Text : LPpath.Text) ?? "";
            if (string.IsNullOrEmpty(folder)) return;

            // Every candidate file is read, which for cloud storage means downloading it.
            if (ProviderRegistry.For(folder).IsRemote)
            {
                _ = new MessageBox("Find Duplicates reads the files it compares, so it works on folders on this computer " +
                                   "and in archives, not in cloud storage.", "Find Duplicates").ShowDialog(this);
                return;
            }
            bool otherUsable = !string.IsNullOrEmpty(other) && !PathsEqual(other, folder) && !ProviderRegistry.For(other).IsRemote;
            bool showHidden = ChkShowHidden?.IsChecked ?? false;
            new DuplicatesWindow(this, folder, otherUsable ? other : null, showHidden, isLeft).Show(this);
        }

        /// <summary>Shows <paramref name="path"/>'s folder in a panel with the cursor on it.</summary>
        internal void RevealInPanel(bool isLeft, string path)
        {
            string? parent = ArchivePath.IsArchiveUri(path) ? ArchivePath.Parse(path).ParentUri() : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent)) return;
            PutCursorOnNextListing(isLeft, parent, ConflictResolver.LeafOf(path), isDir: false);
            NavigatePanelTo(isLeft ? "LP" : "RP", parent);
            Activate();
        }

        internal void RefreshBothPanels()
        {
            RefreshPanel(isLeft: true, manual: true);
            RefreshPanel(isLeft: false, manual: true);
        }
    }
}
