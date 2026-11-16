using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Copy, cut and paste of files through the system clipboard (Ctrl/Cmd+C, X and V in a panel,
    /// or right-click). Files copied here paste in other file managers, and files copied there
    /// paste here, through the usual copy queue with its conflict questions and undo.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>What DORI last cut, so pasting those same files moves them even where the clipboard can't say "cut".</summary>
        private HashSet<string>? _cutPaths;

        private static StringComparer ClipPathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        private async void CopyFilesToClipboard(bool isLeft, bool cut)
        {
            try
            {
                var (folder, entries) = Picked(isLeft);
                if (string.IsNullOrEmpty(folder) || entries.Count == 0) return;
                if (ProviderRegistry.For(folder) is not LocalFileProvider)
                {
                    await new MessageBox("Only files on this computer can go on the clipboard: other programs can't open " +
                                         "files inside archives or in cloud storage. Copy them to the other panel instead.",
                                         cut ? "Cut" : "Copy").ShowDialog(this);
                    return;
                }
                var paths = entries.Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
                if (Clipboard == null) return;

                // One item with the text and the file managers' own formats, then one item per file
                // (Avalonia turns those into Explorer's and Finder's file lists, and text/uri-list on Linux).
                var transfer = new DataTransfer();
                var main = new DataTransferItem();
                main.SetText(string.Join(Environment.NewLine, paths));
                if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                {
                    // GNOME-style apps (Files, Nemo, Caja, Thunar) read this one, which also says cut or copy.
                    main.Set(DataFormat.CreateBytesPlatformFormat(ClipboardFiles.GnomeFormat),
                             Encoding.UTF8.GetBytes(ClipboardFiles.GnomeCopiedFiles(paths, cut)));
                    if (cut) main.Set(DataFormat.CreateBytesPlatformFormat(ClipboardFiles.KdeCutFormat), Encoding.UTF8.GetBytes("1"));
                }
                else if (OperatingSystem.IsWindows())
                    main.Set(DataFormat.CreateBytesPlatformFormat(ClipboardFiles.WindowsDropEffectFormat), BitConverter.GetBytes(cut ? 2 : 5));
                transfer.Add(main);
                var files = new List<IStorageItem>();
                foreach (var p in paths)
                {
                    var uri = new Uri(p);
                    IStorageItem? item = Directory.Exists(p)
                        ? await StorageProvider.TryGetFolderFromPathAsync(uri)
                        : await StorageProvider.TryGetFileFromPathAsync(uri);
                    if (item != null) files.Add(item);
                }
                // Not on Linux: Avalonia 11.3's X11 clipboard turns file items into text/uri-list through
                // a StreamWriter it never flushes, so the list loses up to its last 1,024 characters —
                // empty for a few files, and silently missing the last ones for many (a KDE app would
                // paste only some). Better that those apps see no files than a partial set; GNOME-style
                // file managers use the list above. (ClipboardUriListHelper.FileUriListToUtf8Bytes.)
                if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
                    foreach (var f in files) transfer.Add(DataTransferItem.CreateFile(f));
                await Clipboard.SetDataAsync(transfer);
                _cutPaths = cut ? new HashSet<string>(paths, ClipPathComparer) : null;
                ShowClipboardNote(isLeft, $"{(cut ? "Cut" : "Copied")} {Describe(paths)} to the clipboard" +
                                  (cut ? " — paste to move." : "."));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Copy to clipboard", ex);
                await new MessageBox("The files couldn't be put on the clipboard: " + ex.Message, cut ? "Cut" : "Copy").ShowDialog(this);
            }
        }

        private async void PasteFilesFromClipboard(bool isLeft)
        {
            try
            {
                string dest = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
                if (string.IsNullOrEmpty(dest) || Clipboard == null) return;
                var (paths, cut) = await ReadClipboardFilesAsync();
                paths = await Task.Run(() => paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList());
                if (paths.Count == 0)
                {
                    await new MessageBox("There are no files on the clipboard to paste. Copy some first, here or in another file manager.",
                                         "Paste").ShowDialog(this);
                    return;
                }
                bool move = cut == true || (_cutPaths != null && paths.All(_cutPaths.Contains));

                await TransferOutsidePathsAsync(paths, dest, move);

                if (move)
                {
                    // Like other file managers: cut files paste once.
                    _cutPaths = null;
                    await Clipboard.ClearAsync();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("Paste from clipboard", ex);
                await new MessageBox("The files couldn't be pasted: " + ex.Message, "Paste").ShowDialog(this);
            }
        }

        // Reading uses the format-name API, which Avalonia 11.3 marks obsolete in favour of
        // IAsyncDataTransfer. It still works on every platform and reads other apps' raw formats
        // (x-special/gnome-copied-files, Preferred DropEffect) by name, so it stays for now.
#pragma warning disable CS0618
        /// <summary>
        /// Copies (or moves) files named by full path — from the clipboard, or dropped from another
        /// program — into <paramref name="dest"/>, one transfer per source folder, through the normal
        /// copy path with its conflict questions and undo.
        /// </summary>
        private async Task TransferOutsidePathsAsync(List<string> paths, string dest, bool move)
        {
            foreach (var group in paths.GroupBy(p => Path.GetDirectoryName(p.TrimEnd('/', '\\')) ?? "", ClipPathComparer))
            {
                if (group.Key.Length == 0) continue;
                if (move && PathsEqual(group.Key, dest)) continue;   // moving into the same folder does nothing
                var rows = group.Select(p => (object)new AFileEntry(Path.GetFileName(p.TrimEnd('/', '\\')), 0, Directory.Exists(p))).ToList();
                await RunPanelTransferAsync(rows, group.Key, dest, move);
            }
        }

        // ---- Drag and drop with other programs ----

        private void InitOutsideDragDrop()
        {
            foreach (var grid in new[] { LPgrid, RPgrid })
            {
                var g = grid;
                g.OutsideFilesDropped += async (_, e) =>
                {
                    string folder = (ReferenceEquals(g, LPgrid) ? LPpath.Text : RPpath.Text) ?? "";
                    if (string.IsNullOrEmpty(folder)) return;
                    string dest = e.TargetSubfolder.Length > 0 ? FileUtility.JoinPanelPath(folder, e.TargetSubfolder) : folder;
                    var paths = await Task.Run(() => e.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList());
                    if (paths.Count > 0) await TransferOutsidePathsAsync(paths, dest, e.Move);
                };
                g.OutsideDragItems = async entries =>
                {
                    var items = new List<IStorageItem>();
                    string folder = (ReferenceEquals(g, LPgrid) ? LPpath.Text : RPpath.Text) ?? "";
                    if (ProviderRegistry.For(folder) is not LocalFileProvider) return items;
                    foreach (var en in entries)
                    {
                        string p = FileUtility.JoinPanelPath(folder, en.Name);
                        var uri = new Uri(p);
                        IStorageItem? item = en.Typ ? await StorageProvider.TryGetFolderFromPathAsync(uri)
                                                    : await StorageProvider.TryGetFileFromPathAsync(uri);
                        if (item != null) items.Add(item);
                    }
                    return items;
                };
            }
        }

        /// <summary>The paths on the clipboard and whether they were cut (null when the clipboard doesn't say).</summary>
        private async Task<(List<string> Paths, bool? Cut)> ReadClipboardFilesAsync()
        {
            var clip = Clipboard!;
            var formats = await clip.GetFormatsAsync() ?? Array.Empty<string>();
            bool Has(string f) => formats.Contains(f, StringComparer.OrdinalIgnoreCase);

            if (Has(ClipboardFiles.GnomeFormat))
            {
                var paths = ClipboardFiles.Parse(ClipboardFiles.AsText(await clip.GetDataAsync(ClipboardFiles.GnomeFormat)), out bool? cut);
                if (paths.Count > 0) return (paths, cut);
            }
            if (Has(DataFormats.Files) && await clip.GetDataAsync(DataFormats.Files) is { } files)
            {
                // Storage items from the system, or (DORI's own copy on Linux) the URI list as bytes.
                var paths = files is IEnumerable<IStorageItem> items
                    ? items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList()
                    : ClipboardFiles.Parse(ClipboardFiles.AsText(files), out _);
                bool? cut = Has(ClipboardFiles.WindowsDropEffectFormat)
                    ? ClipboardFiles.DropEffectIsCut(await clip.GetDataAsync(ClipboardFiles.WindowsDropEffectFormat))
                    : null;
                if (paths.Count > 0) return (paths, cut);
            }
            if (Has(ClipboardFiles.UriListFormat))
            {
                var paths = ClipboardFiles.Parse(ClipboardFiles.AsText(await clip.GetDataAsync(ClipboardFiles.UriListFormat)), out _);
                bool? cut = Has(ClipboardFiles.KdeCutFormat)
                    ? ClipboardFiles.AsText(await clip.GetDataAsync(ClipboardFiles.KdeCutFormat))?.Trim() == "1"
                    : null;
                if (paths.Count > 0) return (paths, cut);
            }
            // Plain text: full paths, one per line (what "Copy Full Path" puts there, too).
            var text = await clip.GetTextAsync();
            return (ClipboardFiles.Parse(text, out bool? textCut), textCut);
        }
#pragma warning restore CS0618

        private static string Describe(IReadOnlyCollection<string> paths)
            => paths.Count == 1 ? $"\"{Path.GetFileName(paths.First().TrimEnd('/', '\\'))}\"" : $"{paths.Count} items";

        /// <summary>Says briefly in the panel's status bar what went on the clipboard; the next status update replaces it.</summary>
        private void ShowClipboardNote(bool isLeft, string note)
        {
            if (isLeft) LeftStatusText.Text = note; else RightStatusText.Text = note;
        }
    }
}
