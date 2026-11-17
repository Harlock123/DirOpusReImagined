using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>PDF Tools (right-click → File Tools): merge, split, take out pages and turn pages, into new files. See <see cref="PdfTools"/>.</summary>
    public partial class PdfToolsWindow : Window
    {
        private readonly ObservableCollection<string> _files = new();
        private readonly string _folder = "";
        private readonly string? _other;
        private readonly Action<string>? _written;

        public PdfToolsWindow() { InitializeComponent(); }

        public PdfToolsWindow(IReadOnlyList<string> files, string folder, string? other, Action<string> written) : this()
        {
            foreach (var f in files) _files.Add(f);
            _folder = folder;
            _other = other;
            _written = written;
            FileList.ItemsSource = _files;
            FileList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((p, _) => new TextBlock { Text = Path.GetFileName(p) });
            FileList.SelectedIndex = 0;
            OtherPanelRadio.IsEnabled = other != null;
            ActionBox.SelectedIndex = files.Count > 1 ? 0 : 2;
            ActionBox.SelectionChanged += (_, _) => Update();
            UpButton.Click += (_, _) => MoveSelected(-1);
            DownButton.Click += (_, _) => MoveSelected(+1);
            StartButton.Click += async (_, _) => await RunAsync();
            CloseButton.Click += (_, _) => Close();
            Update();
        }

        private void MoveSelected(int delta)
        {
            int i = FileList.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= _files.Count) return;
            (_files[i], _files[j]) = (_files[j], _files[i]);
            FileList.SelectedIndex = j;
        }

        private int Action => ActionBox.SelectedIndex;

        private void Update()
        {
            bool merge = Action == 0, split = Action == 1, pages = Action is 2 or 3;
            UpButton.IsVisible = DownButton.IsVisible = merge;
            PagesLabel.IsVisible = PagesBox.IsVisible = pages;
            EachLabel.IsVisible = EachBox.IsVisible = split;
            TurnLabel.IsVisible = TurnBox.IsVisible = Action == 3;
            NameLabel.IsVisible = NameBox.IsVisible = !split;
            string first = _files.Count > 0 ? Path.GetFileNameWithoutExtension(_files[0]) : "document";
            NameBox.Text = Action switch
            {
                0 => "Merged.pdf",
                2 => first + " (pages).pdf",
                3 => first + " (turned).pdf",
                _ => NameBox.Text,
            };
        }

        private string Folder(string source) => OtherPanelRadio.IsChecked == true && _other != null ? _other : Path.GetDirectoryName(source) ?? _folder;

        private async Task RunAsync()
        {
            if (_files.Count == 0) return;
            string source = FileList.SelectedItem as string ?? _files[0];
            string folder = Folder(source);
            string name = (NameBox.Text ?? "").Trim();
            if (Action != 1)
            {
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { StatusText.Text = "Type a name for the new file."; return; }
                if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
            }
            int each = 1;
            if (Action == 1 && (!int.TryParse(EachBox.Text, out each) || each < 1)) { StatusText.Text = "Pages in each part should be a number, 1 or more."; return; }
            string ranges = PagesBox.Text ?? "";
            int degrees = TurnBox.SelectedIndex switch { 1 => 180, 2 => 270, _ => 90 };
            int action = Action;
            var files = _files.ToList();

            StartButton.IsEnabled = false;
            StatusText.Text = "Working…";
            try
            {
                string result = await Task.Run(() =>
                {
                    bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
                    string target = PdfTools.Unique(Path.Combine(folder, name), Exists);
                    switch (action)
                    {
                        case 0:
                            int pages = PdfTools.Merge(files, target);
                            return $"Merged {files.Count} PDFs ({pages} pages) into {Path.GetFileName(target)}.";
                        case 1:
                            var made = PdfTools.Split(source, each, folder, Exists);
                            return $"Split {Path.GetFileName(source)} into {made.Count} file(s).";
                        case 2:
                        {
                            var list = PdfTools.ParseRanges(ranges, PdfTools.PageCount(source));
                            PdfTools.Extract(source, list, target);
                            return $"Took out {list.Count} page(s) into {Path.GetFileName(target)}.";
                        }
                        default:
                        {
                            var list = PdfTools.ParseRanges(ranges, PdfTools.PageCount(source));
                            int n = PdfTools.Rotate(source, list, degrees, target);
                            return $"Turned {n} page(s); saved as {Path.GetFileName(target)}.";
                        }
                    }
                });
                StatusText.Text = result;
                _written?.Invoke(folder);
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            finally { StartButton.IsEnabled = true; }
        }
    }
}
