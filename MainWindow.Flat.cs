namespace DirOpusReImagined
{
    /// <summary>
    /// Flat view: a panel lists every file in its folder and all the folders below, each named by
    /// its path relative to the folder ("src/app/main.cs"), so a whole tree can be sorted, filtered
    /// and selected from as one list. Copies and moves from it keep each file's sub-folder.
    /// Toggled with the Flat button in the panel's tab bar, right-click → Flat View, or
    /// Ctrl/Cmd+Shift+F; going to another folder turns it off.
    /// </summary>
    public partial class MainWindow
    {
        private void InitFlatView()
        {
            LPflatToggle.Click += (_, _) => ToggleFlatView(isLeft: true);
            RPflatToggle.Click += (_, _) => ToggleFlatView(isLeft: false);
        }

        private void ToggleFlatView(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string path = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (string.IsNullOrEmpty(path)) return;

            grid.FlatRoot = grid.FlatRoot == null ? path : null;
            grid.FlatTruncated = false;
            UpdateFlatToggle(grid);
            RefreshPanel(isLeft, manual: true);
        }

        /// <summary>Keeps the Flat button pressed exactly while the panel is flat (navigating away turns it off).</summary>
        private void UpdateFlatToggle(TaiDataGrid grid)
        {
            var toggle = ReferenceEquals(grid, LPgrid) ? LPflatToggle : RPflatToggle;
            toggle.IsChecked = grid.FlatRoot != null;
        }
    }
}
