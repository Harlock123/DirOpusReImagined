using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Convert Audio and Video (right-click → File Tools): picks a preset and a part to keep, then queues one ffmpeg run per file. See <see cref="MediaConvert"/>.</summary>
    public partial class MediaToolsWindow : Window
    {
        private readonly List<string> _files = new();
        private readonly string _folder = "";
        private readonly string? _otherFolder;
        private readonly Action<List<MediaJob>>? _run;

        public MediaToolsWindow()
        {
            InitializeComponent();
        }

        /// <param name="run">Queues the conversions.</param>
        public MediaToolsWindow(IReadOnlyList<string> files, string folder, string? otherFolder, Action<List<MediaJob>>? run) : this()
        {
            _files.AddRange(files);
            _folder = folder;
            _otherFolder = otherFolder;
            _run = run;
            ScopeText.Text = files.Count == 1 ? $"File: {Path.GetFileName(files[0])}" : $"{files.Count} files in {folder}";
            PresetBox.ItemsSource = MediaConvert.Presets.Select(p => p.Name).ToList();
            PresetBox.SelectedIndex = 0;
            OtherPanelRadio.IsEnabled = otherFolder != null;
            ToolTip.SetTip(OtherPanelRadio, otherFolder ?? "The other panel isn't showing a folder on this computer");
            PresetBox.SelectionChanged += (_, _) => Update();
            OtherPanelRadio.IsCheckedChanged += (_, _) => Update();
            StartButton.Click += (_, _) => Start();
            CloseButton.Click += (_, _) => Close();
            Update();
        }

        private MediaPreset Preset => MediaConvert.Presets[Math.Max(0, PresetBox.SelectedIndex)];

        private string TargetFolder(string source)
            => OtherPanelRadio.IsChecked == true && _otherFolder != null ? _otherFolder : Path.GetDirectoryName(source) ?? _folder;

        private void Update()
        {
            var preset = Preset;
            HeightBox.IsEnabled = SizeLabel.IsEnabled = preset.CanResize;
            if (_files.Count > 0)
                ExampleText.Text = $"For example: {Path.GetFileName(_files[0])} → {Path.GetFileName(MediaConvert.TargetFor(_files[0], TargetFolder(_files[0]), preset))}";
        }

        private void Start()
        {
            var start = MediaConvert.ParseTime(StartBox.Text);
            var end = MediaConvert.ParseTime(EndBox.Text);
            if ((StartBox.Text ?? "").Trim().Length > 0 && start == null) { StatusText.Text = "The start isn't a time. Try 1:30 or 90."; return; }
            if ((EndBox.Text ?? "").Trim().Length > 0 && end == null) { StatusText.Text = "The end isn't a time. Try 2:45."; return; }
            if (start != null && end != null && end <= start) { StatusText.Text = "The end has to come after the start."; return; }
            var preset = Preset;
            if (preset.Extension.Length == 0 && start == null && end == null)
            {
                StatusText.Text = "\"Only cut\" needs a start or an end.";
                return;
            }
            int? height = preset.CanResize && HeightBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int h) ? h : null;

            // Each target is chosen with the earlier ones counted as taken, so two files never get the same name.
            var taken = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var jobs = new List<MediaJob>();
            foreach (var f in _files)
            {
                string target = MediaConvert.TargetFor(f, TargetFolder(f), preset, p => taken.Contains(p) || File.Exists(p) || Directory.Exists(p));
                taken.Add(target);
                jobs.Add(new MediaJob(f, target, preset, start, end, height));
            }
            _run?.Invoke(jobs);
            Close();
        }
    }
}
