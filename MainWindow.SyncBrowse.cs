using System;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Synced browsing (the ⇄ Follow button beside the button bank tabs, or right-click → Synced
    /// Browsing): the other panel follows into subfolders of the same name and up again. See
    /// <see cref="SyncBrowse.Follow"/> for the rule.
    /// </summary>
    public partial class MainWindow
    {
        private bool _syncBrowse;
        private bool _syncFollowing;
        private string _lpLastPath = "", _rpLastPath = "";

        private void InitSyncBrowse()
        {
            SyncBrowseToggle.Click += (_, _) => SetSyncBrowse(SyncBrowseToggle.IsChecked == true);
        }

        private void ToggleSyncBrowse() => SetSyncBrowse(!_syncBrowse);

        private void SetSyncBrowse(bool on)
        {
            _syncBrowse = on;
            SyncBrowseToggle.IsChecked = on;
            LPgrid.SetSyncBrowseChecked(on);
            RPgrid.SetSyncBrowseChecked(on);
        }

        /// <summary>Called after a panel shows a folder: moves the other panel to match when synced browsing is on.</summary>
        private void AfterPanelNavigated(bool isLeft)
        {
            FollowInTree(isLeft);
            string now = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            string before = isLeft ? _lpLastPath : _rpLastPath;
            if (isLeft) _lpLastPath = now; else _rpLastPath = now;
            if (!_syncBrowse || _syncFollowing || before == now) return;

            string other = (isLeft ? RPpath.Text : LPpath.Text) ?? "";
            if (string.IsNullOrEmpty(other) || ProviderRegistry.For(other).IsRemote) return;
            string? target = SyncBrowse.Follow(before, now, other, p =>
            {
                try { return ProviderRegistry.For(p).DirectoryExists(p); } catch { return false; }
            });
            if (target == null) return;

            _syncFollowing = true;
            try { NavigatePanelTo(isLeft ? "RP" : "LP", target); }
            finally { _syncFollowing = false; }
        }
    }
}
