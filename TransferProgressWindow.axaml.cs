using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Modal progress dialog that drives a <see cref="TransferCoordinator"/> batch and reports
    /// live progress (current file, overall bar, bytes / speed / ETA) with a Cancel button.
    /// The caller awaits <c>ShowDialog</c>, then inspects <see cref="Error"/> / <see cref="Canceled"/>.
    /// </summary>
    public partial class TransferProgressWindow : Window
    {
        private readonly IReadOnlyList<TransferItem> _items;
        private readonly bool _move;
        private readonly CancellationTokenSource _cts = new();
        private bool _finished;

        public Exception? Error { get; private set; }
        public bool Canceled { get; private set; }

        // Parameterless ctor for the XAML designer.
        public TransferProgressWindow()
        {
            InitializeComponent();
            _items = Array.Empty<TransferItem>();
        }

        public TransferProgressWindow(string title, IReadOnlyList<TransferItem> items, bool move)
        {
            InitializeComponent();
            Title = title;
            HeaderText.Text = title + "…";
            _items = items;
            _move = move;
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Progress captures this UI thread's context, so Update runs on the UI thread.
            var progress = new Progress<TransferProgress>(Update);
            try
            {
                await TransferCoordinator.RunAsync(_items, _move, progress, _cts.Token);
            }
            catch (OperationCanceledException) { Canceled = true; }
            catch (Exception ex) { Error = ex; }
            finally
            {
                _finished = true;
                Close();
            }
        }

        private void Update(TransferProgress p)
        {
            if (p.FileCount > 0)
                HeaderText.Text = $"{Title}  ({p.FileIndex} of {p.FileCount})";

            FileText.Text = p.CurrentFile;

            if (p.HasTotal)
            {
                Bar.IsIndeterminate = false;
                Bar.Value = p.Fraction;
            }
            else
            {
                Bar.IsIndeterminate = true;
            }

            StatsText.Text = BuildStats(p);
        }

        private static string BuildStats(TransferProgress p)
        {
            string s;
            if (p.BytesTotal > 0)
                s = $"{Human(p.BytesDone)} / {Human(p.BytesTotal)}";
            else if (p.BytesDone > 0)
                s = Human(p.BytesDone);
            else
                s = "";

            if (p.BytesPerSecond > 0)
                s += $"   {Human((long)p.BytesPerSecond)}/s";

            if (p.Eta is { } eta)
                s += $"   ETA {FormatEta(eta)}";

            return s;
        }

        private void CancelBtn_Click(object? sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            CancelBtn.IsEnabled = false;
            HeaderText.Text = "Cancelling…";
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // Closing via the title bar before completion cancels the transfer.
            if (!_finished) _cts.Cancel();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cts.Dispose();
        }

        private static string Human(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return i == 0 ? $"{bytes} {units[i]}" : $"{v:0.0} {units[i]}";
        }

        private static string FormatEta(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }
    }
}
