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
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 0, 0, 0)
            };
            cancel.Click += (_, _) => _queue.Cancel(op);
            Grid.SetColumn(cancel, 1);

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            grid.Children.Add(left);
            grid.Children.Add(cancel);

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
                Bar = bar, Stats = stats, Cancel = cancel
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

            switch (op.Status)
            {
                case OperationStatus.Queued:
                    row.Bar.IsIndeterminate = false;
                    row.Bar.Value = 0;
                    row.Stats.Text = "Waiting…";
                    // leave Foreground at the muted default (Gray) set at creation
                    row.Stats.Foreground = Brushes.Gray;
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
            int running = ops.Count(o => o.Status == OperationStatus.Running);
            int queued = ops.Count(o => o.Status == OperationStatus.Queued);
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
                if (queued > 0) parts.Add($"{queued} queued");
                if (done > 0) parts.Add($"{done} done");
                if (failed > 0) parts.Add($"{failed} failed");
                if (canceled > 0) parts.Add($"{canceled} cancelled");
                SummaryText.Text = string.Join("  ·  ", parts);
            }

            bool anyActive = running > 0 || queued > 0;
            bool anyFinished = done > 0 || failed > 0 || canceled > 0;
            CancelAllBtn.IsEnabled = anyActive;
            ClearBtn.IsEnabled = anyFinished;
        }

        // ---- Toolbar buttons ----

        private void CancelAllBtn_Click(object? sender, RoutedEventArgs e) => _queue.CancelAll();

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
