using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class SpeedMeterTests
{
    [Fact]
    public void Has_no_speed_until_half_a_second_of_data()
    {
        var m = new SpeedMeter();
        Assert.Null(m.BytesPerSecond);
        m.Add(0, 0);
        m.Add(0.2, 1000);
        Assert.Null(m.BytesPerSecond);
        m.Add(1.0, 5000);
        Assert.Equal(5000, m.BytesPerSecond);
    }

    [Fact]
    public void Measures_over_the_recent_window_only()
    {
        var m = new SpeedMeter(windowSeconds: 5);
        // Slow at first (100 B/s for 10 s), then fast (1000 B/s for 10 s).
        long bytes = 0;
        for (int t = 0; t <= 10; t++) { m.Add(t, bytes); bytes += 100; }
        bytes -= 100;
        for (int t = 11; t <= 20; t++) { bytes += 1000; m.Add(t, bytes); }
        Assert.Equal(1000, m.BytesPerSecond!.Value, precision: 0);
    }

    [Fact]
    public void Paused_time_does_not_count_because_active_time_stands_still()
    {
        var m = new SpeedMeter();
        m.Add(0, 0);
        m.Add(2, 2000);
        // Paused for a long time: the active clock stays at 2 s, so reports during the pause
        // (or the first one after it) don't lower the speed.
        m.Add(2, 2000);
        m.Add(3, 3000);
        Assert.Equal(1000, m.BytesPerSecond);
    }

    [Fact]
    public void A_falling_byte_count_starts_a_fresh_window()
    {
        var m = new SpeedMeter();
        m.Add(0, 0);
        m.Add(2, 10_000);
        m.Add(3, 100);
        Assert.Null(m.BytesPerSecond);
    }
}

public class OperationQueueTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly TempDir _t = new();
    private readonly OperationQueue _q = new();

    public void Dispose()
    {
        _q.CancelAll();
        _t.Dispose();
    }

    private static async Task Done(FileOperation op)
    {
        var finished = await Task.WhenAny(op.Completion, Task.Delay(Timeout));
        Assert.True(finished == op.Completion, $"'{op.Title}' did not finish (status {op.Status})");
    }

    /// <summary>An operation that reports progress <paramref name="steps"/> times, a little apart.</summary>
    private static FileOperation Steps(string title, int steps, List<string>? log = null)
        => new(OperationKind.Copy, Array.Empty<TransferItem>(), title, async (progress, ct) =>
        {
            for (int i = 1; i <= steps; i++)
            {
                progress.Report(new TransferProgress("f", i, steps, i * 100, steps * 100, 0));
                await Task.Delay(10, ct);
            }
            lock (log ?? new List<string>()) log?.Add(title);
        });

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Operations_run_in_order()
    {
        var log = new List<string>();
        var a = Steps("a", 3, log);
        var b = Steps("b", 3, log);
        _q.Enqueue(a);
        _q.Enqueue(b);
        await Done(b);
        Assert.Equal(new[] { "a", "b" }, log);
        Assert.Equal(OperationStatus.Completed, a.Status);
    }

    [Fact]
    public async Task A_paused_waiting_operation_lets_the_ones_behind_it_go_first()
    {
        var log = new List<string>();
        var a = Steps("a", 3, log);
        var b = Steps("b", 3, log);
        _q.Pause(a);
        _q.Enqueue(a);
        _q.Enqueue(b);

        await Done(b);
        Assert.Equal(OperationStatus.Queued, a.Status);
        Assert.True(a.IsPaused);

        _q.Resume(a);
        await Done(a);
        Assert.Equal(new[] { "b", "a" }, log);
        Assert.False(a.IsPaused);
    }

    [Fact]
    public async Task A_running_operation_stops_while_paused_and_carries_on_when_resumed()
    {
        var op = Steps("long", 60);
        _q.Enqueue(op);
        await WaitUntil(() => op.Progress.FileIndex >= 5);

        _q.Pause(op);
        await Task.Delay(100);              // let the step in flight reach its report and stop
        int heldAt = op.Progress.FileIndex;
        var activeAtPause = op.ActiveTime;
        await Task.Delay(300);

        Assert.Equal(heldAt, op.Progress.FileIndex);
        Assert.Equal(OperationStatus.Running, op.Status);
        Assert.True(op.ActiveTime - activeAtPause < TimeSpan.FromMilliseconds(50), "active clock kept running while paused");

        _q.Resume(op);
        await Done(op);
        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal(60, op.Progress.FileIndex);
    }

    [Fact]
    public async Task Cancelling_a_paused_operation_ends_it()
    {
        var op = Steps("long", 60);
        _q.Enqueue(op);
        await WaitUntil(() => op.Progress.FileIndex >= 3);
        _q.Pause(op);
        await Task.Delay(50);

        _q.Cancel(op);
        await Done(op);
        Assert.Equal(OperationStatus.Canceled, op.Status);
        Assert.False(op.IsPaused);
    }

    [Fact]
    public async Task Pause_all_and_resume_all()
    {
        var a = Steps("a", 40);
        var b = Steps("b", 3);
        _q.Enqueue(a);
        _q.Enqueue(b);
        await WaitUntil(() => a.Progress.FileIndex >= 2);

        _q.PauseAll();
        Assert.True(a.IsPaused);
        Assert.True(b.IsPaused);
        await Task.Delay(200);
        Assert.Equal(OperationStatus.Queued, b.Status);   // a holds the queue while paused mid-run

        _q.ResumeAll();
        await Done(b);
        Assert.Equal(OperationStatus.Completed, a.Status);
        Assert.Equal(OperationStatus.Completed, b.Status);
    }

    [Fact]
    public async Task Pausing_a_finished_operation_does_nothing()
    {
        var op = Steps("quick", 1);
        _q.Enqueue(op);
        await Done(op);
        _q.Pause(op);
        Assert.False(op.IsPaused);
    }

    [Fact]
    public async Task AfterFinish_runs_once_the_operation_is_done()
    {
        var op = Steps("x", 2);
        var seen = new TaskCompletionSource<OperationStatus>();
        op.AfterFinish = o => seen.TrySetResult(o.Status);
        _q.Enqueue(op);
        Assert.Equal(OperationStatus.Completed, await seen.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Remove_drops_only_finished_operations()
    {
        var done = Steps("done", 1);
        _q.Enqueue(done);
        await Done(done);
        var waiting = Steps("waiting", 1);
        _q.Pause(waiting);
        _q.Enqueue(waiting);

        var removed = _q.Remove(new[] { done, waiting });
        Assert.Equal(new[] { done }, removed);
        Assert.Contains(waiting, _q.Operations);
        _q.Cancel(waiting);
    }

    // ---------------- retry ----------------

    private (string Src, string Dst) Folders()
    {
        var src = _t.Dir("src") + Path.DirectorySeparatorChar;
        var dst = _t.Dir("dst") + Path.DirectorySeparatorChar;
        return (src, dst);
    }

    private static List<TransferItem> Items(string src, string dst, params string[] names)
        => names.Select(n => new TransferItem(src + n, dst, dst + n, false)).ToList();

    [Fact]
    public async Task Retrying_a_failed_copy_runs_only_the_items_that_did_not_finish()
    {
        var (src, dst) = Folders();
        _t.File("src/1.txt", "one");
        _t.File("src/3.txt", "three");       // 2.txt is missing, so the copy fails on it
        var op = FileOperation.Transfer(false, Items(src, dst, "1.txt", "2.txt", "3.txt"), "Copying 3 items");
        int followUps = 0;
        op.AfterFinish = _ => Interlocked.Increment(ref followUps);
        _q.Enqueue(op);
        await Done(op);
        Assert.Equal(OperationStatus.Failed, op.Status);
        Assert.True(op.CanRetry);

        // Make the first file different, so a full re-copy would show up.
        File.WriteAllText(Path.Combine(dst, "1.txt"), "copied earlier");
        _t.File("src/2.txt", "two");

        var again = FileOperation.Retry(op)!;
        Assert.Same(op, again.RetryOf);
        Assert.Equal(new[] { src + "2.txt", src + "3.txt" }, again.Items.Select(i => i.Source));
        Assert.Contains("(retrying 2 of 3)", again.Title);

        _q.Enqueue(again);
        await Done(again);
        Assert.Equal(OperationStatus.Completed, again.Status);
        Assert.Equal("copied earlier", File.ReadAllText(Path.Combine(dst, "1.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(dst, "2.txt")));
        Assert.Equal("three", File.ReadAllText(Path.Combine(dst, "3.txt")));
        await WaitUntil(() => Volatile.Read(ref followUps) == 2);   // carried over to the retry
    }

    [Fact]
    public async Task Retrying_a_partly_done_move_finishes_it_instead_of_failing_on_moved_items()
    {
        var (src, dst) = Folders();
        _t.File("src/a.txt", "a");
        _t.File("src/c.txt", "c");
        var op = FileOperation.Transfer(true, Items(src, dst, "a.txt", "b.txt", "c.txt"), "Moving 3 items");
        _q.Enqueue(op);
        await Done(op);
        Assert.Equal(OperationStatus.Failed, op.Status);
        Assert.False(File.Exists(src + "a.txt"));   // a.txt moved before the failure

        _t.File("src/b.txt", "b");
        var again = FileOperation.Retry(op)!;
        _q.Enqueue(again);
        await Done(again);

        Assert.Equal(OperationStatus.Completed, again.Status);
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" },
            Directory.GetFiles(dst).Select(Path.GetFileName).OrderBy(n => n));
        Assert.Empty(Directory.GetFiles(src));
    }

    [Fact]
    public async Task Retrying_a_title_does_not_stack_retry_notes()
    {
        var (src, dst) = Folders();
        var op = FileOperation.Transfer(false, Items(src, dst, "x", "y", "z"), "Copying 3 items");
        _q.Enqueue(op);
        await Done(op);                              // fails on x: nothing exists
        var r1 = FileOperation.Retry(op)!;
        _q.Enqueue(r1);
        await Done(r1);
        var r2 = FileOperation.Retry(r1)!;
        Assert.Equal("Copying 3 items", r2.Title);  // nothing finished, so no "retrying" note at all
    }

    [Fact]
    public async Task Retrying_a_delete_retries_only_what_was_not_deleted()
    {
        var del = _t.File("delete-me.txt", "x");
        // A file the user may not delete: read-only on Windows, in a read-only folder elsewhere.
        var lockedDir = _t.Dir("locked");
        var locked = _t.File("locked/stuck.txt", "x");
        if (OperatingSystem.IsWindows()) File.SetAttributes(locked, FileAttributes.ReadOnly);
        else File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (CanStillWriteTo(lockedDir, locked)) return;   // e.g. running as root: permissions don't apply

            var op = FileOperation.DeleteBatch(new[] { (del, false), (locked, false) }, useTrash: false, "Deleting 2 items");
            _q.Enqueue(op);
            await Done(op);

            Assert.Equal(OperationStatus.Failed, op.Status);
            Assert.False(File.Exists(del));
            var again = FileOperation.Retry(op)!;
            Assert.Contains("(retrying 1 of 2)", again.Title);
        }
        finally
        {
            if (OperatingSystem.IsWindows()) File.SetAttributes(locked, FileAttributes.Normal);
            else File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool CanStillWriteTo(string dir, string file)
    {
        if (OperatingSystem.IsWindows()) return (File.GetAttributes(file) & FileAttributes.ReadOnly) == 0;
        try
        {
            var probe = Path.Combine(dir, "probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    [Fact]
    public async Task Retrying_a_cancelled_copy_continues_from_where_it_stopped()
    {
        var (src, dst) = Folders();
        for (int i = 0; i < 5; i++) _t.File($"src/{i}.txt", new string('x', 10));
        var op = FileOperation.Transfer(false, Items(src, dst, "0.txt", "1.txt", "2.txt", "3.txt", "4.txt"), "Copying 5 items");
        op.Cts.Cancel();   // cancelled before it starts
        _q.Enqueue(op);
        await Done(op);
        Assert.Equal(OperationStatus.Canceled, op.Status);
        Assert.True(op.CanRetry);

        var again = FileOperation.Retry(op)!;
        Assert.Equal(5, again.Items.Count);
        _q.Enqueue(again);
        await Done(again);
        Assert.Equal(5, Directory.GetFiles(dst).Length);
    }

    [Fact]
    public async Task Retry_of_a_completed_operation_has_nothing_to_do()
    {
        var (src, dst) = Folders();
        _t.File("src/a.txt", "a");
        var op = FileOperation.Transfer(false, Items(src, dst, "a.txt"), "Copying 1 item");
        _q.Enqueue(op);
        await Done(op);
        Assert.False(op.CanRetry);
        Assert.Null(FileOperation.Retry(op));
    }

    [Fact]
    public async Task An_operation_that_does_not_track_items_is_repeated_in_full()
    {
        int runs = 0;
        var op = new FileOperation(OperationKind.Copy, Array.Empty<TransferItem>(), "custom",
            (_, _) => { Interlocked.Increment(ref runs); throw new IOException("boom"); });
        _q.Enqueue(op);
        await Done(op);
        var again = FileOperation.Retry(op)!;
        _q.Enqueue(again);
        await Done(again);
        Assert.Equal(2, runs);
    }
}
