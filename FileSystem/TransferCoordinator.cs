using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Runs a batch of <see cref="TransferItem"/>s off the UI thread, rebasing each item's
/// per-file progress onto whole-batch totals before forwarding it to the UI. Honors
/// cancellation between and during items.
/// </summary>
public static class TransferCoordinator
{
    public static async Task RunAsync(
        IReadOnlyList<TransferItem> items,
        bool move,
        IProgress<TransferProgress>? progress,
        CancellationToken ct)
    {
        // Pre-scan sizes so the overall bar is meaningful (folders priced via recursive size).
        // Run off the UI thread — for cloud items this makes blocking network calls.
        var (sizes, totalBytes) = await Task.Run(() =>
        {
            var s = new long[items.Count];
            long total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                s[i] = SizeOf(items[i]);
                total += s[i];
            }
            return (s, total);
        }, ct).ConfigureAwait(false);

        long completedBytes = 0;
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = items[i];
            int index = i + 1;
            long baseBytes = completedBytes;
            string itemName = NameOf(item.Source);

            // Per-item progress is rebased onto the whole-batch totals before reaching the UI.
            var itemProgress = new SyncProgress<TransferProgress>(tp =>
                progress?.Report(new TransferProgress(
                    string.IsNullOrEmpty(tp.CurrentFile) ? itemName : tp.CurrentFile,
                    index,
                    items.Count,
                    baseBytes + tp.BytesDone,
                    totalBytes,
                    tp.BytesPerSecond)));

            // Show the item immediately, before any bytes move.
            itemProgress.Report(new TransferProgress(itemName, index, items.Count, 0, 0, 0));

            if (move)
            {
                if (item.IsDirectory)
                    await FileUtility.MoveDirectoryAsync(item.Source, item.TargetPath, itemProgress, ct).ConfigureAwait(false);
                else
                    await FileUtility.MoveFileAsync(item.Source, item.TargetFolder, itemProgress, ct).ConfigureAwait(false);
            }
            else
            {
                if (item.IsDirectory)
                    await FileUtility.CopyDirectoryToFolderAsync(item.Source, item.TargetPath, itemProgress, ct).ConfigureAwait(false);
                else
                    await FileUtility.CopyFileToFolderAsync(item.Source, item.TargetFolder, itemProgress, ct).ConfigureAwait(false);
            }

            // Advance by the pre-scanned size so the bar is monotonic and lands exactly at 100%.
            completedBytes += sizes[i];
            progress?.Report(new TransferProgress(itemName, index, items.Count, completedBytes, totalBytes, 0));
        }
    }

    private static long SizeOf(TransferItem item)
    {
        try
        {
            var p = ProviderRegistry.For(item.Source);
            return item.IsDirectory
                ? p.GetDirectorySize(item.Source, recursive: true)
                : p.Stat(item.Source)?.Size ?? 0;
        }
        catch { return 0; }
    }

    private static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}

/// <summary>
/// IProgress that invokes its callback synchronously on the reporting thread, so progress
/// chains (item → batch) compose without the thread hops <see cref="Progress{T}"/> introduces.
/// The final hop to the UI thread is done by the consumer (a <see cref="Progress{T}"/> in the window).
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _action;
    public SyncProgress(Action<T> action) => _action = action;
    public void Report(T value) => _action(value);
}
