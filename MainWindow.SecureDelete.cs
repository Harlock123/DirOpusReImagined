using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Securely Delete (right-click → File Tools), run in the operations queue. See <see cref="SecureEraser"/>.</summary>
    public partial class MainWindow
    {
        private async void SecureDeletePicked(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder) || entries.Count == 0) return;
            if (!RequireLocal(folder, "Securely Delete")) return;

            var paths = entries.Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
            long bytes = await Task.Run(() => SecureEraser.BytesIn(paths));
            string what = paths.Count == 1 ? $"\"{entries[0].Name}\"" : $"these {paths.Count} items";
            bool sure = await new MessageBox(
                $"Overwrite {what} ({ProgressFormat.Bytes(bytes)}) with random data, then delete {(paths.Count == 1 ? "it" : "them")}?\n\n" +
                "This can't be undone, and nothing goes to the trash. Folders are emptied the same way; " +
                "links are removed without touching what they point to.\n\n" +
                "It can't be relied on for SSDs and flash drives, copy-on-write file systems (Btrfs, ZFS, APFS), " +
                "or files that snapshots, backups or cloud sync also hold: those keep copies DORI can't reach.",
                true, "Securely Delete", "Cancel", "Securely Delete").ShowDialog<bool>(this);
            if (!sure) return;

            string title = $"Securely deleting {(paths.Count == 1 ? entries[0].Name : $"{paths.Count} items")}";
            var items = paths.Select((p, i) => new TransferItem(p, folder, p, entries[i].Typ)).ToList();
            var op = new FileOperation(OperationKind.Delete, items, title, (progress, ct) => Task.Run(() =>
            {
                var bytesProgress = new SyncProgress<(long Done, long Total)>(x =>
                    progress.Report(new TransferProgress(title, 1, 1, x.Done, x.Total, 0)));
                SecureEraser.Erase(paths, bytesProgress, ct);
            }, ct), folder);
            op.AfterFinish = done => Dispatcher.UIThread.Post(() =>
            {
                foreach (var p in paths)
                    if (!System.IO.File.Exists(p) && !System.IO.Directory.Exists(p)) LabelStore.Current.Forget(p);
                RefreshPanelsShowing(folder);
                if (done.Status == OperationStatus.Failed) OperationsWindow.ShowSingleton(this);
            });
            OperationQueue.Instance.Enqueue(op);
            OperationsWindow.ShowSingleton(this);
        }
    }
}
