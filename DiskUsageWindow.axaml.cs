using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Disk Usage (right-click → Disk Usage…): measures everything under a folder, then lists what
    /// is inside it largest first, with a bar for each item's share. Folders open to show what
    /// takes their space; Show in Panel goes to an item in the panel.
    /// </summary>
    public partial class DiskUsageWindow : Window
    {
        public sealed class Row
        {
            public UsageNode Node { get; init; } = null!;
            public string Name { get; init; } = "";
            public double Percent { get; init; }
            public string SizeText { get; init; } = "";
            public string PercentText { get; init; } = "";
            public string FilesText { get; init; } = "";
            public string? Tip { get; init; }
            public FontWeight Weight => Node.IsDirectory ? FontWeight.SemiBold : FontWeight.Normal;
        }

        private readonly MainWindow? _owner;
        private readonly string _root = "";
        private readonly bool _fromLeft;
        private UsageNode? _top;
        private UsageNode? _current;
        private CancellationTokenSource? _cts;

        public DiskUsageWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public DiskUsageWindow(MainWindow owner, string root, bool fromLeft) : this()
        {
            _owner = owner;
            _root = root;
            _fromLeft = fromLeft;

            var list = this.FindControl<ListBox>("UsageList")!;
            list.SelectionChanged += (_, _) => UpdateButtons();
            list.DoubleTapped += (_, _) => OpenSelected();
            list.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
                else if (e.Key == Key.Back) { GoUp(); e.Handled = true; }
            };
            this.FindControl<Button>("UpButton")!.Click += (_, _) => GoUp();
            this.FindControl<Button>("OpenButton")!.Click += (_, _) => OpenSelected();
            this.FindControl<Button>("RevealButton")!.Click += (_, _) => RevealSelected();
            this.FindControl<Button>("RescanButton")!.Click += async (_, _) => await ScanAsync();
            this.FindControl<Button>("StopButton")!.Click += (_, _) => _cts?.Cancel();
            this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
            Opened += async (_, _) => await ScanAsync();
            Closed += (_, _) => _cts?.Cancel();
        }

        private async Task ScanAsync()
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var status = this.FindControl<TextBlock>("StatusText")!;
            this.FindControl<Button>("StopButton")!.IsVisible = true;
            this.FindControl<Button>("RescanButton")!.IsEnabled = false;
            this.FindControl<TextBlock>("PathText")!.Text = _root;
            status.Text = "Measuring…";

            string? keep = _current?.Path;
            var progress = new Progress<long>(n => { if (!cts.IsCancellationRequested) status.Text = $"Measuring… {n:N0} files so far"; });
            try
            {
                _top = await Task.Run(() => DiskUsage.Scan(_root, progress, cts.Token), cts.Token);
                Show(Find(_top, keep) ?? _top);
            }
            catch (OperationCanceledException)
            {
                if (!IsVisible) return;
                status.Text = "Stopped before the end. Measure Again to start over.";
            }
            catch (Exception ex)
            {
                status.Text = "Couldn't measure it: " + ex.Message;
            }
            finally
            {
                this.FindControl<Button>("StopButton")!.IsVisible = false;
                this.FindControl<Button>("RescanButton")!.IsEnabled = true;
            }
        }

        private static UsageNode? Find(UsageNode top, string? path)
        {
            if (path == null) return null;
            var stack = new Stack<UsageNode>();
            stack.Push(top);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n.Path == path) return n;
                if (path.StartsWith(n.Path, StringComparison.Ordinal))
                    foreach (var c in n.Children) if (c.IsDirectory) stack.Push(c);
            }
            return null;
        }

        private void Show(UsageNode folder)
        {
            _current = folder;
            this.FindControl<TextBlock>("PathText")!.Text = folder.Path;
            string note = folder.Incomplete ? " Some folders couldn't be read, so the real total is larger." : "";
            this.FindControl<TextBlock>("StatusText")!.Text =
                $"{ProgressFormat.Bytes(folder.Size)} in {folder.FileCount:N0} files. Largest first; linked folders aren't followed.{note}";
            long biggest = folder.Children.Count == 0 ? 0 : folder.Children.Max(c => c.Size);
            var rows = folder.BySize().Select(c => new Row
            {
                Node = c,
                Name = (c.IsDirectory ? "📁 " : "") + c.Name + (c.IsLink ? "  (link)" : ""),
                // Bars are scaled to the largest item, so the small ones are still told apart.
                Percent = DiskUsage.Share(c.Size, biggest) * 100,
                SizeText = ProgressFormat.Bytes(c.Size),
                PercentText = $"{DiskUsage.Share(c.Size, folder.Size) * 100:0.#}%",
                FilesText = c.IsDirectory ? $"{c.FileCount:N0} files" : "",
                Tip = c.Path + (c.Incomplete ? "\nPart of this folder couldn't be read." : ""),
            }).ToList();
            var list = this.FindControl<ListBox>("UsageList")!;
            list.ItemsSource = rows;
            if (rows.Count > 0) list.SelectedIndex = 0;
            this.FindControl<Button>("UpButton")!.IsEnabled = folder.Parent != null;
            UpdateButtons();
            list.Focus();
        }

        private Row? Selected => this.FindControl<ListBox>("UsageList")!.SelectedItem as Row;

        private void UpdateButtons()
        {
            var row = Selected;
            this.FindControl<Button>("OpenButton")!.IsEnabled = row is { Node: { IsDirectory: true, IsLink: false } };
            this.FindControl<Button>("RevealButton")!.IsEnabled = row != null && _owner != null;
        }

        private void OpenSelected()
        {
            if (Selected is { Node: { IsDirectory: true, IsLink: false } node }) Show(node);
        }

        private void GoUp()
        {
            if (_current?.Parent is { } parent)
            {
                var from = _current;
                Show(parent);
                var list = this.FindControl<ListBox>("UsageList")!;
                list.SelectedItem = (list.ItemsSource as List<Row>)?.FirstOrDefault(r => r.Node == from);
            }
        }

        private void RevealSelected()
        {
            if (Selected is { } row) _owner?.RevealInPanel(_fromLeft, row.Node.Path);
        }
    }
}
