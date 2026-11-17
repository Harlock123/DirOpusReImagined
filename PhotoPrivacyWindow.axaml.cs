using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem.Preview;

namespace DirOpusReImagined
{
    /// <summary>Remove Photo Details (right-click → File Tools): what each photo gives away, and taking it out. See <see cref="PhotoPrivacy"/>.</summary>
    public partial class PhotoPrivacyWindow : Window
    {
        public sealed record Row(string Name, string Details, string Tip);

        private readonly List<string> _files = new();
        private readonly string _folder = "";
        private readonly string? _other;
        private readonly Action<string>? _written;

        public PhotoPrivacyWindow() { InitializeComponent(); }

        public PhotoPrivacyWindow(IReadOnlyList<string> files, string folder, string? other, Action<string> written) : this()
        {
            _files.AddRange(files);
            _folder = folder;
            _other = other;
            _written = written;
            ScopeText.Text = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} photos in {folder}";
            OtherRadio.IsEnabled = other != null;
            StartButton.Click += async (_, _) => await RunAsync();
            CloseButton.Click += (_, _) => Close();
            Opened += async (_, _) => await DescribeAsync();
        }

        /// <summary>"Location 51.5° N, 0.1° W · Camera Pixel 7 · Taken 2024-03-01" — what the photo carries.</summary>
        public static string Describe(PhotoDetails? d)
        {
            if (d == null || d.Fields.Count == 0) return "no details";
            var wanted = new[] { "Location", "Camera", "Lens", "Taken", "Software", "Artist" };
            var shown = d.Fields.Where(f => wanted.Contains(f.Label)).Select(f => $"{f.Label}: {f.Value}").ToList();
            return shown.Count > 0 ? string.Join(" · ", shown) : "camera settings only";
        }

        private async Task DescribeAsync()
        {
            var rows = await Task.Run(() => _files.Select(f =>
            {
                PhotoDetails? d = null;
                try
                {
                    using var s = File.OpenRead(f);
                    var head = new byte[Math.Min(s.Length, ExifReader.ProbeBytes)];
                    s.ReadExactly(head);
                    d = ExifReader.Read(head);
                }
                catch { }
                return new Row(Path.GetFileName(f), Describe(d), f);
            }).ToList());
            FileList.ItemsSource = rows;
            int located = rows.Count(r => r.Details.Contains("Location:"));
            StatusText.Text = (located > 0 ? $"{located} of {rows.Count} photo(s) say where they were taken. " : "") +
                              "The picture itself is copied byte for byte, so nothing is lost in quality. PNG and WebP files lose all their details either way.";
        }

        private async Task RunAsync()
        {
            var mode = AllRadio.IsChecked == true ? StripMode.All : StripMode.Location;
            bool replace = ReplaceRadio.IsChecked == true, other = OtherRadio.IsChecked == true && _other != null;
            if (replace && !await new MessageBox($"Change the {_files.Count} original(s)? The details taken out can't be put back.", true, "Change Them", "Cancel", "Remove Photo Details").ShowDialog<bool>(this))
                return;
            StartButton.IsEnabled = false;
            var problems = new List<string>();
            var folders = new HashSet<string>();
            int done = 0;
            await Task.Run(() =>
            {
                foreach (var f in _files)
                {
                    try
                    {
                        byte[] clean = PhotoPrivacy.Strip(File.ReadAllBytes(f), mode);
                        string target = replace ? f
                            : other ? Path.Combine(_other!, Path.GetFileName(f))
                            : PhotoPrivacy.CleanName(f, Path.GetDirectoryName(f)!, p => File.Exists(p));
                        if (!replace && File.Exists(target)) { problems.Add($"{Path.GetFileName(f)}: {Path.GetFileName(target)} is already there"); continue; }
                        string temp = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + ".dori-part");
                        File.WriteAllBytes(temp, clean);
                        File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(f));
                        File.Move(temp, target, overwrite: replace);
                        folders.Add(Path.GetDirectoryName(target)!);
                        done++;
                    }
                    catch (Exception ex) { problems.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
                }
            });
            foreach (var folder in folders) _written?.Invoke(folder);
            if (replace) await DescribeAsync();
            StatusText.Text = $"{done} photo(s) done." + (problems.Count > 0 ? " Not done: " + string.Join("; ", problems.Take(5)) : "");
            StartButton.IsEnabled = true;
        }
    }
}
