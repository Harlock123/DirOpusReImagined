using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Find Similar Photos (right-click → File Tools): groups of look-alike pictures, the best of each first. See <see cref="SimilarPhotos"/>.</summary>
    public partial class SimilarPhotosWindow : Window
    {
        public sealed class Row
        {
            public bool IsPhoto { get; init; }
            public bool IsMarked { get; set; }
            public bool IsBest { get; init; }
            public string Text { get; init; } = "";
            public string Detail { get; init; } = "";
            public string Tip { get; init; } = "";
            public string Path { get; init; } = "";
            public Bitmap? Thumb { get; init; }
            public FontWeight Weight => IsPhoto ? FontWeight.Normal : FontWeight.Bold;
        }

        private readonly List<string> _picked = new();
        private readonly string _folder = "";
        private readonly Func<IReadOnlyList<string>, Task<int>>? _trash;
        private readonly Action<string>? _show;
        private CancellationTokenSource? _cts;
        private List<Row> _rows = new();

        public SimilarPhotosWindow() { InitializeComponent(); }

        public SimilarPhotosWindow(IReadOnlyList<string> picked, string folder, Func<IReadOnlyList<string>, Task<int>> trash, Action<string> show) : this()
        {
            _picked.AddRange(picked);
            _folder = folder;
            _trash = trash;
            _show = show;
            ScopeText.Text = picked.Count == 1 ? $"Look in: {picked[0]}" : $"Look at {picked.Count} items in {folder}";
            FindButton.Click += async (_, _) => await FindAsync();
            StopButton.Click += (_, _) => _cts?.Cancel();
            MarkButton.Click += (_, _) => { foreach (var r in _rows.Where(r => r.IsPhoto)) r.IsMarked = !r.IsBest; Refresh(); };
            ClearButton.Click += (_, _) => { foreach (var r in _rows) r.IsMarked = false; Refresh(); };
            ShowButton.Click += (_, _) => { if (ResultsList.SelectedItem is Row { IsPhoto: true } r) _show?.Invoke(r.Path); };
            ResultsList.DoubleTapped += (_, _) => { if (ResultsList.SelectedItem is Row { IsPhoto: true } r) _show?.Invoke(r.Path); };
            ResultsList.SelectionChanged += (_, _) => ShowButton.IsEnabled = ResultsList.SelectedItem is Row { IsPhoto: true };
            TrashButton.Click += async (_, _) => await TrashMarkedAsync();
            CloseButton.Click += (_, _) => Close();
            Closed += (_, _) => { _cts?.Cancel(); DisposeThumbs(); };
        }

        private int Threshold => ThresholdBox.SelectedIndex switch { 0 => SimilarPhotos.Strict, 2 => SimilarPhotos.Loose, _ => SimilarPhotos.Normal };

        private void Refresh()
        {
            ResultsList.ItemsSource = null;
            ResultsList.ItemsSource = _rows;
        }

        private void DisposeThumbs()
        {
            foreach (var r in _rows) r.Thumb?.Dispose();
        }

        private async Task FindAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            FindButton.IsVisible = false;
            StopButton.IsVisible = true;
            DisposeThumbs();
            _rows = new List<Row>();
            Refresh();
            bool subfolders = SubfoldersBox.IsChecked == true;
            int threshold = Threshold;
            var progress = new Progress<string>(s => StatusText.Text = s);
            List<List<PhotoPrint>> groups = new();
            int looked = 0;
            try
            {
                groups = await Task.Run(() =>
                {
                    var files = _picked.SelectMany(p => File.Exists(p) ? new List<string> { p } : SimilarPhotos.CollectPictures(p, subfolders, ct))
                                       .Distinct().ToList();
                    var prints = new System.Collections.Concurrent.ConcurrentBag<PhotoPrint>();
                    Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, f =>
                    {
                        if (SimilarPhotos.Fingerprint(f) is { } print) prints.Add(print);
                        int n = Interlocked.Increment(ref looked);
                        if (n % 25 == 0) ((IProgress<string>)progress).Report($"Looked at {n:N0} of {files.Count:N0} pictures…");
                    });
                    return SimilarPhotos.Group(prints.ToList(), threshold, ct);
                }, ct);
            }
            catch (OperationCanceledException) { StatusText.Text = "Stopped."; }

            var rows = new List<Row>();
            int shown = 0;
            foreach (var g in groups.Take(300))
            {
                rows.Add(new Row { Text = $"{g.Count} alike", Detail = ProgressFormat.Bytes(g.Skip(1).Sum(p => p.Bytes)) + " in the others" });
                bool first = true;
                foreach (var p in g)
                {
                    rows.Add(new Row
                    {
                        IsPhoto = true, IsBest = first, Path = p.Path, Tip = p.Path, Thumb = Thumb(p.Path),
                        Text = Relative(p.Path) + (first ? "   (best)" : ""),
                        Detail = $"{p.Width} × {p.Height} · {ProgressFormat.Bytes(p.Bytes)} · {p.Modified:d}",
                    });
                    first = false;
                    shown++;
                }
            }
            _rows = rows;
            Refresh();
            if (!ct.IsCancellationRequested)
                StatusText.Text = groups.Count == 0
                    ? $"No look-alike pictures among {looked:N0}."
                    : $"{groups.Count:N0} group(s) of look-alike pictures among {looked:N0}." + (groups.Count > 300 ? " The first 300 groups are shown." : "") +
                      " Mark the ones to remove (or Mark All but the Best), then Move Marked to Trash.";
            MarkButton.IsEnabled = ClearButton.IsEnabled = TrashButton.IsEnabled = groups.Count > 0;
            FindButton.IsVisible = true;
            StopButton.IsVisible = false;
        }

        private static Bitmap? Thumb(string path)
        {
            try
            {
                using var s = File.OpenRead(path);
                return Bitmap.DecodeToWidth(s, 96);
            }
            catch { return null; }
        }

        private string Relative(string path)
        {
            try { string r = System.IO.Path.GetRelativePath(_folder, path); return r.StartsWith("..") ? path : r; }
            catch { return path; }
        }

        private async Task TrashMarkedAsync()
        {
            var marked = _rows.Where(r => r.IsPhoto && r.IsMarked).Select(r => r.Path).ToList();
            if (marked.Count == 0) { StatusText.Text = "Nothing is marked."; return; }
            if (!await new MessageBox($"Move {marked.Count:N0} picture(s) to the trash? Ctrl/Cmd+Z in a panel brings them back.", true, "Move to Trash", "Cancel", "Find Similar Photos")
                    .ShowDialog<bool>(this)) return;
            int n = _trash != null ? await _trash(marked) : 0;
            foreach (var r in _rows.Where(r => marked.Contains(r.Path))) r.Thumb?.Dispose();
            _rows = _rows.Where(r => !marked.Contains(r.Path)).ToList();
            Refresh();
            StatusText.Text = $"{n:N0} picture(s) moved to the trash.";
        }
    }
}
