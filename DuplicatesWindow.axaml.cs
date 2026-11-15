using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Finds files with identical contents in a panel's folder (and optionally the other panel's),
    /// lists them grouped, and deletes the copies marked — to the trash when it's available, so
    /// the delete can be undone like any other.
    /// </summary>
    public partial class DuplicatesWindow : Window
    {
        /// <summary>One row: a group heading, or one copy with a mark box.</summary>
        public sealed class Row : INotifyPropertyChanged
        {
            private bool _marked;
            public string Text { get; init; } = "";
            public string Detail { get; init; } = "";
            public string? Tip { get; init; }
            public bool IsFile => Entry != null;
            public FontWeight Weight => IsFile ? FontWeight.Normal : FontWeight.Bold;
            public FileEntry? Entry { get; init; }
            public DuplicateGroup? Group { get; init; }

            public bool IsMarked
            {
                get => _marked;
                set { if (_marked == value) return; _marked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked))); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly MainWindow? _owner;
        private readonly string _folder = "";
        private readonly string? _otherFolder;
        private readonly bool _showHidden;
        private readonly bool _fromLeft;
        private List<DuplicateGroup> _groups = new();
        private List<Row> _rows = new();
        private CancellationTokenSource? _cts;

        public DuplicatesWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public DuplicatesWindow(MainWindow owner, string folder, string? otherFolder, bool showHidden, bool fromLeft) : this()
        {
            _owner = owner;
            _folder = folder;
            _otherFolder = otherFolder;
            _showHidden = showHidden;
            _fromLeft = fromLeft;

            this.FindControl<TextBlock>("ScopeText")!.Text = "Looking in: " + folder;
            var other = this.FindControl<CheckBox>("OtherPanelBox")!;
            other.IsVisible = otherFolder != null;
            if (otherFolder != null) ToolTip.SetTip(other, otherFolder);

            this.FindControl<Button>("FindButton")!.Click += async (_, _) => await FindAsync();
            this.FindControl<Button>("StopButton")!.Click += (_, _) => _cts?.Cancel();
            this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
            this.FindControl<Button>("KeepOldestButton")!.Click += (_, _) => MarkAllBut(KeepRule.Oldest);
            this.FindControl<Button>("KeepNewestButton")!.Click += (_, _) => MarkAllBut(KeepRule.Newest);
            this.FindControl<Button>("KeepShortestButton")!.Click += (_, _) => MarkAllBut(KeepRule.ShortestPath);
            this.FindControl<Button>("ClearMarksButton")!.Click += (_, _) => { foreach (var r in _rows) r.IsMarked = false; };
            this.FindControl<Button>("DeleteButton")!.Click += async (_, _) => await DeleteMarkedAsync();
            this.FindControl<ListBox>("ResultsList")!.DoubleTapped += (_, _) =>
            {
                if (this.FindControl<ListBox>("ResultsList")!.SelectedItem is Row { Entry: { } e }) _owner?.RevealInPanel(_fromLeft, e.Path);
            };
            Closing += (_, _) => _cts?.Cancel();
            Opened += async (_, _) => await FindAsync();
        }

        private async Task FindAsync()
        {
            var status = this.FindControl<TextBlock>("StatusText")!;
            var find = this.FindControl<Button>("FindButton")!;
            var stop = this.FindControl<Button>("StopButton")!;
            bool recursive = this.FindControl<CheckBox>("SubfoldersBox")!.IsChecked == true;
            var roots = new List<string> { _folder };
            if (_otherFolder != null && this.FindControl<CheckBox>("OtherPanelBox")!.IsChecked == true) roots.Add(_otherFolder);

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            find.IsVisible = false;
            stop.IsVisible = true;
            SetRows(new List<DuplicateGroup>());
            status.Text = "Listing files…";

            // Progress arrives for every file; show it at most a few times a second.
            DuplicateFinder.Progress? latest = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) => { if (latest is { } p) status.Text = $"{p.Stage}… {p.Done:N0} of {p.Total:N0} files"; };
            timer.Start();
            try
            {
                var (groups, unreadable, count) = await Task.Run(() =>
                {
                    var files = DuplicateFinder.CollectFiles(roots, _showHidden, recursive, ct);
                    var found = DuplicateFinder.Find(files, out int bad, new Progress<DuplicateFinder.Progress>(p => latest = p), ct);
                    return (found, bad, files.Count);
                }, ct);
                timer.Stop();
                SetRows(groups);
                status.Text = groups.Count == 0
                    ? $"No duplicates among {count:N0} files."
                    : $"{groups.Count:N0} sets of identical files ({groups.Sum(g => g.Files.Count):N0} files) among {count:N0}. " +
                      $"Deleting all but one of each would free {Format(groups.Sum(g => g.Wasted))}."
                      + (unreadable > 0 ? $" {unreadable:N0} files couldn't be read and were skipped." : "");
            }
            catch (OperationCanceledException)
            {
                timer.Stop();
                status.Text = "Stopped.";
            }
            catch (Exception ex)
            {
                timer.Stop();
                status.Text = "The search failed: " + ex.Message;
            }
            finally
            {
                find.IsVisible = true;
                stop.IsVisible = false;
            }
        }

        private void SetRows(List<DuplicateGroup> groups)
        {
            _groups = groups;
            _rows = new List<Row>();
            foreach (var g in groups)
            {
                _rows.Add(new Row { Text = $"{g.Files.Count} copies · {Format(g.Size)} each", Group = g });
                foreach (var f in g.Files)
                {
                    var row = new Row { Text = Shown(f.Path), Tip = f.Path, Detail = f.LastModified.ToString("g"), Entry = f, Group = g };
                    row.PropertyChanged += (_, _) => UpdateMarked();
                    _rows.Add(row);
                }
            }
            this.FindControl<ListBox>("ResultsList")!.ItemsSource = _rows;
            UpdateMarked();
        }

        /// <summary>
        /// A path as the list shows it: relative to the folder searched, and with that folder's
        /// name in front when both panels' folders were searched.
        /// </summary>
        private string Shown(string path)
        {
            bool both = _otherFolder != null && this.FindControl<CheckBox>("OtherPanelBox")!.IsChecked == true;
            foreach (var root in new[] { _folder, _otherFolder })
            {
                if (root == null) continue;
                string prefix = root.TrimEnd('/', '\\');
                if (path.Length > prefix.Length + 1 && path.StartsWith(prefix, StringComparison.Ordinal)
                    && path[prefix.Length] is '/' or '\\' or '!')
                {
                    string rel = path[(prefix.Length + 1)..].TrimStart('/');
                    return both ? ConflictResolver.LeafOf(prefix) + "/" + rel : rel;
                }
            }
            return path;
        }

        private void MarkAllBut(KeepRule rule)
        {
            var toMark = new HashSet<FileEntry>(_groups.SelectMany(g => DuplicateFinder.AllButKept(g, rule)));
            foreach (var r in _rows.Where(r => r.Entry != null)) r.IsMarked = toMark.Contains(r.Entry!);
        }

        private List<Row> Marked() => _rows.Where(r => r.IsMarked && r.Entry != null).ToList();

        private void UpdateMarked()
        {
            var marked = Marked();
            this.FindControl<Button>("DeleteButton")!.IsEnabled = marked.Count > 0;
            this.FindControl<TextBlock>("MarkedText")!.Text = marked.Count == 0
                ? "Double-click a file to show it in the panel."
                : $"{marked.Count:N0} marked ({Format(marked.Sum(r => r.Entry!.Size))})" +
                  (EveryCopyMarked() ? " — every copy of some file is marked!" : "");
        }

        private bool EveryCopyMarked()
            => _groups.Any(g => g.Files.All(f => _rows.Any(r => r.IsMarked && ReferenceEquals(r.Entry, f))));

        private async Task DeleteMarkedAsync()
        {
            var marked = Marked();
            if (marked.Count == 0 || _owner == null) return;

            bool trash = AppOptions.UseTrash && TrashService.IsSupported(marked[0].Entry!.Path)
                         && ProviderRegistry.For(marked[0].Entry!.Path) is LocalFileProvider;
            string warning = EveryCopyMarked() ? "\n\nEvery copy of some files is marked, so those files will be gone entirely." : "";
            string how = trash ? "to the trash (Undo can put them back)" : "permanently";
            bool ok = await new MessageBox($"Delete {marked.Count:N0} files ({Format(marked.Sum(r => r.Entry!.Size))}) {how}?{warning}",
                                           showCancel: true, okText: "Delete", cancelText: "Cancel", title: "Find Duplicates")
                .ShowDialog<bool>(this);
            if (!ok) return;

            var targets = marked.Select(r => (r.Entry!.Path, false)).ToList();
            var op = FileOperation.DeleteBatch(targets, trash, $"Deleting {marked.Count:N0} duplicate files");
            var owner = _owner;
            string folder = _folder;
            op.AfterFinish = done => Dispatcher.UIThread.Post(() =>
            {
                owner.RecordDeleteForUndo(folder, done.Trashed);
                owner.RefreshBothPanels();
                if (done.Status == OperationStatus.Failed) OperationsWindow.ShowSingleton(owner);
                if (IsVisible) RemoveGone();
            });
            OperationQueue.Instance.Enqueue(op);
        }

        /// <summary>After a delete, drops the copies that are gone, and groups left with one copy.</summary>
        private void RemoveGone()
        {
            var groups = _groups
                .Select(g => g with { Files = g.Files.Where(f => File.Exists(f.Path) || ProviderRegistry.For(f.Path) is not LocalFileProvider).ToList() })
                .Where(g => g.Files.Count > 1)
                .ToList();
            SetRows(groups);
            this.FindControl<TextBlock>("StatusText")!.Text = groups.Count == 0
                ? "No duplicates left."
                : $"{groups.Count:N0} sets of identical files left.";
        }

        private static string Format(long bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? $"{bytes:N0} bytes" : $"{v:0.#} {units[u]}";
        }
    }
}
