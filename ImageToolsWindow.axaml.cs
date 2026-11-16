using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined
{
    /// <summary>Image Tools (right-click → File Tools → Image Tools…): resize, convert and turn the selected pictures. See <see cref="ImageTools"/>.</summary>
    public partial class ImageToolsWindow : Window
    {
        private readonly List<string> _files = new();
        private readonly string _folder = "";
        private readonly string? _otherFolder;
        private readonly Action<string>? _done;
        private CancellationTokenSource? _cts;

        public ImageToolsWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <param name="done">Called with the folder written to, so the panels showing it can refresh.</param>
        public ImageToolsWindow(IReadOnlyList<string> files, string folder, string? otherFolder, Action<string>? done) : this()
        {
            _files.AddRange(files);
            _folder = folder;
            _otherFolder = otherFolder;
            _done = done;

            this.FindControl<TextBlock>("ScopeText")!.Text = files.Count == 1
                ? $"Picture: {Path.GetFileName(files[0])}"
                : $"{files.Count} pictures in {folder}";
            var other = this.FindControl<RadioButton>("OtherPanelRadio")!;
            other.IsEnabled = otherFolder != null;
            if (otherFolder != null) ToolTip.SetTip(other, otherFolder);
            else ToolTip.SetTip(other, "The other panel isn't showing a folder on this computer");

            other.IsCheckedChanged += (_, _) =>
            {
                // Saving elsewhere usually wants the same names; beside the originals they must differ.
                var suffix = this.FindControl<TextBox>("SuffixBox")!;
                if (other.IsChecked == true && suffix.Text == "_edited") suffix.Text = "";
                else if (other.IsChecked != true && string.IsNullOrEmpty(suffix.Text)) suffix.Text = "_edited";
                UpdateExample();
            };
            this.FindControl<TextBox>("SuffixBox")!.TextChanged += (_, _) => UpdateExample();
            this.FindControl<ComboBox>("FormatBox")!.SelectionChanged += (_, _) => UpdateExample();
            this.FindControl<Button>("StartButton")!.Click += async (_, _) => await RunAsync();
            this.FindControl<Button>("StopButton")!.Click += (_, _) => _cts?.Cancel();
            this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
            Closed += (_, _) => _cts?.Cancel();
            UpdateExample();
        }

        private void UpdateExample()
        {
            var example = this.FindControl<TextBlock>("ExampleText");
            if (example == null || _files.Count == 0) return;
            if (TryReadJob(out var job, out _)) example.Text = "→ " + ImageTools.OutputName(_files[0], job);
        }

        private bool TryReadJob(out ImageJob job, out string error)
        {
            job = null!;
            error = "";
            static bool Size(string? text, out int value)
            {
                value = 0;
                string t = (text ?? "").Trim();
                return t.Length == 0 || (int.TryParse(t, out value) && value > 0);
            }
            if (!Size(this.FindControl<TextBox>("MaxWidthBox")!.Text, out int w) ||
                !Size(this.FindControl<TextBox>("MaxHeightBox")!.Text, out int h))
            {
                error = "The width and height are whole numbers of pixels, or empty.";
                return false;
            }
            if (!int.TryParse((this.FindControl<TextBox>("QualityBox")!.Text ?? "").Trim(), out int quality) || quality < 1 || quality > 100)
            {
                error = "The quality is a number from 1 to 100.";
                return false;
            }
            var format = (ImageFormatChoice)Math.Max(0, this.FindControl<ComboBox>("FormatBox")!.SelectedIndex);
            int turn = Math.Max(0, this.FindControl<ComboBox>("TurnBox")!.SelectedIndex) * 90;
            bool upright = this.FindControl<CheckBox>("UprightBox")!.IsChecked == true;
            string suffix = this.FindControl<TextBox>("SuffixBox")!.Text ?? "";
            if (suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || suffix.Contains('/') || suffix.Contains('\\'))
            {
                error = "What's added to the names can't contain / or \\.";
                return false;
            }
            job = new ImageJob(w, h, turn, upright, format, quality, suffix);
            return true;
        }

        private async Task RunAsync()
        {
            var status = this.FindControl<TextBlock>("StatusText")!;
            if (!TryReadJob(out var job, out string error)) { status.Text = error; return; }
            string target = this.FindControl<RadioButton>("OtherPanelRadio")!.IsChecked == true && _otherFolder != null ? _otherFolder : _folder;

            var cts = _cts = new CancellationTokenSource();
            var start = this.FindControl<Button>("StartButton")!;
            var stop = this.FindControl<Button>("StopButton")!;
            start.IsEnabled = false;
            stop.IsVisible = true;
            var problems = new List<string>();
            int made = 0;
            var progress = new Progress<int>(i => status.Text = $"Working… {i} of {_files.Count}");
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < _files.Count; i++)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        ((IProgress<int>)progress).Report(i + 1);
                        try { ImageTools.Process(_files[i], target, job); made++; }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            problems.Add($"{Path.GetFileName(_files[i])}: {ex.Message}");
                        }
                    }
                }, cts.Token);
            }
            catch (OperationCanceledException) { problems.Insert(0, "Stopped before the end."); }
            finally
            {
                start.IsEnabled = true;
                stop.IsVisible = false;
            }

            _done?.Invoke(target);
            status.Text = $"Made {made} of {_files.Count}." + (problems.Count == 0 ? "" :
                "\n" + string.Join("\n", problems.Take(8)) + (problems.Count > 8 ? $"\n…and {problems.Count - 8} more" : ""));
        }
    }
}
