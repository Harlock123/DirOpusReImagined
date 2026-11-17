using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// DORI and the world around it: folders sent from other programs (and later copies of DORI),
    /// drives being plugged in and ejected, Connect to Server, and the daily update check.
    /// </summary>
    public partial class MainWindow
    {
        private DriveWatcher? _driveWatcher;

        private void InitOutsideRelease36()
        {
            SingleInstance.StartServer(lines => Dispatcher.UIThread.Post(() =>
            {
                var scripts = lines.Select(SingleInstance.DecodeScript).Where(x => x != null).Cast<string>().ToList();
                var paths = lines.Where(l => SingleInstance.DecodeScript(l) == null).ToList();
                if (paths.Count > 0) OpenOutsidePaths(paths, bringToFront: true);
                foreach (var script in scripts) _ = RunScriptAsync(script, "sent from another program");
            }));
            Closing += (_, _) => SingleInstance.StopServer();

            _driveWatcher = new DriveWatcher(TimeSpan.FromSeconds(3));
            _driveWatcher.Added += root => Dispatcher.UIThread.Post(() => OfferNewDrive(root));
            Closing += (_, _) => _driveWatcher?.Dispose();

            Opened += (_, _) =>
            {
                // After the saved tabs are back, so the folders asked for open as new tabs beside them.
                if (SingleInstance.StartupPaths.Count > 0)
                    Dispatcher.UIThread.Post(() => OpenOutsidePaths(SingleInstance.StartupPaths, bringToFront: false), DispatcherPriority.Background);
                foreach (var script in SingleInstance.StartupScripts)
                    Dispatcher.UIThread.Post(() => _ = RunScriptAsync(script, "given when DORI started"), DispatcherPriority.Background);
                _ = CheckForUpdatesQuietlyAsync();
            };
        }

        // ---- Folders from other programs ----

        /// <summary>Opens each folder in a new tab of the active panel; a file opens its folder with the cursor on it.</summary>
        private void OpenOutsidePaths(IReadOnlyList<string> paths, bool bringToFront)
        {
            bool left = !ReferenceEquals(_activeGrid, RPgrid);
            string side = left ? "LP" : "RP";
            foreach (var p in paths)
            {
                string folder;
                string? file = null;
                try
                {
                    if (Directory.Exists(p)) folder = p;
                    else if (File.Exists(p)) { folder = Path.GetDirectoryName(p) ?? p; file = Path.GetFileName(p); }
                    else continue;
                }
                catch { continue; }
                NewTab(left);
                if (file != null) PutCursorOnNextListing(left, folder, file, isDir: false);
                NavigatePanelTo(side, folder);
            }
            if (bringToFront)
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }
        }

        // ---- Drives ----

        private void OpenDevices()
            => new DevicesWindow((left, root) => NavigatePanelTo(left ? "LP" : "RP", root), EjectDeviceAsync).Show(this);

        /// <summary>Moves any panel off the drive, then ejects it. Null when it went, else why not.</summary>
        private async Task<string?> EjectDeviceAsync(DeviceInfo device)
        {
            string root = device.Root.TrimEnd('/', '\\');
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var (side, path) in new[] { ("LP", LPpath.Text ?? ""), ("RP", RPpath.Text ?? "") })
            {
                bool onIt = path.Length > 0 && (PathsEqual(path.TrimEnd('/', '\\'), root) ||
                            path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                if (onIt) NavigatePanelTo(side, home);
            }
            // The panels' folder watchers let go of the drive as they move.
            await Task.Delay(300);
            return await Task.Run(() => Devices.Eject(device));
        }

        private Window? _driveNotice;

        /// <summary>A drive was plugged in: offer to open it, unless that's been turned off.</summary>
        private void OfferNewDrive(string root)
        {
            if (!DriveWatcher.AskOnPlugIn || _driveNotice != null) return;
            var device = Devices.Find(root);
            string name = device?.Display ?? root;
            string size = device is { Total: > 0 } ? $" ({ProgressFormat.Bytes(device.Total)})" : "";

            var text = new TextBlock { Text = $"{name}{size} was plugged in.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.Bold };
            var dontAsk = new CheckBox { Content = "Don't ask when drives are plugged in" };
            var left = new Button { Content = "Open in Left" };
            var right = new Button { Content = "Open in Right" };
            var later = new Button { Content = "Not Now", IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(left);
            buttons.Children.Add(right);
            buttons.Children.Add(later);
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
            panel.Children.Add(text);
            panel.Children.Add(dontAsk);
            panel.Children.Add(buttons);
            var window = new Window
            {
                Title = "Drive plugged in", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel,
            };
            window.Bind(BackgroundProperty, window.GetResourceObservable("WindowBackgroundBrush"));
            void Done(string? side)
            {
                if (dontAsk.IsChecked == true) DriveWatcher.AskOnPlugIn = false;
                window.Close();
                if (side != null) NavigatePanelTo(side, root);
            }
            left.Click += (_, _) => Done("LP");
            right.Click += (_, _) => Done("RP");
            later.Click += (_, _) => Done(null);
            window.Closed += (_, _) => _driveNotice = null;
            _driveNotice = window;
            window.Show(this);
        }

        // ---- Connect to Server ----

        private async void OpenConnectServer(bool isLeft)
        {
            string? path = await new ConnectServerWindow().ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(path)) NavigatePanelTo(isLeft ? "LP" : "RP", path);
        }

        // ---- Updates ----

        /// <summary>The once-a-day check, when turned on. Says nothing unless there is a newer DORI.</summary>
        private async Task CheckForUpdatesQuietlyAsync()
        {
            try
            {
                if (!UpdateCheck.DueToday(DateTime.Now)) return;
                var latest = await UpdateCheck.GetLatestAsync();
                UpdateCheck.MarkChecked(DateTime.Now);
                if (latest != null && UpdateCheck.IsNewer(latest.Version, UpdateCheck.Current))
                    new UpdateWindow(latest).Show(this);
            }
            catch { /* offline, or GitHub busy: try again tomorrow */ }
        }
    }
}
