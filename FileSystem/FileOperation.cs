using System;
using System.Collections.Generic;
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
        => new(
            move ? OperationKind.Move : OperationKind.Copy,
            items,
            title,
            (progress, ct) => TransferCoordinator.RunAsync(items, move, progress, ct),
            destinationFolder);

    /// <summary>Marks the operation terminal and releases anyone awaiting <see cref="Completion"/>.</summary>
    internal void SignalCompleted() => _completion.TrySetResult();
}
