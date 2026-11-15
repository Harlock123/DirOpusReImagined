using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>What kind of work an operation performs. Drives the label and icon in the UI.</summary>
public enum OperationKind
{
    Copy,
    Move,
    Delete
}

/// <summary>Lifecycle of a queued operation.</summary>
public enum OperationStatus
{
    /// <summary>Waiting in the queue for the worker to reach it.</summary>
    Queued,

    /// <summary>Currently being executed by the worker.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Completed,

    /// <summary>Stopped early because it (or the whole queue) was cancelled.</summary>
    Canceled,

    /// <summary>Threw an error; see <see cref="FileOperation.Error"/>.</summary>
    Failed
}

/// <summary>
/// One unit of background file work (a copy/move batch, or a delete) tracked by the
/// <see cref="OperationQueue"/>. The actual work is carried as a delegate so the queue stays
/// decoupled from the transfer engine and new operation kinds only need a new factory.
/// <para>
/// Mutable status/progress fields are written by the queue worker on a background thread and
/// read by the UI; the values themselves are immutable snapshots (<see cref="TransferProgress"/>
/// is a readonly struct) so a torn read is harmless. UI consumers must still marshal event
/// callbacks onto the UI thread — see <see cref="OperationQueue"/>.
/// </para>
/// </summary>
public sealed class FileOperation
{
    private static long _seq;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Process-unique, monotonically increasing id. Handy as a UI key and for logs.</summary>
    public long Id { get; } = Interlocked.Increment(ref _seq);

    public OperationKind Kind { get; }

    /// <summary>Human-readable one-line description, e.g. "Copying 12 items → D:\Backup".</summary>
    public string Title { get; }

    /// <summary>The items this operation acts on (copy/move batch). Empty for a bare delete list.</summary>
    public IReadOnlyList<TransferItem> Items { get; }

    /// <summary>Destination folder for copy/move (null for delete). Used only for display/refresh hints.</summary>
    public string? DestinationFolder { get; }

    /// <summary>The work to perform. Awaited by the queue worker on a background thread.</summary>
    internal Func<IProgress<TransferProgress>, CancellationToken, Task> ExecuteAsync { get; }

    /// <summary>Cancels just this operation (whether queued or running).</summary>
    public CancellationTokenSource Cts { get; } = new();

    public OperationStatus Status { get; internal set; } = OperationStatus.Queued;

    /// <summary>Latest progress snapshot reported while running. Default (all-zero) before it starts.</summary>
    public TransferProgress Progress { get; internal set; }

    /// <summary>Set when <see cref="Status"/> is <see cref="OperationStatus.Failed"/>.</summary>
    public Exception? Error { get; internal set; }

    /// <summary>Completes (never faults) when the operation reaches a terminal status.</summary>
    public Task Completion => _completion.Task;

    // ---- pause ----

    private readonly ManualResetEventSlim _running = new(initialState: true);   // reset = paused
    private readonly Stopwatch _activeClock = new();                             // running and not paused
    private bool _started;

    /// <summary>
    /// True while paused. A queued operation that is paused is passed over until it's resumed; a
    /// running one stops at its next progress report (within a file for streamed copies, before
    /// the next file otherwise) and holds its place.
    /// </summary>
    public bool IsPaused => !_running.IsSet;

    /// <summary>How long the operation has actually been working: running time minus paused time.</summary>
    public TimeSpan ActiveTime => _activeClock.Elapsed;

    /// <summary>Pauses the operation. Returns false if it has already finished.</summary>
    internal bool Pause()
    {
        if (IsTerminal) return false;
        _running.Reset();
        _activeClock.Stop();
        return true;
    }

    /// <summary>Resumes a paused operation. Returns false if it wasn't paused.</summary>
    internal bool Resume()
    {
        if (!IsPaused) return false;
        if (_started && !IsTerminal) _activeClock.Start();
        _running.Set();
        return true;
    }

    /// <summary>Blocks the calling worker thread while paused. Throws if cancelled meanwhile.</summary>
    internal void WaitWhilePaused(CancellationToken ct) => _running.Wait(ct);

    internal void MarkStarted()
    {
        _started = true;
        if (!IsPaused) _activeClock.Start();
    }

    /// <summary>Stops the clock when the operation finishes; a finished operation is never "paused".</summary>
    internal void MarkStopped()
    {
        _activeClock.Stop();
        _running.Set();
    }

    // ---- per-item progress, for retrying only what didn't finish ----

    private readonly HashSet<int> _doneItems = new();

    /// <summary>Records that work item <paramref name="index"/> (in the factory's item list) finished.</summary>
    internal void MarkItemDone(int index)
    {
        lock (_doneItems) _doneItems.Add(index);
    }

    internal bool IsItemDone(int index)
    {
        lock (_doneItems) return _doneItems.Contains(index);
    }

    /// <summary>
    /// Builds the operation that does what this one left undone, or null if nothing is left. Set by
    /// the factories that track their items; when null, a retry repeats the whole operation.
    /// </summary>
    private Func<FileOperation, FileOperation?>? _remainingWork;

    /// <summary>The operation this one retries, if it is a retry.</summary>
    public FileOperation? RetryOf { get; private set; }

    /// <summary>
    /// Called on the worker thread once the operation finishes, whatever the outcome. A retry
    /// carries it over, so follow-up work (refreshing the panels, recording a move for undo) also
    /// happens when a retried operation finishes. UI work must be posted to the UI thread.
    /// </summary>
    public Action<FileOperation>? AfterFinish { get; set; }

    /// <summary>True when a retry would do something: the operation failed or was cancelled.</summary>
    public bool CanRetry => Status is OperationStatus.Failed or OperationStatus.Canceled;

    /// <summary>True once the operation has finished, one way or another.</summary>
    public bool IsTerminal =>
        Status is OperationStatus.Completed or OperationStatus.Canceled or OperationStatus.Failed;

    public FileOperation(
        OperationKind kind,
        IReadOnlyList<TransferItem> items,
        string title,
        Func<IProgress<TransferProgress>, CancellationToken, Task> executeAsync,
        string? destinationFolder = null)
    {
        Kind = kind;
        Items = items ?? Array.Empty<TransferItem>();
        Title = title;
        ExecuteAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        DestinationFolder = destinationFolder;
    }

    /// <summary>
    /// Builds a copy or move operation that runs the standard <see cref="TransferCoordinator"/>
    /// batch pipeline (pre-scan sizing, per-item byte progress, cancellation).
    /// </summary>
    public static FileOperation Transfer(
        bool move,
        IReadOnlyList<TransferItem> items,
        string title,
        string? destinationFolder = null)
    {
        FileOperation op = null!;
        op = new FileOperation(
            move ? OperationKind.Move : OperationKind.Copy,
            items,
            title,
            (progress, ct) => TransferCoordinator.RunAsync(items, move, progress, ct, i => op.MarkItemDone(i)),
            destinationFolder);
        op._remainingWork = src =>
        {
            var left = Remaining(src, items);
            return left.Count == 0 ? null : Transfer(move, left, RetryTitle(src.Title, left.Count, items.Count), destinationFolder);
        };
        return op;
    }

    private static List<T> Remaining<T>(FileOperation op, IReadOnlyList<T> items)
        => items.Where((_, i) => !op.IsItemDone(i)).ToList();

    private static string RetryTitle(string title, int left, int total)
    {
        // Strip an earlier retry note so retrying a retry doesn't stack them up.
        int note = title.IndexOf(" (retrying ", StringComparison.Ordinal);
        if (note >= 0) title = title[..note];
        return left == total ? title : $"{title} (retrying {left} of {total})";
    }

    /// <summary>
    /// Builds a delete operation over a set of (path, isDirectory) targets. Each item is deleted off
    /// the UI thread via <see cref="FileUtility"/> (to the OS trash when <paramref name="useTrash"/>
    /// is set and supported, otherwise permanently). Progress is reported per item; if any items fail,
    /// the operation ends Failed with a summary of the failures (the ones that succeeded are gone).
    /// </summary>
    public static FileOperation DeleteBatch(
        IReadOnlyList<(string Path, bool IsDir)> targets,
        bool useTrash,
        string title)
    {
        var list = targets ?? Array.Empty<(string, bool)>();
        FileOperation op = null!;
        op = new FileOperation(
            OperationKind.Delete,
            Array.Empty<TransferItem>(),
            title,
            (progress, ct) => Task.Run(() =>
            {
                var failures = new List<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (path, isDir) = list[i];
                    string name = Path.GetFileName(path.TrimEnd('/', '\\'));

                    progress.Report(new TransferProgress(
                        $"Deleting {name}  ({i + 1} of {list.Count})", i + 1, list.Count, 0, 0, 0));

                    string? err = isDir
                        ? FileUtility.TryDeleteFolder(path, useTrash)
                        : FileUtility.TryDeleteFile(path, useTrash);
                    if (err != null) failures.Add($"{name}: {err}");
                    else op.MarkItemDone(i);
                }

                if (failures.Count > 0)
                {
                    const int maxShown = 4;
                    string detail = string.Join("; ", failures.Take(maxShown));
                    if (failures.Count > maxShown) detail += $"; …and {failures.Count - maxShown} more";
                    throw new IOException(
                        $"{failures.Count} of {list.Count} item(s) could not be deleted: {detail}");
                }
            }, ct));
        op._remainingWork = src =>
        {
            var left = Remaining(src, list);
            return left.Count == 0 ? null : DeleteBatch(left, useTrash, RetryTitle(src.Title, left.Count, list.Count));
        };
        return op;
    }

    /// <summary>
    /// Builds a fresh operation that finishes the work of a failed or cancelled one: only the items
    /// that didn't complete, so a partly done move doesn't fail on items that already moved and a
    /// copy doesn't copy everything again. (The original's spent <see cref="Cts"/> and completion
    /// can't be reused.) Operations that don't track their items are repeated in full. Returns null
    /// when nothing is left to do. <see cref="AfterFinish"/> is carried over.
    /// </summary>
    public static FileOperation? Retry(FileOperation source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var again = source._remainingWork != null
            ? source._remainingWork(source)
            : new FileOperation(source.Kind, source.Items, source.Title, source.ExecuteAsync, source.DestinationFolder);
        if (again == null) return null;

        again.RetryOf = source;
        again.AfterFinish = source.AfterFinish;
        return again;
    }

    /// <summary>Marks the operation terminal and releases anyone awaiting <see cref="Completion"/>.</summary>
    internal void SignalCompleted() => _completion.TrySetResult();
}
