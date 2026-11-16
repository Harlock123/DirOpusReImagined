using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Non-modal, single-instance window that shows the live state of the app-wide
    /// <see cref="OperationQueue"/>: one card per operation with a progress bar, current file,
    /// bytes/speed/ETA, and a per-operation cancel button, plus bulk "Cancel all" / "Clear finished".
    /// <para>
    /// It is a pure view over the queue — closing it never stops the queue, and reopening rebuilds
    /// the cards from the current queue state.
    /// </para>
    /// </summary>
    public partial class OperationsWindow : Window
    {
        private static OperationsWindow? _instance;

        private readonly OperationQueue _queue = OperationQueue.Instance;
        private readonly Dictionary<FileOperation, Row> _rows = new();

        /// <summary>Groups the controls that make up one operation card, for cheap in-place updates.</summary>
        private sealed class Row
        {
            public FileOperation Op = null!;
            public Border Card = null!;
            public TextBlock Title = null!;
            public TextBlock File = null!;
            public ProgressBar Bar = null!;
            public TextBlock Stats = null!;
            public Button Cancel = null!;
            public Button Pause = null!;
            public Button Retry = null!;
        }

        public OperationsWindow()
        {
            InitializeComponent();

            // Rebuild any operations already in the queue (e.g. when reopened mid-transfer).
            foreach (var op in _queue.Operations)
                AddRow(op);
            UpdateSummary();

            _queue.OperationAdded += OnAdded;
            _queue.OperationUpdated += OnUpdated;
            _queue.OperationCompleted += OnUpdated;

            InitLimitAndHold();
        }

        // ---- Speed limit and Start Later ----

        private void InitLimitAndHold()
        {
            SpeedLimitBox.Text = SpeedLimiter.Describe(AppOptions.SpeedLimit);
            SpeedLimitBox.LostFocus += (_, _) => CommitSpeedLimit();
            SpeedLimitBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) CommitSpeedLimit(); };

            Action onHold = () => Dispatcher.UIThread.Post(UpdateHold);
            _queue.HoldChanged += onHold;
            Closed += (_, _) =>
            {
                _queue.HoldChanged -= onHold;
                _queue.OperationAdded -= OnAdded;
                _queue.OperationUpdated -= OnUpdated;
                _queue.OperationCompleted -= OnUpdated;
            };
            UpdateHold();
        }

        private void CommitSpeedLimit()
        {
            if (!SpeedLimiter.TryParse(SpeedLimitBox.Text, out long limit))
            {
                SpeedLimitBox.Text = SpeedLimiter.Describe(AppOptions.SpeedLimit);
                _ = new MessageBox("Type a speed such as 10 MB or 512 KB (per second), or leave it empty for no limit.", "Speed limit").ShowDialog(this);
                return;
            }
            SpeedLimitBox.Text = SpeedLimiter.Describe(limit);
            if (limit == AppOptions.SpeedLimit) return;
            AppOptions.SpeedLimit = limit;
            ConfigSettings.Save("SpeedLimit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private async void StartLaterBtn_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new TextPromptDialog("Start later",
                "Start the operations that haven't started at what time? For example 02:00 or 23:30. " +
                "A time already gone today means tomorrow. Anything added meanwhile waits too.",
                _queue.HoldUntil?.ToString("HH:mm") ?? "02:00");
            string? text = await dialog.ShowDialog<string?>(this);
            if (text == null) return;
            if (!QueueSchedule.TryNextTime(text, DateTime.Now, out var when))
            {
                await new MessageBox($"\"{text}\" isn't a time. Try 02:00, 2am or 23:30.", "Start later").ShowDialog(this);
                return;
            }
            _queue.SetHold(when);
        }

        private void StartNowBtn_Click(object? sender, RoutedEventArgs e) => _queue.SetHold(null);

        private void UpdateHold()
        {
            var until = _queue.HoldUntil;
            bool held = until is { } u && u > DateTime.Now;
            StartNowBtn.IsVisible = held;
            HoldText.Text = held ? $"Waiting until {QueueSchedule.Describe(until!.Value, DateTime.Now)}" : "";
        }

        /// <summary>Opens the shared window (or brings the existing one to the front).</summary>
        public static void ShowSingleton(Window owner)
        {
            if (_instance is { } existing)
            {
                if (existing.WindowState == WindowState.Minimized)
                    existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }

            var win = new OperationsWindow();
            _instance = win;
            win.Closed += (_, _) => { if (ReferenceEquals(_instance, win)) _instance = null; };
            win.Show(owner);
        }

        // ---- Queue event handlers (fire on the worker thread) -> marshal to UI ----

        private void OnAdded(FileOperation op) =>
            Dispatcher.UIThread.Post(() => { AddRow(op); UpdateSummary(); });

        private void OnUpdated(FileOperation op) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_rows.TryGetValue(op, out var row)) UpdateRow(row);
                else AddRow(op);
                UpdateSummary();
            });

        // ---- Card construction / update ----

        private void AddRow(FileOperation op)
        {
            if (_rows.ContainsKey(op)) return;

            var title = new TextBlock { FontWeight = FontWeight.SemiBold, Text = op.Title };
            var file = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Text = "" };
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 12 };
            var stats = new TextBlock
            {
                Foreground = Brushes.Gray,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,   // failure messages can be long
                Text = ""
            };

            var left = new StackPanel { Spacing = 4 };
            left.Children.Add(title);
            left.Children.Add(file);
            left.Children.Add(bar);
            left.Children.Add(stats);
            Grid.SetColumn(left, 0);

            var cancel = new Button
            {
                Content = "✕",
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cancel.Click += (_, _) => _queue.Cancel(op);

            ToolTip.SetTip(cancel, "Cancel");

            var pause = new Button
            {
                Content = "⏸",
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            pause.Click += (_, _) =>
            {
                if (op.IsPaused) _queue.Resume(op); else _queue.Pause(op);
            };

            var retry = new Button
            {
                Content = "↻ Retry",
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsVisible = false   // only shown for failed or cancelled operations
            };
            ToolTip.SetTip(retry, "Run the items that didn't finish");
            retry.Click += (_, _) => RetryOp(op);

            var actions = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 0, 0, 0)
            };
            actions.Children.Add(pause);
            actions.Children.Add(cancel);
            actions.Children.Add(retry);
            Grid.SetColumn(actions, 1);

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            grid.Children.Add(left);
            grid.Children.Add(actions);

            var card = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Child = grid
            };

            var row = new Row
            {
                Op = op, Card = card, Title = title, File = file,
                Bar = bar, Stats = stats, Cancel = cancel, Pause = pause, Retry = retry
            };
            _rows[op] = row;
            OpsPanel.Children.Add(card);
            UpdateRow(row);
        }

        private void UpdateRow(Row row)
        {
            var op = row.Op;
            var p = op.Progress;

            row.File.Text = string.IsNullOrEmpty(p.CurrentFile) ? "" : p.CurrentFile;

            // Retry is offered for failed and cancelled operations; pause while not finished.
            row.Retry.IsVisible = op.CanRetry;
            row.Pause.IsVisible = !op.IsTerminal;
            row.Pause.Content = op.IsPaused ? "▶" : "⏸";
            ToolTip.SetTip(row.Pause, op.IsPaused ? "Resume" : "Pause");

            switch (op.Status)
            {
                case OperationStatus.Queued:
                    row.Bar.IsIndeterminate = false;
                    row.Bar.Value = 0;
                    row.Stats.Text = op.IsPaused ? "Paused — waiting operations behind it will go first" : "Waiting…";
                    // leave Foreground at the muted default (Gray) set at creation
                    row.Stats.Foreground = Brushes.Gray;
                    row.Cancel.IsEnabled = true;
                    row.Cancel.IsVisible = true;
                    break;

                case OperationStatus.Running when op.IsPaused:
                    // Hold the bar where it is; no speed or time left while nothing moves.
                    row.Bar.IsIndeterminate = false;
                    if (p.HasTotal) row.Bar.Value = p.Fraction;
                    string at = ProgressFormat.Stats(p with { BytesPerSecond = 0 });
                    row.Stats.Text = at.Length > 0 ? $"Paused · {at}" : "Paused";
                    row.Stats.Foreground = Brushes.DarkGoldenrod;
                    row.Cancel.IsEnabled = true;
                    row.Cancel.IsVisible = true;
                    break;

                case OperationStatus.Running:
                    if (p.HasTotal) { row.Bar.IsIndeterminate = false; row.Bar.Value = p.Fraction; }
                    else row.Bar.IsIndeterminate = true;
                    row.Stats.Text = ProgressFormat.Stats(p);
                    row.Stats.Foreground = Brushes.Gray;
                    row.Cancel.IsEnabled = true;
                    row.Cancel.IsVisible = true;
                    break;

                case OperationStatus.Completed:
                    row.Bar.IsIndeterminate = false;
                    row.Bar.Value = 1;
                    row.File.Text = "";
                    row.Stats.Text = "Completed";
                    row.Stats.Foreground = Brushes.SeaGreen;
                    row.Cancel.IsVisible = false;
                    break;

                case OperationStatus.Canceled:
                    row.Bar.IsIndeterminate = false;
                    row.File.Text = "";
                    row.Stats.Text = "Cancelled";
                    row.Stats.Foreground = Brushes.Gray;
                    row.Cancel.IsVisible = false;
                    break;

                case OperationStatus.Failed:
                    row.Bar.IsIndeterminate = false;
                    row.File.Text = "";
                    row.Stats.Text = "Failed: " + (op.Error?.Message ?? "unknown error");
                    row.Stats.Foreground = Brushes.IndianRed;
                    row.Cancel.IsVisible = false;
                    break;
            }
        }

        private void UpdateSummary()
        {
            var ops = _queue.Operations;
            int paused = ops.Count(o => o.IsPaused && !o.IsTerminal);
            int running = ops.Count(o => o.Status == OperationStatus.Running && !o.IsPaused);
            int queued = ops.Count(o => o.Status == OperationStatus.Queued && !o.IsPaused);
            int done = ops.Count(o => o.Status == OperationStatus.Completed);
            int failed = ops.Count(o => o.Status == OperationStatus.Failed);
            int canceled = ops.Count(o => o.Status == OperationStatus.Canceled);

            EmptyHint.IsVisible = ops.Count == 0;

            if (ops.Count == 0)
            {
                SummaryText.Text = "No operations";
            }
            else
            {
                var parts = new List<string>();
                if (running > 0) parts.Add($"{running} running");
                if (paused > 0) parts.Add($"{paused} paused");
                if (queued > 0) parts.Add($"{queued} queued");
                if (done > 0) parts.Add($"{done} done");
                if (failed > 0) parts.Add($"{failed} failed");
                if (canceled > 0) parts.Add($"{canceled} cancelled");
                SummaryText.Text = string.Join("  ·  ", parts);
            }

            bool anyActive = running > 0 || queued > 0 || paused > 0;
            bool anyFinished = done > 0 || failed > 0 || canceled > 0;
            CancelAllBtn.IsEnabled = anyActive;
            ClearBtn.IsEnabled = anyFinished;
            RetryAllBtn.IsEnabled = failed > 0 || canceled > 0;

            // One button: pause while anything is going, resume once everything left is paused.
            bool allPaused = anyActive && running == 0 && queued == 0;
            PauseAllBtn.Content = allPaused ? "Resume all" : "Pause all";
            PauseAllBtn.IsEnabled = anyActive;
        }

        // ---- Retry ----

        /// <summary>
        /// Queues a new operation for what a failed or cancelled one left undone, and clears the old
        /// card so the list doesn't fill up with superseded attempts.
        /// </summary>
        private void RetryOp(FileOperation op)
        {
            var again = FileOperation.Retry(op);
            if (again == null)
            {
                // Everything did finish after all (e.g. the failure was in follow-up work).
                if (_rows.TryGetValue(op, out var r)) r.Stats.Text = "Nothing left to retry";
                return;
            }
            _queue.Enqueue(again);   // OperationAdded -> OnAdded builds a fresh card automatically
            RemoveFinished(new[] { op });
        }

        private void RemoveFinished(IEnumerable<FileOperation> ops)
        {
            foreach (var removed in _queue.Remove(ops))
            {
                if (_rows.TryGetValue(removed, out var row))
                {
                    OpsPanel.Children.Remove(row.Card);
                    _rows.Remove(removed);
                }
            }
            UpdateSummary();
        }

        // ---- Toolbar buttons ----

        private void CancelAllBtn_Click(object? sender, RoutedEventArgs e) => _queue.CancelAll();

        private void PauseAllBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (PauseAllBtn.Content as string == "Resume all") _queue.ResumeAll();
            else _queue.PauseAll();
        }

        private void RetryAllBtn_Click(object? sender, RoutedEventArgs e)
        {
            // Snapshot first: enqueuing mutates the queue's operation list as we go.
            var retryable = _queue.Operations.Where(o => o.CanRetry).ToList();
            foreach (var op in retryable)
                RetryOp(op);
        }

        private void ClearBtn_Click(object? sender, RoutedEventArgs e)
        {
            var removed = _queue.ClearFinished();
            foreach (var op in removed)
            {
                if (_rows.TryGetValue(op, out var row))
                {
                    OpsPanel.Children.Remove(row.Card);
                    _rows.Remove(op);
                }
            }
            UpdateSummary();
        }

        protected override void OnClosed(EventArgs e)
        {
            _queue.OperationAdded -= OnAdded;
            _queue.OperationUpdated -= OnUpdated;
            _queue.OperationCompleted -= OnUpdated;
            base.OnClosed(e);
        }
    }
}
