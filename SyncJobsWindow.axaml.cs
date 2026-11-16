using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>Lists, edits and runs the saved sync jobs.</summary>
    public partial class SyncJobsWindow : Window
    {
        private readonly List<SyncJob> _jobs;
        private readonly Func<(string Active, string Other)> _panels;
        private readonly Action<List<SyncJob>> _save;
        private readonly Action<SyncJob> _run;
        private int _current = -1;
        private bool _loading;

        public SyncJobsWindow() : this(new List<SyncJob>(), () => ("", ""), _ => { }, _ => { }) { }

        public SyncJobsWindow(List<SyncJob> jobs, Func<(string Active, string Other)> panels, Action<List<SyncJob>> save, Action<SyncJob> run)
        {
            InitializeComponent();
            _jobs = jobs.Select(j => j.Clone()).ToList();
            _panels = panels;
            _save = save;
            _run = run;

            NewButton.Click += (_, _) =>
            {
                var (a, o) = _panels();
                string leaf = System.IO.Path.GetFileName(a.TrimEnd('/', '\\'));
                _jobs.Add(new SyncJob { Name = string.IsNullOrEmpty(leaf) ? "Sync job" : leaf + " backup", From = a, To = o });
                _current = _jobs.Count - 1;
                Refresh();
                ProblemText.Text = "Check the job, then press Save Job.";
            };
            DeleteButton.Click += async (_, _) =>
            {
                if (Current is not { } j) return;
                if (!await new MessageBox($"Delete the sync job \"{j.Name}\"? The folders aren't touched.", true, "Delete", "Cancel", "Sync Jobs").ShowDialog<bool>(this)) return;
                _jobs.RemoveAt(_current);
                _current = Math.Min(_current, _jobs.Count - 1);
                _save(_jobs.Select(x => x.Clone()).ToList());
                Refresh();
            };
            SaveButton.Click += (_, _) => Save();
            RunButton.Click += (_, _) =>
            {
                if (Save() is { } j) { _run(j); ProblemText.Text = "Started: see the File Operations window."; }
            };
            JobList.SelectionChanged += (_, _) => { if (!_loading) { _current = JobList.SelectedIndex; ShowCurrent(); } };
            DailyBox.IsCheckedChanged += (_, _) => TimeBox.IsEnabled = DailyBox.IsChecked == true;
            _current = _jobs.Count > 0 ? 0 : -1;
            Refresh();
        }

        private SyncJob? Current => _current >= 0 && _current < _jobs.Count ? _jobs[_current] : null;

        private void Refresh()
        {
            _loading = true;
            JobList.ItemsSource = _jobs.Select(j => (j.DailyAt != null ? "⏰ " : "") + j.Name).ToList();
            JobList.SelectedIndex = _current;
            _loading = false;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            var j = Current;
            Editor.IsEnabled = DeleteButton.IsEnabled = j != null;
            ProblemText.Text = j == null ? "Press New from Panels to make a job from the two folders showing." : "";
            j ??= new SyncJob { Name = "" };
            NameBox.Text = j.Name;
            FromBox.Text = j.From;
            ToBox.Text = j.To;
            ModeBox.SelectedIndex = (int)j.Mode;
            ContentBox.IsChecked = j.Content;
            DailyBox.IsChecked = j.DailyAt != null;
            TimeBox.Text = j.DailyAt is { } t ? t.ToString(@"hh\:mm") : "";
            TimeBox.IsEnabled = j.DailyAt != null;
            LastRunText.Text = j.LastRun is { } lr ? $"Last run {lr:g}" : "Not run yet";
        }

        private SyncJob? Save()
        {
            if (Current is not { } old) return null;
            TimeSpan? daily = null;
            if (DailyBox.IsChecked == true)
            {
                if (!QueueSchedule.TryNextTime(TimeBox.Text, DateTime.Now, out var next))
                {
                    ProblemText.Text = "Give the time to run, such as 02:00.";
                    return null;
                }
                daily = next.TimeOfDay;
            }
            var j = new SyncJob
            {
                Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Sync job" : NameBox.Text.Trim(),
                From = (FromBox.Text ?? "").Trim(),
                To = (ToBox.Text ?? "").Trim(),
                Mode = (SyncJobMode)Math.Max(0, ModeBox.SelectedIndex),
                Content = ContentBox.IsChecked == true,
                DailyAt = daily,
                // A new time starts afresh; an unchanged one keeps when it last ran.
                LastRun = daily == old.DailyAt ? old.LastRun : (daily is { } d ? DateTime.Now.Date + d < DateTime.Now ? DateTime.Now : old.LastRun : old.LastRun),
            };
            if (j.Problem() is { } problem) { ProblemText.Text = problem; return null; }
            _jobs[_current] = j;
            _save(_jobs.Select(x => x.Clone()).ToList());
            Refresh();
            ProblemText.Text = "Saved. " + j.Summary();
            return j;
        }
    }
}
