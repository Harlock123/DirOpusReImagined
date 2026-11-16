using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Disk Usage, Image Tools, and splitting and joining files, opened from a panel.</summary>
    public partial class MainWindow
    {
        /// <summary>The other panel's folder when it is on this computer (not an archive or the cloud), else null.</summary>
        private string? OtherLocalFolder(bool isLeft)
        {
            string other = (isLeft ? RPpath.Text : LPpath.Text) ?? "";
            return other.Length > 0 && ProviderRegistry.For(other) is LocalFileProvider && Directory.Exists(other) ? other : null;
        }

        private bool RequireLocal(string folder, string what)
        {
            if (ProviderRegistry.For(folder) is LocalFileProvider) return true;
            _ = new MessageBox($"{what} works on folders on this computer, not in cloud storage or inside archives. " +
                               "Copy the files to a local folder first.", what).ShowDialog(this);
            return false;
        }

        // ---- Disk Usage ----

        private void OpenDiskUsage(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Disk Usage")) return;
            // A single selected folder is measured on its own; otherwise the whole panel folder.
            string root = entries.Count == 1 && entries[0].Typ ? FileUtility.JoinPanelPath(folder, entries[0].Name) : folder;
            if (isLeft ? LPgrid.FlatRoot != null : RPgrid.FlatRoot != null) root = folder;
            new DiskUsageWindow(this, root.Length > 1 ? root.TrimEnd('/', '\\') : root, isLeft).Show(this);
        }

        // ---- Image Tools ----

        private void OpenImageTools(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Image Tools")) return;
            var files = entries.Where(e => !e.Typ && ImageTools.CanRead(e.Name))
                               .Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
            if (files.Count == 0)
            {
                _ = new MessageBox("Select the pictures first (JPEG, PNG, WebP, GIF, BMP or ICO).", "Image Tools").ShowDialog(this);
                return;
            }
            new ImageToolsWindow(files, folder, OtherLocalFolder(isLeft), written => RefreshPanelsShowing(written)).ShowDialog(this);
        }

        // ---- Split and join ----

        private string _lastPieceSize = "1 GB";

        private async void SplitPickedFile(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Split File")) return;
            var files = entries.Where(e => !e.Typ).ToList();
            if (files.Count != 1)
            {
                await new MessageBox("Select one file to split.", "Split File").ShowDialog(this);
                return;
            }
            string source = FileUtility.JoinPanelPath(folder, files[0].Name);
            string target = OtherLocalFolder(isLeft) ?? Path.GetDirectoryName(source)!;
            long length = new FileInfo(source).Length;

            string? text = await new TextPromptDialog("Split File",
                $"Split {Path.GetFileName(source)} ({ProgressFormat.Bytes(length)}) into pieces of what size? " +
                "For example 100 MB, 4 GB or 650M.\n\n" +
                $"The pieces ({Path.GetFileName(source)}.001, .002…) and a .sha256 file to check them go into:\n{target}",
                _lastPieceSize).ShowDialog<string?>(this);
            if (text == null) return;
            if (!FileSplitter.TryParseSize(text, out long size))
            {
                await new MessageBox($"\"{text}\" isn't a size. Try 100 MB, 4 GB or 650M.", "Split File").ShowDialog(this);
                return;
            }
            _lastPieceSize = text;
            int count = FileSplitter.PieceCount(length, size);
            RunFileTool($"Splitting {Path.GetFileName(source)} into {count} pieces → {target}", source, target,
                (p, ct) => FileSplitter.Split(source, target, size, p, ct));
        }

        private async void JoinPickedPieces(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || !RequireLocal(folder, "Join Pieces")) return;
            var piece = entries.FirstOrDefault(e => !e.Typ && FileSplitter.JoinedName(Path.GetFileName(e.Name)) != null);
            if (piece == null)
            {
                await new MessageBox("Select a numbered piece (name.001, name.002…) to join it with the rest.", "Join Pieces").ShowDialog(this);
                return;
            }
            string any = FileUtility.JoinPanelPath(folder, piece.Name);
            string target = OtherLocalFolder(isLeft) ?? Path.GetDirectoryName(any)!;
            List<string> pieces;
            try { pieces = FileSplitter.FindPieces(any); }
            catch (IOException ex)
            {
                await new MessageBox(ex.Message, "Join Pieces").ShowDialog(this);
                return;
            }
            string name = FileSplitter.JoinedName(Path.GetFileName(any))!;
            RunFileTool($"Joining {pieces.Count} pieces into {name} → {target}", any, target, (p, ct) =>
            {
                var result = FileSplitter.Join(any, target, p, ct);
                if (result.Verified == null)
                    Dispatcher.UIThread.Post(() => _ = new MessageBox(
                        $"{name} was joined from {result.Pieces} pieces. There was no {name}.sha256 beside them, so it couldn't be checked.",
                        "Join Pieces").ShowDialog(this));
            });
        }

        /// <summary>Runs a split or join in the operations queue, with progress, pause and cancel, and refreshes the panels after.</summary>
        private void RunFileTool(string title, string source, string target, Action<IProgress<(long Done, long Total)>, System.Threading.CancellationToken> work)
        {
            string name = Path.GetFileName(source);
            var op = new FileOperation(OperationKind.Copy,
                new[] { new TransferItem(source, target, Path.Combine(target, name), false) }, title,
                (progress, ct) => Task.Run(() =>
                {
                    // Every tenth of a second is plenty (and the end, so the bar finishes full).
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    long last = -1000;
                    var bytes = new SyncProgress<(long Done, long Total)>(x =>
                    {
                        long now = clock.ElapsedMilliseconds;
                        if (now - last < 100 && x.Done < x.Total) return;
                        last = now;
                        progress.Report(new TransferProgress(name, 1, 1, x.Done, x.Total, 0));
                    });
                    work(bytes, ct);
                }, ct), target);
            op.AfterFinish = done => Dispatcher.UIThread.Post(() =>
            {
                RefreshPanelsShowing(target);
                if (done.Status == OperationStatus.Failed) OperationsWindow.ShowSingleton(this);
            });
            OperationQueue.Instance.Enqueue(op);
            OperationsWindow.ShowSingleton(this);
        }
    }
}
