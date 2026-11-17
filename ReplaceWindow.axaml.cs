using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace DirOpusReImagined
{
    /// <summary>
    /// Find and Replace in Files (right-click → File Tools, or Ctrl/Cmd+Shift+H): previews every
    /// changed line, then changes only the ticked files. See <see cref="ReplaceInFiles"/>.
    /// </summary>
    public partial class ReplaceWindow : Window
    {
        /// <summary>One line of the results: a file (with its tick) or one of its changed lines.</summary>
        public sealed class Row
        {
            public bool IsFile { get; init; }
            public bool IsMarked { get; set; } = true;
            public string Text { get; init; } = "";
            public string Detail { get; init; } = "";
            public string Tip { get; init; } = "";
            public FontWeight Weight => IsFile ? FontWeight.Bold : FontWeight.Normal;
            public FontFamily Font => IsFile ? FontFamily.Default : new FontFamily("Cascadia Mono,Consolas,Menlo,monospace");
            public string Path { get; init; } = "";
        }

        private readonly List<string> _picked = new();
        private readonly string _folder = "";
        private readonly Func<string, List<ReplacedFile>, Task>? _done;
        private readonly ObservableCollection<Row> _rows = new();
        private CancellationTokenSource? _cts;
        private ReplaceOptions? _previewed;

        public ReplaceWindow()
        {
            InitializeComponent();
        }

        /// <param name="picked">The files and folders to look in.</param>
        /// <param name="done">Called with the folder and the files changed, for undo and refreshing panels.</param>
        public ReplaceWindow(IReadOnlyList<string> picked, string folder, Func<string, List<ReplacedFile>, Task>? done) : this()
        {
            _picked.AddRange(picked);
            _folder = folder;
            _done = done;
            ScopeText.Text = picked.Count == 1
                ? $"Look in: {picked[0]}"
                : $"Look in {picked.Count} items in {folder}";
            ResultsList.ItemsSource = _rows;
            PreviewButton.Click += async (_, _) => await PreviewAsync();
            StopButton.Click += (_, _) => _cts?.Cancel();
            ReplaceButton.Click += async (_, _) => await ReplaceAsync();
            CloseButton.Click += (_, _) => Close();
            Closed += (_, _) => _cts?.Cancel();
            // Anything changed after a preview means the preview no longer says what Replace would do.
            foreach (var box in new[] { FindBox, ReplaceBox, NamesBox }) box.TextChanged += (_, _) => Stale();
            foreach (var check in new[] { MatchCaseBox, WholeWordBox, RegexBox, SubfoldersBox }) check.IsCheckedChanged += (_, _) => Stale();
            Opened += (_, _) => FindBox.Focus();
        }

        private ReplaceOptions Options() => new(FindBox.Text ?? "", ReplaceBox.Text ?? "", RegexBox.IsChecked == true,
                                                MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true);

        private void Stale()
        {
            if (_previewed == null) return;
            _previewed = null;
            ReplaceButton.IsEnabled = false;
            StatusText.Text = "Something changed since the preview. Preview again to see what Replace would do.";
        }

        private void Busy(bool busy)
        {
            PreviewButton.IsVisible = !busy;
            StopButton.IsVisible = busy;
            ReplaceButton.IsEnabled = !busy && _previewed != null && _rows.Any(r => r.IsFile);
        }

        private async Task PreviewAsync()
        {
            var options = Options();
            Regex rx;
            try { rx = ReplaceInFiles.MakeRegex(options); }
            catch (ArgumentException ex)
            {
                StatusText.Text = string.IsNullOrEmpty(options.Find) ? "Type what to find first." : "That pattern isn't valid: " + ex.Message;
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _rows.Clear();
            _previewed = null;
            Busy(true);
            StatusText.Text = "Looking…";
            string names = NamesBox.Text ?? "";
            bool subfolders = SubfoldersBox.IsChecked == true;

            var found = new List<FileReplacement>();
            int read = 0, skipped = 0;
            string? failure = null;
            try
            {
                await Task.Run(() =>
                {
                    var files = ReplaceInFiles.CollectFiles(_picked, names, subfolders, ct);
                    foreach (var f in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var r = ReplaceInFiles.Preview(f, rx, options);
                            read++;
                            if (r != null) lock (found) found.Add(r);
                        }
                        catch (RegexMatchTimeoutException) { throw; }
                        catch { skipped++; }
                    }
                }, ct);
            }
            catch (OperationCanceledException) { failure = "Stopped."; }
            catch (RegexMatchTimeoutException) { failure = "The pattern took too long on one of the files. Try a simpler one."; }
            catch (Exception ex) { failure = ex.Message; }

            const int maxRows = 5000;
            foreach (var f in found)
            {
                if (_rows.Count >= maxRows) break;
                string rel = Relative(f.Path);
                _rows.Add(new Row
                {
                    IsFile = true, Path = f.Path, Text = rel, Tip = f.Path,
                    Detail = f.Count == 1 ? "1 change" : $"{f.Count:N0} changes",
                });
                foreach (var line in f.Lines)
                {
                    if (_rows.Count >= maxRows) break;
                    _rows.Add(new Row
                    {
                        Text = $"{line.Line,6}  {Shorten(line.Before)}",
                        Detail = "→  " + Shorten(line.After, 70),
                        Tip = $"Line {line.Line}\nNow:   {line.Before}\nAfter: {line.After}",
                    });
                }
            }

            int changes = found.Sum(f => f.Count);
            if (failure == null)
            {
                _previewed = options;
            }
            StatusText.Text = (failure != null ? failure + " " : "") +
                (found.Count == 0
                    ? $"Nothing to replace in {read:N0} file(s)."
                    : $"{changes:N0} change(s) in {found.Count:N0} of {read:N0} file(s). Untick any file to leave it as it is.") +
                (skipped > 0 ? $" {skipped:N0} couldn't be read." : "") +
                (_rows.Count >= maxRows ? " (Only the first lines are shown; all ticked files are still changed.)" : "");
            Busy(false);
        }

        private async Task ReplaceAsync()
        {
            if (_previewed == null) return;
            var options = _previewed;
            var marked = _rows.Where(r => r.IsFile && r.IsMarked).Select(r => r.Path).ToList();
            if (marked.Count == 0)
            {
                StatusText.Text = "No files are ticked.";
                return;
            }
            var rx = ReplaceInFiles.MakeRegex(options);
            string backups = Path.Combine(Path.GetTempPath(), "dori-undo", "replace-" + Guid.NewGuid().ToString("N")[..12]);
            var changed = new List<ReplacedFile>();
            var problems = new List<string>();
            int total = 0;
            Busy(true);
            ReplaceButton.IsEnabled = false;
            StatusText.Text = "Replacing…";
            await Task.Run(() =>
            {
                foreach (var path in marked)
                {
                    try
                    {
                        var done = ReplaceInFiles.ReplaceFile(path, rx, options, backups, out int count);
                        if (done != null) { changed.Add(done); total += count; }
                    }
                    catch (Exception ex) { problems.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
                }
            });
            _previewed = null;
            Busy(false);
            ReplaceButton.IsEnabled = false;
            _rows.Clear();
            StatusText.Text = $"Replaced {total:N0} time(s) in {changed.Count:N0} file(s)." +
                              (changed.Count > 0 ? " Ctrl/Cmd+Z in a panel puts them back." : "") +
                              (problems.Count > 0 ? "\nNot changed: " + string.Join("; ", problems.Take(5)) + (problems.Count > 5 ? "…" : "") : "");
            if (changed.Count > 0 && _done != null) await _done(_folder, changed);
        }

        private string Relative(string path)
        {
            try
            {
                string rel = Path.GetRelativePath(_folder, path);
                return rel.StartsWith("..") ? path : rel;
            }
            catch { return path; }
        }

        private static string Shorten(string s, int max = 110)
        {
            s = s.Replace('\t', ' ').Trim();
            return s.Length <= max ? s : s[..(max - 1)] + "…";
        }
    }
}
