using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Application-wide queue of background file operations. Operations run one at a time on a
/// background worker (concurrency = 1, to avoid disk/network thrash and keep progress legible),
/// in the order they were enqueued. Enqueuing never blocks the UI thread.
/// <para>
/// <b>Pause:</b> a paused operation that hasn't started is passed over, so the ones behind it
/// still run. A paused running operation stops at its next progress report and holds the queue
/// until it is resumed or cancelled (one at a time, so nothing can overtake it mid-way).
/// </para>
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
    private readonly List<FileOperation> _pending = new();      // not-yet-started, FIFO
    private bool _workerRunning;
    private DateTime? _holdUntil;

    /// <summary>
    /// When set, operations that haven't started wait until this time (Operations window → Start
    /// Later); one already running carries on. Null when the queue isn't held.
    /// </summary>
    public DateTime? HoldUntil
    {
        get { lock (_gate) return _holdUntil; }
    }

    /// <summary>Holds the queue until <paramref name="when"/>, or releases it when null.</summary>
    public void SetHold(DateTime? when)
    {
        lock (_gate)
        {
            _holdUntil = when;
            Monitor.PulseAll(_gate);
        }
        HoldChanged?.Invoke();
    }

    /// <summary>Raised when the hold is set, released, or runs out (on any thread).</summary>
    public event Action? HoldChanged;

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
            _pending.Add(op);
            Monitor.PulseAll(_gate);
        }

        OperationAdded?.Invoke(op);
        EnsureWorker();
    }

    /// <summary>Cancels one operation, whether it is queued (skipped when reached), running, or paused.</summary>
    public void Cancel(FileOperation op)
    {
        if (op is null) return;
        op.Cts.Cancel();
        Wake();
    }

    /// <summary>Cancels every operation that has not yet finished.</summary>
    public void CancelAll()
    {
        FileOperation[] active;
        lock (_gate) active = _operations.Where(o => !o.IsTerminal).ToArray();
        foreach (var op in active) op.Cts.Cancel();
        Wake();
    }

    /// <summary>Pauses one operation (see the class notes for what that means while it runs).</summary>
    public void Pause(FileOperation op)
    {
        if (op is null || !op.Pause()) return;
        OperationUpdated?.Invoke(op);
    }

    /// <summary>Resumes a paused operation.</summary>
    public void Resume(FileOperation op)
    {
        if (op is null || !op.Resume()) return;
        Wake();
        OperationUpdated?.Invoke(op);
    }

    /// <summary>Pauses every operation that hasn't finished.</summary>
    public void PauseAll()
    {
        FileOperation[] active;
        lock (_gate) active = _operations.Where(o => !o.IsTerminal).ToArray();
        foreach (var op in active) Pause(op);
    }

    /// <summary>Resumes every paused operation.</summary>
    public void ResumeAll()
    {
        FileOperation[] paused;
        lock (_gate) paused = _operations.Where(o => o.IsPaused).ToArray();
        foreach (var op in paused) Resume(op);
    }

    /// <summary>Lets a worker waiting on paused operations look again.</summary>
    private void Wake()
    {
        lock (_gate) Monitor.PulseAll(_gate);
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

    /// <summary>
    /// Removes the given operations from the tracked list, if they've finished (running or queued
    /// ones are left alone). Returns the ones removed.
    /// </summary>
    public IReadOnlyList<FileOperation> Remove(IEnumerable<FileOperation> ops)
    {
        var wanted = new HashSet<FileOperation>(ops);
        List<FileOperation> removed;
        lock (_gate)
        {
            removed = _operations.Where(o => o.IsTerminal && wanted.Contains(o)).ToList();
            _operations.RemoveAll(o => o.IsTerminal && wanted.Contains(o));
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
            FileOperation? op;
            bool holdEnded = false;
            lock (_gate)
            {
                while (true)
                {
                    if (_pending.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }
                    // Held until a time (Start Later): only cancelled operations are taken (to be
                    // marked finished) until it comes.
                    TimeSpan heldFor = TimeSpan.Zero;
                    if (_holdUntil is { } until)
                    {
                        heldFor = until - DateTime.Now;
                        if (heldFor <= TimeSpan.Zero) { _holdUntil = null; holdEnded = true; }
                    }
                    bool held = heldFor > TimeSpan.Zero;

                    // The oldest operation that may start: not paused, or cancelled (so it can be
                    // marked finished). If every waiting one is paused, sleep until something changes.
                    op = _pending.FirstOrDefault(o => o.Cts.IsCancellationRequested || (!held && !o.IsPaused));
                    if (op != null)
                    {
                        _pending.Remove(op);
                        break;
                    }
                    if (held) Monitor.Wait(_gate, heldFor < TimeSpan.FromMinutes(1) ? heldFor : TimeSpan.FromMinutes(1));
                    else Monitor.Wait(_gate);
                }
            }

            if (holdEnded) HoldChanged?.Invoke();
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
        op.MarkStarted();
        OperationUpdated?.Invoke(op);

        // SyncProgress forwards on the reporting thread; the UI subscriber does the final hop.
        // Every report is also where a pause takes hold: the work stops here until it's resumed,
        // and a cancel while paused ends it. Speed is measured over active time only.
        var meter = new SpeedMeter();
        var progress = new SyncProgress<TransferProgress>(tp =>
        {
            op.WaitWhilePaused(op.Cts.Token);
            if (tp.Resumed) meter.Restart();   // bytes kept from an earlier try weren't copied just now
            meter.Add(op.ActiveTime.TotalSeconds, tp.BytesDone);
            op.Progress = tp with { BytesPerSecond = meter.BytesPerSecond ?? tp.BytesPerSecond };
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
        op.MarkStopped();
        op.SignalCompleted();
        OperationUpdated?.Invoke(op);
        OperationCompleted?.Invoke(op);

        try { op.AfterFinish?.Invoke(op); }
        catch (Exception ex) { CrashLog.Write("Operation follow-up", ex); }
    }
}
