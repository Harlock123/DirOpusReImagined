using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Shows two files side by side with the differences coloured: changed lines amber, removed
    /// lines red on the left, added lines green on the right. Files that aren't text, or are very
    /// large, are compared byte by byte instead. Works for local, archive and cloud files alike.
    /// </summary>
    public partial class FileCompareWindow : Window
    {
        /// <summary>One row as the list shows it.</summary>
        public sealed class RowView
        {
            public string LeftNo { get; init; } = "";
            public string LeftText { get; init; } = "";
            public string RightNo { get; init; } = "";
            public string RightText { get; init; } = "";
            public IBrush? LeftBackground { get; init; }
            public IBrush? RightBackground { get; init; }
        }

        // Translucent, so they read on light and dark themes alike.
        private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.FromArgb(70, 230, 170, 30));
        private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromArgb(70, 230, 60, 60));
        private static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromArgb(70, 60, 190, 80));
        private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128));

        private string _left = "", _right = "";
        private List<string>? _leftLines, _rightLines;
        private List<int> _changeStarts = new();
        private int _changeIndex = -1;
        private string _summary = "";

        public FileCompareWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public FileCompareWindow(string left, string right) : this()
        {
            _left = left;
            _right = right;
            this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
            this.FindControl<Button>("NextButton")!.Click += (_, _) => GoToChange(+1);
            this.FindControl<Button>("PrevButton")!.Click += (_, _) => GoToChange(-1);
            this.FindControl<Button>("SwapButton")!.Click += (_, _) =>
            {
                (_left, _right) = (_right, _left);
                (_leftLines, _rightLines) = (_rightLines, _leftLines);
                _ = LoadAsync();
            };
            this.FindControl<CheckBox>("IgnoreWhitespaceBox")!.IsCheckedChanged += (_, _) => _ = LoadAsync();
            Opened += (_, _) => _ = LoadAsync();
        }

        private bool IgnoreWhitespace => this.FindControl<CheckBox>("IgnoreWhitespaceBox")!.IsChecked == true;

        private async Task LoadAsync()
        {
            var summary = this.FindControl<TextBlock>("SummaryText")!;
            var list = this.FindControl<ListBox>("RowsList")!;
            this.FindControl<TextBlock>("LeftHeader")!.Text = _left;
            this.FindControl<TextBlock>("RightHeader")!.Text = _right;
            Title = $"Compare — {ConflictResolver.LeafOf(_left)} ↔ {ConflictResolver.LeafOf(_right)}";
            summary.Text = "Comparing…";
            bool ignoreWs = IgnoreWhitespace;

            try
            {
                var result = await Task.Run(() => Compare(ignoreWs));
                if (result.Rows == null)
                {
                    list.ItemsSource = null;
                    summary.Text = _summary = result.Summary;
                    _changeStarts = new();
                }
                else
                {
                    list.ItemsSource = result.Rows.Select(ToView).ToList();
                    summary.Text = _summary = result.Summary;
                    _changeStarts = TextDiff.ChangeStarts(result.Rows);
                }
                _changeIndex = -1;
                this.FindControl<Button>("NextButton")!.IsEnabled = _changeStarts.Count > 0;
                this.FindControl<Button>("PrevButton")!.IsEnabled = _changeStarts.Count > 0;
                if (_changeStarts.Count > 0) GoToChange(+1);
            }
            catch (Exception ex)
            {
                summary.Text = "Couldn't compare: " + ex.Message;
            }
        }

        private (List<DiffRow>? Rows, string Summary) Compare(bool ignoreWhitespace)
        {
            if (_leftLines == null || _rightLines == null)
            {
                byte[]? a = ReadUpTo(_left, TextDiff.MaxTextBytes);
                byte[]? b = ReadUpTo(_right, TextDiff.MaxTextBytes);
                if (a == null || b == null || TextDiff.LooksBinary(a) || TextDiff.LooksBinary(b))
                    return (null, CompareBytes());
                _leftLines = TextDiff.SplitLines(Decode(a));
                _rightLines = TextDiff.SplitLines(Decode(b));
            }
            var rows = TextDiff.Compare(_leftLines, _rightLines, ignoreWhitespace);
            return (rows, TextDiff.Summary(rows, ignoreWhitespace));
        }

        /// <summary>For binary or very large files: are they identical, and if not where do they first differ.</summary>
        private string CompareBytes()
        {
            using var a = ProviderRegistry.For(_left).OpenRead(_left);
            using var b = ProviderRegistry.For(_right).OpenRead(_right);
            long? at = TextDiff.FirstDifference(a, b);
            return at == null
                ? "These aren't text files (or are too large to show line by line). Their contents are identical."
                : $"These aren't text files (or are too large to show line by line). They first differ at byte {at:N0} (0x{at:X}).";
        }

        /// <summary>The whole file, or null if it is larger than <paramref name="limit"/>.</summary>
        private static byte[]? ReadUpTo(string path, long limit)
        {
            using var s = ProviderRegistry.For(path).OpenRead(path);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, n);
                if (ms.Length > limit) return null;
            }
            return ms.ToArray();
        }

        private static string Decode(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static RowView ToView(DiffRow r) => new()
        {
            LeftNo = r.LeftNo?.ToString() ?? "",
            LeftText = Tabs(r.LeftText),
            RightNo = r.RightNo?.ToString() ?? "",
            RightText = Tabs(r.RightText),
            LeftBackground = r.Kind switch
            {
                DiffKind.Changed => ChangedBrush, DiffKind.Removed => RemovedBrush, DiffKind.Added => EmptyBrush, _ => null,
            },
            RightBackground = r.Kind switch
            {
                DiffKind.Changed => ChangedBrush, DiffKind.Added => AddedBrush, DiffKind.Removed => EmptyBrush, _ => null,
            },
        };

        private static string Tabs(string s) => s.Replace("\t", "    ");

        private void GoToChange(int delta)
        {
            if (_changeStarts.Count == 0) return;
            _changeIndex = _changeIndex < 0 && delta < 0
                ? _changeStarts.Count - 1
                : ((_changeIndex + delta) % _changeStarts.Count + _changeStarts.Count) % _changeStarts.Count;
            var list = this.FindControl<ListBox>("RowsList")!;
            int row = _changeStarts[_changeIndex];
            // Scrolled to, not selected: the selection colour would hide the difference colours.
            list.ScrollIntoView(Math.Min(row + 8, list.ItemCount - 1));   // a little of what follows, too
            list.ScrollIntoView(row);
            this.FindControl<TextBlock>("SummaryText")!.Text = _summary + $"  (change {_changeIndex + 1} of {_changeStarts.Count})";
        }
    }
}
