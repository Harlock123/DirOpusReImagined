using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Application-wide queue of background file operations. Operations run one at a time on a
/// background worker (concurrency = 1, to avoid disk/network thrash and keep progress legible),
/// in the order they were enqueued. Enqueuing never blocks the UI thread.
/// <para>
/// <b>Threading:</b> the <see cref="OperationAdded"/>, <see cref="OperationUpdated"/> and
/// <see cref="OperationCompleted"/> events are raised on the worker/thread-pool thread, NOT the
/// UI thread. Subscribers that touch UI must marshal with <c>Dispatcher.UIThread.Post</c>.
/// </para>
/// </summary>
public sealed class OperationQueue
{
    /// <summary>The single app-wide queue.</summary>
    public static OperationQueue Instance { get; } = new();

    private readonly object _gate = new();
    private readonly List<FileOperation> _operations = new();   // everything, for the UI (newest kept until cleared)
    private readonly Queue<FileOperation> _pending = new();     // not-yet-started, FIFO
    private bool _workerRunning;

    /// <summary>Raised when an operation is enqueued.</summary>
    public event Action<FileOperation>? OperationAdded;

    /// <summary>Raised on every status change and every progress tick.</summary>
    public event Action<FileOperation>? OperationUpdated;

    /// <summary>Raised once when an operation reaches a terminal status (completed/canceled/failed).</summary>
    public event Action<FileOperation>? OperationCompleted;

    /// <summary>Snapshot of all tracked operations (queued, running, and finished-but-not-cleared).</summary>
    public IReadOnlyList<FileOperation> Operations
    {
        get { lock (_gate) return _operations.ToList(); }
    }

    /// <summary>True while any operation is queued or running.</summary>
    public bool HasActiveWork
    {
        get { lock (_gate) return _operations.Any(o => !o.IsTerminal); }
    }

    /// <summary>Adds an operation to the tail of the queue and starts the worker if idle.</summary>
    public void Enqueue(FileOperation op)
    {
        if (op is null) throw new ArgumentNullException(nameof(op));

        lock (_gate)
        {
            _operations.Add(op);
            _pending.Enqueue(op);
        }

        OperationAdded?.Invoke(op);
        EnsureWorker();
    }

    /// <summary>Cancels one operation, whether it is queued (skipped when reached) or running.</summary>
    public void Cancel(FileOperation op) => op?.Cts.Cancel();

    /// <summary>Cancels every operation that has not yet finished.</summary>
    public void CancelAll()
    {
        FileOperation[] active;
        lock (_gate) active = _operations.Where(o => !o.IsTerminal).ToArray();
        foreach (var op in active) op.Cts.Cancel();
    }

    /// <summary>Removes finished operations from the tracked list. Returns the ones removed.</summary>
    public IReadOnlyList<FileOperation> ClearFinished()
    {
        List<FileOperation> removed;
        lock (_gate)
        {
            removed = _operations.Where(o => o.IsTerminal).ToList();
            _operations.RemoveAll(o => o.IsTerminal);
        }
        return removed;
    }

    private void EnsureWorker()
    {
        lock (_gate)
        {
            if (_workerRunning) return;
            _workerRunning = true;
        }
        _ = Task.Run(WorkerLoopAsync);
    }

    private async Task WorkerLoopAsync()
    {
        while (true)
        {
            FileOperation op;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _workerRunning = false;
                    return;
                }
                op = _pending.Dequeue();
            }

            await RunOneAsync(op).ConfigureAwait(false);
        }
    }

    private async Task RunOneAsync(FileOperation op)
    {
        // Cancelled while still waiting in the queue: skip execution entirely.
        if (op.Cts.IsCancellationRequested)
        {
            Finish(op, OperationStatus.Canceled);
            return;
        }

        op.Status = OperationStatus.Running;
        OperationUpdated?.Invoke(op);

        // SyncProgress forwards on the reporting thread; the UI subscriber does the final hop.
        var progress = new SyncProgress<TransferProgress>(tp =>
        {
            op.Progress = tp;
            OperationUpdated?.Invoke(op);
        });

        try
        {
            await op.ExecuteAsync(progress, op.Cts.Token).ConfigureAwait(false);
            Finish(op, OperationStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            Finish(op, OperationStatus.Canceled);
        }
        catch (Exception ex)
        {
            op.Error = ex;
            Finish(op, OperationStatus.Failed);
        }
    }

    private void Finish(FileOperation op, OperationStatus status)
    {
        op.Status = status;
        op.SignalCompleted();
        OperationUpdated?.Invoke(op);
        OperationCompleted?.Invoke(op);
    }
}
