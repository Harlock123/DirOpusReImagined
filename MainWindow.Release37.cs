using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined
{
    /// <summary>
    /// Scripts, previous versions, similar photos, photo details, PDF tools, Extract Here and
    /// Test Archive, administrator actions, cloud share links and mounts, and settings.
    /// </summary>
    public partial class MainWindow : IScriptHost
    {
        // ---- Scripts ----

        private RunScriptWindow? _scriptWindow;

        /// <summary>Runs a script, saying what went wrong (with its line) if it fails. <paramref name="origin"/> says where it came from.</summary>
        internal async Task<bool> RunScriptAsync(string script, string origin, Action<string>? log = null, CancellationToken ct = default)
        {
            try
            {
                var statements = DoriScript.Parse(script);
                LeftActive = !ReferenceEquals(_activeGrid, RPgrid);
                await DoriScript.RunAsync(statements, this, log, ct);
                return true;
            }
            catch (OperationCanceledException) { log?.Invoke("stopped"); return false; }
            catch (Exception ex)
            {
                log?.Invoke(ex.Message);
                if (log == null) await new MessageBox($"A script {origin} stopped: {ex.Message}", "Script").ShowDialog(this);
                return false;
            }
        }

        private void OpenRunScript()
        {
            if (_scriptWindow != null) { _scriptWindow.Activate(); return; }
            _scriptWindow = new RunScriptWindow((text, log, ct) => RunScriptAsync(text, "", log, ct));
            _scriptWindow.Closed += (_, _) => _scriptWindow = null;
            _scriptWindow.Show(this);
        }

        // IScriptHost

        public bool LeftActive { get; set; } = true;

        public string PanelPath(bool left) => (left ? LPpath.Text : RPpath.Text) ?? "";

        public Task GoAsync(bool left, string path)
        {
            NavigatePanelTo(left ? "LP" : "RP", path);
            return Task.CompletedTask;
        }

        public Task NewTabAsync(bool left, string path)
        {
            NewTab(left);
            NavigatePanelTo(left ? "LP" : "RP", path);
            return Task.CompletedTask;
        }

        public int Select(bool left, string patterns, bool add)
        {
            var grid = left ? LPgrid : RPgrid;
            if (!add) grid.SelectedItems.Clear();
            // Patterns are separated by spaces or commas (";" separates commands in a script).
            foreach (var p in patterns.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                grid.SelectByPattern(FileSearch.MakeNameMatcher(p), filesOnly: false);
            grid.ReRender();
            UpdateStatusBar();
            return grid.SelectedItems.Count;
        }

        public void SelectAll(bool left)
        {
            var grid = left ? LPgrid : RPgrid;
            grid.SelectAllFilesOnly();
            grid.ReRender();
            UpdateStatusBar();
        }

        public void Deselect(bool left)
        {
            var grid = left ? LPgrid : RPgrid;
            grid.SelectedItems.Clear();
            grid.ReRender();
            UpdateStatusBar();
        }

        public void Invert(bool left) => (left ? LPgrid : RPgrid).InvertSelection();

        public int SelectedCount(bool left) => (left ? LPgrid : RPgrid).SelectedItems.Count;

        public void Filter(bool left, string text) => SetPanelFilter(left, text);

        public Task RefreshAsync(bool left)
        {
            RefreshPanel(left, manual: true);
            return Task.CompletedTask;
        }

        public bool Command(bool left, string name)
        {
            if (!Enum.TryParse(name.Trim(), ignoreCase: true, out ShortcutCommand command)) return false;
            (left ? LPgrid : RPgrid).RunShortcut(command);
            return true;
        }

        public bool Button(string label)
        {
            foreach (var bank in _banks)
                foreach (var b in bank.Buttons)
                    if (!string.IsNullOrWhiteSpace(b.Action) && string.Equals((b.Content ?? "").Trim(), label.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        RunButtonEntry(ToEntry(b));
                        return true;
                    }
            return false;
        }

        public Task RunProgramAsync(bool left, string commandLine)
        {
            _commandPanel = left ? LPgrid : RPgrid;
            CommandLineBox.Text = commandLine;
            RunCommandLine();
            return Task.CompletedTask;
        }

        public async Task WaitForOperationsAsync(CancellationToken ct)
        {
            await Task.Delay(300, ct);   // let a copy just asked for reach the queue
            while (OperationQueue.Instance.HasActiveWork) await Task.Delay(250, ct);
        }

        public Task MessageAsync(string text) => new MessageBox(text, "Script").ShowDialog(this);

        public bool Exists(string path) => UndoPlanner.Exists(path);

        // ---- Previous versions ----

        private void OpenPreviousVersions(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Previous Versions")) return;
            string path = entries.Count > 0 ? FileUtility.JoinPanelPath(folder, entries[0].Name) : folder;
            string side = isLeft ? "LP" : "RP";
            new PreviousVersionsWindow(path,
                show: p => NavigatePanelTo(side, Directory.Exists(p) ? p : Path.GetDirectoryName(p)!),
                view: p => OpenFileViewer(Path.GetDirectoryName(p)!, Path.GetFileName(p)),
                compare: (a, b) => new FileCompareWindow(a, b).Show(this),
                trash: TrashForRestoreAsync,
                refresh: RefreshPanelsShowing).Show(this);
        }

        /// <summary>Moves the current copy to the trash before an older one takes its place (undoable).</summary>
        private async Task<bool> TrashForRestoreAsync(string path)
        {
            if (!TrashService.IsSupported(path)) return false;
            string folder = Path.GetDirectoryName(path) ?? "";
            var item = await Task.Run(() => TrashService.Trash(path));
            if (item != null && UndoPlanner.RecordTrash(folder, new[] { item }, DateTime.Now) is { } undo) PushUndo(undo);
            return true;
        }

        // ---- Similar photos ----

        private void OpenSimilarPhotos(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Find Similar Photos")) return;
            var grid = isLeft ? LPgrid : RPgrid;
            bool selected = grid.SelectedItems.OfType<AFileEntry>().Any(e => e.Name != "..");
            var picked = selected ? entries.Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList() : new List<string> { folder };
            string side = isLeft ? "LP" : "RP";
            new SimilarPhotosWindow(picked, folder,
                trash: TrashPathsAsync,
                show: p => { PutCursorOnNextListing(isLeft, Path.GetDirectoryName(p)!, Path.GetFileName(p), isDir: false); NavigatePanelTo(side, Path.GetDirectoryName(p)!); }).Show(this);
        }

        /// <summary>Moves files to the trash as one undoable step, refreshing the panels showing them. Returns how many went.</summary>
        private async Task<int> TrashPathsAsync(IReadOnlyList<string> paths)
        {
            var done = new List<TrashedItem>();
            int count = 0;
            await Task.Run(() =>
            {
                foreach (var p in paths)
                {
                    try
                    {
                        if (!TrashService.IsSupported(p)) continue;
                        if (TrashService.Trash(p) is { } item) done.Add(item);
                        count++;
                    }
                    catch (Exception ex) { CrashLog.Write("Trash " + p, ex); }
                }
            });
            foreach (var group in done.GroupBy(d => Path.GetDirectoryName(d.OriginalPath) ?? ""))
                if (UndoPlanner.RecordTrash(group.Key, group.ToList(), DateTime.Now) is { } undo) PushUndo(undo);
            foreach (var dir in paths.Select(p => Path.GetDirectoryName(p) ?? "").Distinct()) RefreshPanelsShowing(dir);
            return count;
        }

        // ---- Photo details ----

        private void OpenPhotoPrivacy(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Remove Photo Details")) return;
            var files = entries.Where(e => !e.Typ && PhotoPrivacy.CanStrip(e.Name)).Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
            if (files.Count == 0)
            {
                _ = new MessageBox("Select the photos first (JPEG, PNG or WebP).", "Remove Photo Details").ShowDialog(this);
                return;
            }
            new PhotoPrivacyWindow(files, folder, OtherLocalFolder(isLeft), written => RefreshPanelsShowing(written)).ShowDialog(this);
        }

        // ---- PDF tools ----

        private void OpenPdfTools(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "PDF Tools")) return;
            var files = entries.Where(e => !e.Typ && PdfTools.IsPdf(e.Name)).Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
            if (files.Count == 0)
            {
                _ = new MessageBox("Select one or more PDFs first.", "PDF Tools").ShowDialog(this);
                return;
            }
            new PdfToolsWindow(files, folder, OtherLocalFolder(isLeft), written => RefreshPanelsShowing(written)).ShowDialog(this);
        }

        // ---- Extract Here and Test Archive ----

        /// <summary>The archive under the cursor (or the one selected), as a file path, or null after saying why.</summary>
        private string? PickedArchive(bool isLeft, string what)
        {
            var (folder, entries) = Picked(isLeft);
            var e = entries.FirstOrDefault(x => !x.Typ && ArchivePath.IsArchiveFile(x.Name));
            if (string.IsNullOrEmpty(folder) || e == null || ProviderRegistry.For(folder) is not LocalFileProvider)
            {
                _ = new MessageBox($"Put the cursor on an archive on this computer (.zip, .7z, .rar, .tar…, .iso) for {what}.", what).ShowDialog(this);
                return null;
            }
            return FileUtility.JoinPanelPath(folder, e.Name);
        }

        private async void ExtractArchiveHere(bool isLeft)
        {
            string? archive = PickedArchive(isLeft, "Extract Here");
            if (archive == null) return;
            if (!await EnsureArchivePasswordAsync(archive)) return;
            string folder = Path.GetDirectoryName(archive)!;
            string root = ArchivePath.RootUriFor(archive);
            List<AFileEntry> top;
            try
            {
                top = await Task.Run(() =>
                {
                    var p = ProviderRegistry.For(root);
                    return p.EnumerateDirectories(root).Select(d => new AFileEntry(d.Name, 0, true))
                            .Concat(p.EnumerateFiles(root).Select(f => new AFileEntry(f.Name, f.Size, false))).ToList();
                });
            }
            catch (Exception ex)
            {
                await new MessageBox($"{Path.GetFileName(archive)} couldn't be read: {ex.Message}", "Extract Here").ShowDialog(this);
                return;
            }
            if (top.Count == 0) { await new MessageBox("The archive is empty.", "Extract Here").ShowDialog(this); return; }
            var (needsFolder, target) = ExtractHere.Plan(Path.GetFileName(archive), top.Select(t => t.Name).ToList(), folder,
                                                         p => File.Exists(p) || Directory.Exists(p));
            if (needsFolder) Directory.CreateDirectory(target);
            await RunPanelTransferAsync(top.Cast<object>().ToList(), root, target, move: false);
            RefreshPanelsShowing(folder);
        }

        private void TestArchive(bool isLeft)
        {
            string? archive = PickedArchive(isLeft, "Test Archive");
            if (archive == null) return;
            _ = TestArchiveAsync(archive);
        }

        private async Task TestArchiveAsync(string archive)
        {
            if (!await EnsureArchivePasswordAsync(archive)) return;
            string name = Path.GetFileName(archive);
            ArchiveTester.Result? result = null;
            RunFileTool($"Testing {name}", archive, Path.GetDirectoryName(archive)!, (p, ct) =>
            {
                // Files tested, shown against the archive's size so the queue's bar reads sensibly.
                long size = Math.Max(1, new FileInfo(archive).Length);
                var counts = new SyncProgress<(int Done, int Total, string Name)>(x => p.Report(((long)(size * (double)x.Done / Math.Max(1, x.Total)), size)));
                result = ArchiveTester.Test(archive, counts, ct);
                var r = result;
                Dispatcher.UIThread.Post(() =>
                {
                    string text = r.Problems.Count == 0
                        ? $"{name} is fine: all {r.Files:N0} file(s) ({ProgressFormat.Bytes(r.Bytes)}) read back correctly."
                        : $"{name} has {r.Problems.Count:N0} problem(s) in {r.Files:N0} file(s):\n\n" +
                          string.Join("\n", r.Problems.Take(15).Select(x => $"{x.Entry}: {x.Problem}")) +
                          (r.Problems.Count > 15 ? $"\n… and {r.Problems.Count - 15} more." : "");
                    _ = new MessageBox(text, "Test Archive").ShowDialog(this);
                });
            });
            await Task.CompletedTask;
        }

        // ---- As administrator ----

        private async void RunAsAdministrator(bool isLeft, ElevatedAction action)
        {
            if (!Elevation.Supported)
            {
                await new MessageBox("DORI can't ask for administrator rights here (on Linux it needs pkexec, from polkit).", "As Administrator").ShowDialog(this);
                return;
            }
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "As Administrator")) return;
            var paths = entries.Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
            string? other = OtherLocalFolder(isLeft);
            List<string> args;
            string what;
            switch (action)
            {
                case ElevatedAction.Delete:
                    if (paths.Count == 0) return;
                    what = paths.Count == 1 ? $"\"{Path.GetFileName(paths[0])}\"" : $"{paths.Count} items";
                    if (!await new MessageBox($"Delete {what} for good, as administrator? They don't go to the trash, and this can't be undone.",
                                              true, "Delete", "Cancel", "As Administrator").ShowDialog<bool>(this)) return;
                    args = paths;
                    break;
                case ElevatedAction.Copy or ElevatedAction.Move:
                    if (paths.Count == 0) return;
                    if (other == null) { await new MessageBox("The other panel has to show a folder on this computer.", "As Administrator").ShowDialog(this); return; }
                    args = paths.Append(other).ToList();
                    what = $"{(action == ElevatedAction.Copy ? "copied" : "moved")} to {other}";
                    break;
                case ElevatedAction.Rename:
                {
                    if (paths.Count != 1) { await new MessageBox("Select one item to rename.", "As Administrator").ShowDialog(this); return; }
                    string? name = await new TextPromptDialog("Rename as Administrator", "New name:", Path.GetFileName(paths[0])).ShowDialog<string?>(this);
                    if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0) return;
                    args = new List<string> { paths[0], Path.Combine(folder, name.Trim()) };
                    what = "renamed";
                    break;
                }
                case ElevatedAction.NewFolder:
                {
                    string? name = await new TextPromptDialog("New Folder as Administrator", "Folder name:", "New Folder").ShowDialog<string?>(this);
                    if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0) return;
                    args = new List<string> { Path.Combine(folder, name.Trim()) };
                    what = "made";
                    break;
                }
                default: return;
            }
            string? problem = await Elevation.RunAsync(action, args);
            RefreshPanelsShowing(folder);
            if (other != null) RefreshPanelsShowing(other);
            if (problem != null) await new MessageBox("It wasn't done: " + problem, "As Administrator").ShowDialog(this);
        }

        /// <summary>The viewer's quick edit couldn't save: offer to save as administrator.</summary>
        internal async Task<bool> SaveAsAdministratorAsync(Window owner, string path, byte[] bytes)
        {
            if (!Elevation.Supported || ProviderRegistry.For(path) is not LocalFileProvider) return false;
            if (!await new MessageBox($"You don't have permission to change {Path.GetFileName(path)}. Save it as administrator?",
                                      true, "Save as Administrator", "Cancel", "Save").ShowDialog<bool>(owner)) return false;
            string temp = Path.Combine(Path.GetTempPath(), "dori-admin-" + Guid.NewGuid().ToString("N")[..10]);
            try
            {
                await File.WriteAllBytesAsync(temp, bytes);
                string? problem = await Elevation.RunAsync(ElevatedAction.WriteFile, new[] { temp, path });
                if (problem != null) { await new MessageBox("It wasn't saved: " + problem, "Save").ShowDialog(owner); return false; }
                return true;
            }
            finally { try { File.Delete(temp); } catch { } }
        }

        // ---- Cloud share links and mounts ----

        private async void CopyShareLink(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (!CloudPath.IsCloudUri(folder) || entries.Count == 0)
            {
                await new MessageBox("Put the cursor on a file or folder in cloud storage (a cloud:// panel) first.", "Copy Share Link").ShowDialog(this);
                return;
            }
            string target = JoinChildPath(folder, entries[0].Name);
            try
            {
                string link = await CloudExtras.PublicLinkAsync(target);
                if (Clipboard != null) await Clipboard.SetTextAsync(link);
                await new MessageBox($"The link to {entries[0].Name} is on the clipboard:\n\n{link}\n\nAnyone with it can open the item.", "Copy Share Link").ShowDialog(this);
            }
            catch (Exception ex)
            {
                await new MessageBox("No link was made: " + ex.Message, "Copy Share Link").ShowDialog(this);
            }
        }

        private async Task MountRemoteAsync(string side, string remote)
        {
            try
            {
                string mountPoint = await CloudExtras.MountAsync(remote);
                await Task.Delay(800);   // let the mount appear before the panel looks at it
                NavigatePanelTo(side, mountPoint);
            }
            catch (Exception ex)
            {
                await new MessageBox($"{remote} wasn't mounted: {ex.Message}", "Mount as Drive").ShowDialog(this);
            }
        }

        private async Task UnmountAsync(string mountPoint)
        {
            foreach (var (side, path) in new[] { ("LP", LPpath.Text ?? ""), ("RP", RPpath.Text ?? "") })
                if (path.StartsWith(mountPoint, StringComparison.Ordinal))
                    NavigatePanelTo(side, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            try { await CloudExtras.UnmountAsync(mountPoint); }
            catch (Exception ex) { await new MessageBox("It wasn't unmounted: " + ex.Message, "Unmount").ShowDialog(this); }
        }

        /// <summary>The ☁ menu's mount entries: Mount as Drive for each remote, and Unmount for each mount.</summary>
        private async Task<List<object>> MountMenuItemsAsync(string side, IReadOnlyList<string> remotes)
        {
            var items = new List<object>();
            if (remotes.Count == 0) return items;
            var mount = new MenuItem { Header = "Mount as Drive" };
            var mountItems = new List<MenuItem>();
            foreach (var r in remotes)
            {
                string remote = r.TrimEnd(':');
                var mi = new MenuItem { Header = remote };
                mi.Click += async (_, _) => await MountRemoteAsync(side, remote);
                mountItems.Add(mi);
            }
            mount.ItemsSource = mountItems;
            items.Add(new Separator());
            items.Add(mount);
            List<(string Fs, string MountPoint)> mounts = new();
            try { mounts = await CloudExtras.ListMountsAsync(); } catch { }
            foreach (var (fs, mp) in mounts)
            {
                var un = new MenuItem { Header = $"Unmount {fs} ({mp})" };
                un.Click += async (_, _) => await UnmountAsync(mp);
                items.Add(un);
            }
            return items;
        }

        // ---- Screen readers ----

        /// <summary>Names for the buttons that show only a symbol, so a screen reader can say what they do.</summary>
        private void InitAccessibility()
        {
            void Name(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);
            foreach (var (side, back, fwd, recent, cloud, drive, tab, view, tree, flat, find) in new[]
            {
                ("Left", LpBackButton, LpForwardButton, LpRecentButton, LpCloudButton, LpDriveButton, LPnewTab, LPviewToggle, LPtreeToggle, (Control)LPflatToggle, LPfindButton),
                ("Right", RpBackButton, RpForwardButton, RpRecentButton, RpCloudButton, RpDriveButton, RPnewTab, RPviewToggle, RPtreeToggle, (Control)RPflatToggle, RPfindButton),
            })
            {
                Name(back, $"{side} panel: back");
                Name(fwd, $"{side} panel: forward");
                Name(recent, $"{side} panel: recent folders");
                Name(cloud, $"{side} panel: cloud remotes and servers");
                Name(drive, $"{side} panel: drives");
                Name(tab, $"{side} panel: new tab");
                Name(view, $"{side} panel: list or thumbnails");
                Name(tree, $"{side} panel: folder tree");
                Name(flat, $"{side} panel: flat view");
                Name(find, $"{side} panel: find files");
            }
            UpdateAccessibleNames();
        }

        /// <summary>Each panel's name for screen readers: which side, and the folder it shows.</summary>
        private void UpdateAccessibleNames()
        {
            LPgrid.AccessibleName = "Left panel, " + (LPpath.Text ?? "");
            RPgrid.AccessibleName = "Right panel, " + (RPpath.Text ?? "");
        }

        // ---- Settings ----

        private void OpenSettings() => new SettingsWindow(() => Close()).ShowDialog(this);
    }
}
