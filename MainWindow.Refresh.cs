using System;
using System.IO;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined
{
    /// <summary>
    /// Panel refresh (Ctrl/Cmd+R and the panel menu) and automatic refresh of local folders when
    /// something outside DORI changes them. Both re-list the folder and put the cursor, selection,
    /// scroll position and filter back, so a refresh never loses the user's place.
    /// </summary>
    public partial class MainWindow
    {
        // Quiet period after the last change before a watched panel re-lists, and the longest a
        // continuous stream of changes (a big copy, a growing download) can hold a refresh off.
        private static readonly TimeSpan WatchDebounce = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan WatchMaxDelay = TimeSpan.FromSeconds(3);

        private sealed class PanelWatch
        {
            public FileSystemWatcher? Watcher;
            public string Path = "";
            public DispatcherTimer? Timer;
            public DateTime FirstPendingUtc = DateTime.MinValue;
            // A change arrived while the panels were showing a compare; refresh once it's cleared.
            public bool DeferredByCompare;
        }

        private readonly PanelWatch _lpWatch = new();
        private readonly PanelWatch _rpWatch = new();

        // View state to restore once a refresh's listing lands (cloud listings arrive asynchronously).
        private (string Path, TaiDataGrid.ViewState State, int CursorIndex)? _lpPendingView;
        private (string Path, TaiDataGrid.ViewState State, int CursorIndex)? _rpPendingView;

        /// <summary>
        /// Re-lists one panel in place. A manual refresh also drops the cloud listing cache so a
        /// remote folder is fetched fresh, and refreshes the other panel when it shows the same folder.
        /// </summary>
        private void RefreshPanel(bool isLeft, bool manual)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string path = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (string.IsNullOrEmpty(path)) return;

            var provider = ProviderRegistry.For(path);

            // A local folder deleted or renamed under us: step up to the nearest folder that still
            // exists instead of showing an error.
            if (provider is LocalFileProvider && !Directory.Exists(path))
            {
                string? parent = NearestExistingParent(path);
                if (parent != null) NavigatePanelInPlace(isLeft, parent);
                return;
            }

            if (manual)
            {
                if (provider.IsRemote) RcloneListCache.Clear();
                if (_comparing) ClearCompare();
            }

            var pending = (path, grid.CaptureViewState(), grid.CursorIndex);
            if (isLeft) _lpPendingView = pending; else _rpPendingView = pending;

            bool showHidden = ChkShowHidden?.IsChecked ?? false;
            FileUtility.PopulateFilePanel(grid, path, showHidden, isLeft ? _lpSort : _rpSort);

            if (manual)
            {
                string other = (isLeft ? RPpath.Text : LPpath.Text) ?? "";
                if (PathsEqual(other, path)) RefreshPanel(!isLeft, manual: false);
            }
        }

        /// <summary>
        /// Called from <see cref="OnPanelPopulated"/> after the filter is applied: restores the view
        /// state saved by <see cref="RefreshPanel"/> (if this listing is the refresh it was saved for)
        /// and points the panel's watcher at the folder now shown.
        /// </summary>
        private void AfterPanelPopulated(TaiDataGrid grid)
        {
            bool isLeft = ReferenceEquals(grid, LPgrid);
            string path = (isLeft ? LPpath.Text : RPpath.Text) ?? "";

            var pending = isLeft ? _lpPendingView : _rpPendingView;
            if (isLeft) _lpPendingView = null; else _rpPendingView = null;
            if (pending is { } p && PathsEqual(p.Path, path))
                grid.RestoreViewState(p.State, p.CursorIndex);

            WatchPanel(isLeft, path);
        }

        /// <summary>Starts (or moves) the watcher for a panel. Only plain local folders are watched.</summary>
        private void WatchPanel(bool isLeft, string path)
        {
            var w = isLeft ? _lpWatch : _rpWatch;
            if (w.Watcher != null && PathsEqual(w.Path, path)) return;

            StopWatching(w);
            w.Path = path;

            if (string.IsNullOrEmpty(path) || ProviderRegistry.For(path) is not LocalFileProvider) return;
            if (!Directory.Exists(path)) return;

            try
            {
                var fsw = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                };
                FileSystemEventHandler changed = (_, _) => OnWatchedChange(isLeft);
                fsw.Created += changed;
                fsw.Deleted += changed;
                fsw.Changed += changed;
                fsw.Renamed += (_, _) => OnWatchedChange(isLeft);
                // Buffer overflow or the folder itself vanishing: a re-list sorts out either.
                fsw.Error += (_, _) => OnWatchedChange(isLeft);
                fsw.EnableRaisingEvents = true;
                w.Watcher = fsw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                          or PlatformNotSupportedException)
            {
                // e.g. the Linux inotify watch limit is reached; the panel still works, it just
                // won't refresh itself. Ctrl/Cmd+R remains available.
                StopWatching(w);
            }
        }

        private static void StopWatching(PanelWatch w)
        {
            if (w.Watcher != null)
            {
                w.Watcher.EnableRaisingEvents = false;
                w.Watcher.Dispose();
                w.Watcher = null;
            }
            w.Timer?.Stop();
            w.FirstPendingUtc = DateTime.MinValue;
            w.DeferredByCompare = false;
        }

        /// <summary>Watcher thread → UI thread: (re)arms the panel's debounce timer.</summary>
        private void OnWatchedChange(bool isLeft)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var w = isLeft ? _lpWatch : _rpWatch;
                if (w.Watcher == null) return;

                var now = DateTime.UtcNow;
                if (w.FirstPendingUtc == DateTime.MinValue) w.FirstPendingUtc = now;

                if (w.Timer == null)
                {
                    w.Timer = new DispatcherTimer();
                    w.Timer.Tick += (_, _) => OnWatchTimer(isLeft);
                }
                w.Timer.Stop();

                // Trailing debounce, but never wait past WatchMaxDelay from the first change.
                var untilMax = w.FirstPendingUtc + WatchMaxDelay - now;
                var delay = untilMax < WatchDebounce ? untilMax : WatchDebounce;
                w.Timer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
                w.Timer.Start();
            });
        }

        private void OnWatchTimer(bool isLeft)
        {
            var w = isLeft ? _lpWatch : _rpWatch;
            w.Timer?.Stop();
            w.FirstPendingUtc = DateTime.MinValue;

            // The panel moved on since the change was queued.
            string path = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (w.Watcher == null || !PathsEqual(w.Path, path)) return;

            // A re-list would wipe the compare colours the user is looking at; wait until it's cleared.
            if (_comparing) { w.DeferredByCompare = true; return; }

            RefreshPanel(isLeft, manual: false);
        }

        /// <summary>Runs the refreshes that changes during a compare held back. Called by ClearCompare.</summary>
        private void RunDeferredWatchRefreshes()
        {
            if (_lpWatch.DeferredByCompare) { _lpWatch.DeferredByCompare = false; RefreshPanel(true, manual: false); }
            if (_rpWatch.DeferredByCompare) { _rpWatch.DeferredByCompare = false; RefreshPanel(false, manual: false); }
        }

        private void StopAllWatchers()
        {
            StopWatching(_lpWatch);
            StopWatching(_rpWatch);
        }

        /// <summary>Navigates a panel to <paramref name="path"/>, keeping its sort, as a normal step in its history.</summary>
        private void NavigatePanelInPlace(bool isLeft, string path)
        {
            string current = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (!string.IsNullOrEmpty(current)) PushHistory(isLeft, current);

            bool showHidden = ChkShowHidden?.IsChecked ?? false;
            if (isLeft)
            {
                LPpath.Text = path;
                LPfilter.Text = "";
                FileUtility.PopulateFilePanel(LPgrid, path, showHidden, _lpSort);
                RefreshLPGridPostActions();
            }
            else
            {
                RPpath.Text = path;
                RPfilter.Text = "";
                FileUtility.PopulateFilePanel(RPgrid, path, showHidden, _rpSort);
                RefreshRPGridPostActions();
            }
        }

        private static string? NearestExistingParent(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
                while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    dir = Path.GetDirectoryName(dir);
                return string.IsNullOrEmpty(dir) ? null : dir;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            // Windows and macOS file systems are case-insensitive by default; Linux is not.
            var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), cmp);
        }
    }
}
