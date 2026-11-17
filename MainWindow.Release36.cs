using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Find and replace in files, the trash, stepping through files and the slideshow, the hex
    /// editor, converting audio and video, and keeping labels on the files themselves.
    /// </summary>
    public partial class MainWindow
    {
        // ---- Find and Replace in Files ----

        private void OpenReplaceInFiles(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Find and Replace")) return;
            var grid = isLeft ? LPgrid : RPgrid;
            // With nothing selected, the whole folder is searched rather than the one row under the cursor.
            bool selected = grid.SelectedItems.OfType<AFileEntry>().Any(e => e.Name != "..");
            var picked = selected ? entries.Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList() : new List<string> { folder };
            new ReplaceWindow(picked, folder, (f, changed) =>
            {
                PushUndo(new ReplaceUndo(f, changed, DateTime.Now));
                foreach (var dir in changed.Select(c => Path.GetDirectoryName(c.Path) ?? f).Append(f).Distinct())
                    RefreshPanelsShowing(dir);
                return Task.CompletedTask;
            }).Show(this);
        }

        private async Task UndoReplaceAsync(ReplaceUndo undo)
        {
            string what = undo.Items.Count == 1 ? $"\"{Path.GetFileName(undo.Items[0].Path)}\"" : $"{undo.Items.Count} files";
            var confirm = new MessageBox($"Put back {what} as they were before the replace?", showCancel: true,
                                         okText: "Put back", cancelText: "Cancel", title: "Undo replace");
            if (!await confirm.ShowDialog<bool>(this)) return;
            _undo.Pop();
            var problems = new List<string>();
            await Task.Run(() =>
            {
                foreach (var item in undo.Items)
                {
                    try
                    {
                        if (!File.Exists(item.Backup)) { problems.Add($"{Path.GetFileName(item.Path)}: its saved copy is gone"); continue; }
                        if (!ReplaceInFiles.Unchanged(item)) { problems.Add($"{Path.GetFileName(item.Path)}: it has changed since the replace"); continue; }
                        string temp = Path.Combine(Path.GetDirectoryName(item.Path)!, "." + Path.GetFileName(item.Path) + ".dori-replace");
                        File.Copy(item.Backup, temp, overwrite: true);
                        try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, File.GetUnixFileMode(item.Path)); } catch { }
                        File.Move(temp, item.Path, overwrite: true);
                        File.Delete(item.Backup);
                    }
                    catch (Exception ex) { problems.Add($"{Path.GetFileName(item.Path)}: {ex.Message}"); }
                }
            });
            foreach (var dir in undo.Items.Select(i => Path.GetDirectoryName(i.Path) ?? undo.Folder).Append(undo.Folder).Distinct())
                RefreshPanelsShowing(dir);
            await ReportUndoProblems("put back", problems);
        }

        // ---- Trash ----

        private void OpenTrash(bool isLeft)
        {
            string side = isLeft ? "LP" : "RP";
            new TrashWindow(folder => RefreshPanelsShowing(folder), folder => NavigatePanelTo(side, folder)).Show(this);
        }

        // ---- Stepping through files, the slideshow and the hex editor ----

        /// <summary>The files a panel showing <paramref name="panelPath"/> lists, in its order, as full paths.</summary>
        private List<string> PanelFiles(string panelPath)
        {
            TaiDataGrid? grid = PathsEqual(LPpath.Text ?? "", panelPath) ? LPgrid
                              : PathsEqual(RPpath.Text ?? "", panelPath) ? RPgrid : null;
            if (grid == null) return new List<string>();
            return grid.Items.OfType<AFileEntry>().Where(e => !e.Typ && e.Name != ".." && !string.IsNullOrEmpty(e.Name))
                       .Select(e => JoinChildPath(panelPath, e.Name)).ToList();
        }

        private void WireViewer(FileViewer viewer, string panelPath)
        {
            viewer.IsPicture = FileExtensionIsImage;
            viewer.Siblings = PanelFiles(panelPath);
            viewer.SlideshowRequested += (v, path) => StartSlideshow(v.Siblings, path, v);
            viewer.HexEditorRequested += (_, path) => OpenHexEditor(path, panelPath);
        }

        private void StartSlideshowFromPanel(bool isLeft)
        {
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            var grid = isLeft ? LPgrid : RPgrid;
            string? start = grid.CursorItem is AFileEntry { Typ: false } e ? JoinChildPath(folder, e.Name) : null;
            StartSlideshow(PanelFiles(folder), start, this);
        }

        private void StartSlideshow(IReadOnlyList<string> files, string? start, Window owner)
        {
            var pictures = files.Where(p => FileExtensionIsImage(FileViewer.LeafName(p))).ToList();
            if (pictures.Count == 0)
            {
                _ = new MessageBox("There are no pictures in this folder to show.", "Slideshow").ShowDialog(owner);
                return;
            }
            int index = start == null ? 0 : Math.Max(0, pictures.FindIndex(p => PathsEqual(p, start)));
            new SlideshowWindow(pictures, index, TrashFromSlideshowAsync, LabelFromSlideshow).Show(owner);
        }

        /// <summary>The slideshow's Delete: to the trash (local pictures only), undoable from a panel.</summary>
        private async Task<bool> TrashFromSlideshowAsync(string path)
        {
            if (ProviderRegistry.For(path) is not LocalFileProvider || !TrashService.IsSupported(path)) return false;
            string folder = Path.GetDirectoryName(path) ?? "";
            try
            {
                var item = await Task.Run(() => TrashService.Trash(path));
                if (item != null && UndoPlanner.RecordTrash(folder, new[] { item }, DateTime.Now) is { } undo) PushUndo(undo);
                RefreshPanelsShowing(folder);
                return true;
            }
            catch (Exception ex)
            {
                CrashLog.Write("Slideshow trash", ex);
                return false;
            }
        }

        private static void LabelFromSlideshow(string path, LabelColour colour)
        {
            if (ProviderRegistry.For(path) is LocalFileProvider) LabelStore.Current.SetColour(new[] { path }, colour);
        }

        private void OpenHexEditorForPanel(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            AFileEntry? target = grid.CursorItem as AFileEntry;
            if (target == null || target.Typ) target = grid.GetListOfSelectedFiles().FirstOrDefault();
            if (target == null || target.Typ || string.IsNullOrEmpty(folder))
            {
                _ = new MessageBox("Put the cursor on a file first.", "Hex Editor").ShowDialog(this);
                return;
            }
            OpenHexEditor(JoinChildPath(folder, target.Name), folder);
        }

        private void OpenHexEditor(string path, string folder)
        {
            var editor = new HexEditorWindow(path, FileViewer.LeafName(path));
            editor.Saved += _ => RefreshPanelsShowing(folder);
            editor.Show(this);
        }

        // ---- Convert audio and video ----

        private void OpenMediaTools(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Convert Audio and Video")) return;
            if (!VideoFrames.Available)
            {
                _ = new MessageBox("Converting audio and video needs ffmpeg, which isn't installed.\n\n" +
                                   "Linux: install the ffmpeg package (for example sudo apt install ffmpeg).\n" +
                                   "macOS: brew install ffmpeg.\nWindows: winget install ffmpeg.\n\nThen start DORI again.",
                                   "Convert Audio and Video").ShowDialog(this);
                return;
            }
            var files = entries.Where(e => !e.Typ && MediaConvert.IsMediaFile(e.Name))
                               .Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
            if (files.Count == 0)
            {
                _ = new MessageBox("Select the audio or video files first.", "Convert Audio and Video").ShowDialog(this);
                return;
            }
            new MediaToolsWindow(files, folder, OtherLocalFolder(isLeft), QueueMediaJobs).ShowDialog(this);
        }

        private void QueueMediaJobs(List<MediaJob> jobs)
        {
            foreach (var job in jobs)
            {
                string target = Path.GetDirectoryName(job.Target)!;
                long size = 1;
                try { size = Math.Max(1, new FileInfo(job.Source).Length); } catch { }
                RunFileTool($"Converting {Path.GetFileName(job.Source)} → {Path.GetFileName(job.Target)}", job.Source, target, (p, ct) =>
                {
                    // ffmpeg reports time done; it is shown against the source's size so the queue's bar and speed read sensibly.
                    var scaled = new SyncProgress<(long Done, long Total)>(x =>
                        p.Report(((long)(size * (x.Total > 0 ? (double)x.Done / x.Total : 0)), size)));
                    MediaConvert.Run(job, scaled, ct);
                });
            }
        }

        // ---- Labels kept on the files ----

        private async void ToggleSystemTags()
        {
            if (!SystemTags.Supported)
            {
                await new MessageBox("Windows has no tags that every kind of file can carry, so labels stay in DORI there.", "Labels").ShowDialog(this);
                return;
            }
            bool on = !SystemTags.Enabled;
            SystemTags.Enabled = on;
            ApplySystemTagsSetting();
            ApplyLabels(LPgrid, LPpath.Text);
            ApplyLabels(RPgrid, RPpath.Text);
            if (!on)
            {
                await new MessageBox("Labels are now kept only in DORI. Tags already on files stay there, but DORI no longer shows or changes them.", "Labels").ShowDialog(this);
                return;
            }
            var store = LabelStore.Current;
            var paths = store.StoredPaths().Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (paths.Count == 0) return;
            bool copy = await new MessageBox(
                $"Labels you set from now on are kept on the files too, where {(OperatingSystem.IsMacOS() ? "Finder" : "Dolphin and other programs")} can see them.\n\n" +
                $"Copy the {paths.Count:N0} label(s) DORI already keeps onto their files now?", true, "Copy Them", "Not Now", "Labels").ShowDialog<bool>(this);
            if (!copy) return;
            int written = await Task.Run(() => paths.Count(p => SystemTags.Write(p, store.GetStored(p))));
            await new MessageBox($"{written:N0} of {paths.Count:N0} label(s) were copied onto their files." +
                                 (written < paths.Count ? " The rest are on drives that don't keep tags (such as FAT USB sticks); DORI still has them." : ""),
                                 "Labels").ShowDialog(this);
        }
    }
}
