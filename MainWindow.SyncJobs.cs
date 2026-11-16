using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Saved sync jobs (right-click → File Tools → Saved Sync Jobs…, or the command palette). Daily
    /// jobs are checked every minute; a run uses the same planning as the Sync buttons and goes into
    /// the operations queue without asking.
    /// </summary>
    public partial class MainWindow
    {
        private List<SyncJob> _syncJobs = new();
        private DispatcherTimer? _syncJobTimer;
        private readonly HashSet<string> _syncJobsRunning = new();

        private void InitSyncJobs()
        {
            _syncJobs = SyncJobStore.Load();
            _syncJobTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => RunDueSyncJobs());
            _syncJobTimer.Start();
            // A run missed while DORI was closed happens shortly after it opens.
            DispatcherTimer.RunOnce(RunDueSyncJobs, TimeSpan.FromSeconds(30));
            Closing += (_, _) => _syncJobTimer?.Stop();
        }

        private void RunDueSyncJobs()
        {
            foreach (var job in _syncJobs.Where(j => j.IsDue(DateTime.Now)).ToList())
                _ = RunSyncJobAsync(job, byItself: true);
        }

        private void OpenSyncJobs(bool isLeft)
        {
            string active = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            string other = (isLeft ? RPpath.Text : LPpath.Text) ?? "";
            new SyncJobsWindow(_syncJobs, () => (active, other), SaveSyncJobs, job => _ = RunSyncJobAsync(job, byItself: false)).Show(this);
        }

        private void SaveSyncJobs(List<SyncJob> jobs)
        {
            _syncJobs = jobs;
            try { SyncJobStore.Save(jobs); }
            catch (Exception ex) { CrashLog.Write("Saving sync jobs", ex); }
        }

        /// <summary>Plans the job and queues its deletes (mirror) and copies. A job runs once at a time.</summary>
        internal async Task RunSyncJobAsync(SyncJob job, bool byItself)
        {
            if (!_syncJobsRunning.Add(job.Name)) return;
            try
            {
                var stamp = _syncJobs.FirstOrDefault(j => j.Name == job.Name) ?? job;
                stamp.LastRun = DateTime.Now;
                SaveSyncJobs(_syncJobs);

                var ops = new List<FileOperation>();
                try
                {
                    if (job.Mode == SyncJobMode.TwoWay)
                    {
                        var plan = await Task.Run(() => DirectoryComparer.PlanTwoWay(job.From, job.To, job.Content));
                        if (plan.Copies.Count > 0)
                            ops.Add(FileOperation.Transfer(false, plan.Copies, $"{job.Name}: copying {plan.Copies.Count} item(s) both ways"));
                    }
                    else
                    {
                        var plan = await Task.Run(() => DirectoryComparer.PlanSync(job.From, job.To, job.Content));
                        if (job.Mode == SyncJobMode.Mirror && plan.Deletes.Count > 0)
                        {
                            // A job runs without asking, so its removals go to the trash where there is one.
                            bool trash = ProviderRegistry.For(job.To) is LocalFileProvider;
                            ops.Add(FileOperation.DeleteBatch(plan.Deletes, useTrash: trash,
                                $"{job.Name}: removing {plan.Deletes.Count} item(s) not in {job.From}"));
                        }
                        if (plan.Copies.Count > 0)
                            ops.Add(FileOperation.Transfer(false, plan.Copies, $"{job.Name}: copying {plan.Copies.Count} item(s) → {job.To}", job.To));
                    }
                }
                catch (Exception ex)
                {
                    string why = $"Sync job {job.Name} couldn't compare the folders: {ex.Message}";
                    if (byItself) LeftStatusText.Text = why;
                    else await new MessageBox(why, "Sync Jobs").ShowDialog(this);
                    return;
                }

                if (ops.Count == 0)
                {
                    string none = $"Sync job {job.Name}: already up to date.";
                    LeftStatusText.Text = none;
                    if (!byItself) await new MessageBox(none, "Sync Jobs").ShowDialog(this);
                    return;
                }
                if (byItself)
                {
                    // Not raising the window over whatever is being done; it opens if something fails.
                    foreach (var o in ops)
                    {
                        o.AfterFinish = done => Dispatcher.UIThread.Post(() =>
                        {
                            RefreshLPGrid();
                            RefreshRPGrid();
                            if (done.Status == OperationStatus.Failed) OperationsWindow.ShowSingleton(this);
                        });
                        OperationQueue.Instance.Enqueue(o);
                    }
                    LeftStatusText.Text = $"Sync job {job.Name} started.";
                }
                else EnqueueSequenceAndRefresh(ops);
            }
            finally { _syncJobsRunning.Remove(job.Name); }
        }
    }
}
