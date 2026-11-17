using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// The system trash (right-click → File Tools → Trash…): what's in it, putting items back,
    /// deleting them for good and emptying it. See <see cref="TrashBrowser"/>.
    /// </summary>
    public partial class TrashWindow : Window
    {
        public sealed class Row
        {
            public TrashEntry Entry { get; init; } = null!;
            public string Name => Entry.Name + (Entry.IsDirectory ? "/" : "");
            public string From => Entry.CanRestore ? Entry.OriginalFolder : "(not recorded)";
            public string When => Entry.Deleted == DateTime.MinValue ? "" : Entry.Deleted.ToString("g");
            public string SizeText => Entry.IsDirectory ? "folder" : ProgressFormat.Bytes(Entry.Size);
            public string Tip => Entry.CanRestore ? Entry.OriginalPath : Entry.TrashedPath;
        }

        private readonly Action<string>? _restored;
        private readonly Action<string>? _show;
        private List<TrashEntry> _all = new();
        private readonly ObservableCollection<Row> _rows = new();

        public TrashWindow()
        {
            InitializeComponent();
        }

        /// <param name="restored">Called with each folder something was put back into, so panels showing it refresh.</param>
        /// <param name="show">Opens a folder in the panel.</param>
        public TrashWindow(Action<string>? restored, Action<string>? show) : this()
        {
            _restored = restored;
            _show = show;
            ItemsList.ItemsSource = _rows;
            ItemsList.SelectionChanged += (_, _) => UpdateButtons();
            ItemsList.DoubleTapped += (_, _) => ShowOriginal();
            FilterBox.TextChanged += (_, _) => Fill();
            RestoreButton.Click += async (_, _) => await RestoreAsync();
            ShowButton.Click += (_, _) => ShowOriginal();
            DeleteButton.Click += async (_, _) => await DeleteAsync();
            EmptyButton.Click += async (_, _) => await EmptyAsync();
            RefreshButton.Click += async (_, _) => await LoadAsync();
            CloseButton.Click += (_, _) => Close();
            if (OperatingSystem.IsMacOS())
                StatusText.Text = "macOS keeps where trashed items came from in Finder's own records, so Put Back is done from the Trash in Finder.";
            Opened += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            SummaryText.Text = "Reading the trash…";
            _all = await Task.Run(TrashBrowser.List);
            Fill();
        }

        private void Fill()
        {
            string filter = (FilterBox.Text ?? "").Trim();
            _rows.Clear();
            foreach (var e in _all)
            {
                if (filter.Length > 0 && e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                                      && e.OriginalPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _rows.Add(new Row { Entry = e });
            }
            long bytes = _all.Where(e => !e.IsDirectory).Sum(e => Math.Max(0, e.Size));
            int folders = _all.Count(e => e.IsDirectory);
            SummaryText.Text = _all.Count == 0
                ? "The trash is empty."
                : $"{_all.Count:N0} item(s) in the trash · {ProgressFormat.Bytes(bytes)} in files" +
                  (folders > 0 ? $" and {folders:N0} folder(s)" : "") +
                  (_rows.Count != _all.Count ? $" · {_rows.Count:N0} shown" : "");
            EmptyButton.IsEnabled = _all.Count > 0;
            UpdateButtons();
        }

        private List<TrashEntry> Selected() => ItemsList.SelectedItems?.OfType<Row>().Select(r => r.Entry).ToList() ?? new();

        private void UpdateButtons()
        {
            var sel = Selected();
            RestoreButton.IsEnabled = sel.Any(e => e.CanRestore);
            ShowButton.IsEnabled = sel.Count == 1 && sel[0].CanRestore;
            DeleteButton.IsEnabled = sel.Count > 0;
        }

        private void ShowOriginal()
        {
            var sel = Selected();
            if (sel.Count != 1 || !sel[0].CanRestore) return;
            string folder = sel[0].OriginalFolder;
            if (Directory.Exists(folder)) _show?.Invoke(folder);
            else StatusText.Text = $"{folder} isn't there any more. Put Back makes it again.";
        }

        private async Task RestoreAsync()
        {
            var sel = Selected().Where(e => e.CanRestore).ToList();
            var problems = new List<string>();
            var folders = new HashSet<string>();
            await Task.Run(() =>
            {
                foreach (var e in sel)
                {
                    try
                    {
                        TrashService.Restore(e.ToItem());
                        folders.Add(e.OriginalFolder);
                    }
                    catch (Exception ex) { problems.Add($"{e.Name}: {ex.Message}"); }
                }
            });
            foreach (var f in folders) _restored?.Invoke(f);
            await LoadAsync();
            StatusText.Text = $"Put back {sel.Count - problems.Count:N0} item(s)." +
                              (problems.Count > 0 ? " Not put back: " + string.Join("; ", problems.Take(5)) + (problems.Count > 5 ? "…" : "") : "");
        }

        private async Task DeleteAsync()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            string what = sel.Count == 1 ? $"\"{sel[0].Name}\"" : $"{sel.Count:N0} items";
            bool ok = await new MessageBox($"Delete {what} for good? This can't be undone.", true, "Delete", "Cancel", "Trash").ShowDialog<bool>(this);
            if (!ok) return;
            var problems = new List<string>();
            await Task.Run(() =>
            {
                foreach (var e in sel)
                {
                    try { TrashBrowser.DeleteForever(e); }
                    catch (Exception ex) { problems.Add($"{e.Name}: {ex.Message}"); }
                }
            });
            await LoadAsync();
            StatusText.Text = $"Deleted {sel.Count - problems.Count:N0} item(s) for good." +
                              (problems.Count > 0 ? " Not deleted: " + string.Join("; ", problems.Take(5)) : "");
        }

        private async Task EmptyAsync()
        {
            if (_all.Count == 0) return;
            bool ok = await new MessageBox($"Empty the trash? All {_all.Count:N0} item(s) are deleted for good. This can't be undone.",
                                           true, "Empty Trash", "Cancel", "Trash").ShowDialog<bool>(this);
            if (!ok) return;
            var items = _all.ToList();
            SummaryText.Text = "Emptying…";
            var failed = await Task.Run(() => TrashBrowser.Empty(items));
            await LoadAsync();
            StatusText.Text = failed.Count == 0 ? "The trash was emptied." : "Some items couldn't be deleted: " + string.Join("; ", failed.Take(5));
        }
    }
}
