namespace DirOpusReImagined
{
    /// <summary>Disk catalogues (right-click → File Tools → Disk Catalogues…).</summary>
    public partial class MainWindow
    {
        private CataloguesWindow? _cataloguesWindow;

        private void OpenCatalogues(bool isLeft)
        {
            if (_cataloguesWindow != null) { _cataloguesWindow.Activate(); return; }
            _cataloguesWindow = new CataloguesWindow(
                () => isLeft ? LPpath.Text : RPpath.Text,
                (folder, name) =>
                {
                    if (name != null) PutCursorOnNextListing(isLeft, folder, name, isDir: false);
                    NavigatePanelTo(isLeft ? "LP" : "RP", folder);
                    Activate();
                });
            _cataloguesWindow.Closed += (_, _) => _cataloguesWindow = null;
            _cataloguesWindow.Show(this);
        }
    }
}
