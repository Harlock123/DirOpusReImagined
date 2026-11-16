using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Making, browsing, searching and updating disk catalogues (see <see cref="Catalogue"/>).</summary>
    public partial class CataloguesWindow : Window
    {
        public sealed class CatalogueRow
        {
            public Catalogue Catalogue { get; init; } = null!;
            public string Title => Catalogue.Name;
            public string Detail => $"{Catalogue.FileCount:N0} files · {ProgressFormat.Bytes(Catalogue.TotalBytes)} · {Catalogue.Made:d}";
        }

        public sealed class EntryRow
        {
            public Catalogue Catalogue { get; init; } = null!;
            public CatalogueEntry? Entry { get; init; }   // null for the "found nothing" line
            public string Name { get; init; } = "";
            public string SizeText { get; init; } = "";
            public string DateText { get; init; } = "";
            public string? Tip { get; init; }
            public FontWeight Weight => Entry?.IsDirectory == true ? FontWeight.Bold : FontWeight.Normal;
        }

        private readonly Func<string?> _panelFolder;
        private readonly Action<string, string?> _showInPanel;
        private List<Catalogue> _catalogues = new();
        private Catalogue? _browsing;
        private string _folder = "";
        private bool _searching;
        private CancellationTokenSource? _work;

        public CataloguesWindow() : this(() => null, (_, _) => { }) { }

        /// <param name="panelFolder">The active panel's folder, for New from Panel.</param>
        /// <param name="showInPanel">Goes to a folder in the panel, selecting a name in it (or null).</param>
        public CataloguesWindow(Func<string?> panelFolder, Action<string, string?> showInPanel)
        {
            InitializeComponent();
            _panelFolder = panelFolder;
            _showInPanel = showInPanel;

            CatalogueList.SelectionChanged += (_, _) =>
            {
                if (CatalogueList.SelectedItem is CatalogueRow r) Browse(r.Catalogue, "");
            };
            EntryList.DoubleTapped += (_, _) => OpenSelected();
            EntryList.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
                else if (e.Key == Key.Back) { GoUp(); e.Handled = true; }
            };
            UpButton.Click += (_, _) => GoUp();
            NewButton.Click += async (_, _) => await NewFromPanelAsync();
            UpdateButton.Click += async (_, _) => await UpdateAsync();
            DeleteButton.Click += async (_, _) => await DeleteAsync();
            SearchButton.Click += (_, _) => Search();
            SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Search(); e.Handled = true; } };
            ShowButton.Click += (_, _) => ShowSelected();
            CancelButton.Click += (_, _) => _work?.Cancel();
            Closed += (_, _) => _work?.Cancel();

            Opened += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync(string? select = null)
        {
            StatusText.Text = "Loading catalogues…";
            _catalogues = await Task.Run(() => Catalogue.LoadAll());
            CatalogueList.ItemsSource = _catalogues.Select(c => new CatalogueRow { Catalogue = c }).ToList();
            StatusText.Text = _catalogues.Count == 0
                ? "No catalogues yet. Go to a drive's folder in the panel and press New from Panel."
                : $"{_catalogues.Count} catalogue{(_catalogues.Count == 1 ? "" : "s")}.";
            int index = select == null ? 0 : Math.Max(0, _catalogues.FindIndex(c => c.FilePath == select));
            if (_catalogues.Count > 0) CatalogueList.SelectedIndex = index;
            else Browse(null, "");
        }

        private void Browse(Catalogue? cat, string folder, string? selectName = null)
        {
            _searching = false;
            _browsing = cat;
            _folder = folder;
            UpButton.IsEnabled = cat != null && folder.Length > 0;
            UpdateButton.IsEnabled = DeleteButton.IsEnabled = cat != null;
            if (cat == null)
            {
                PathText.Text = "";
                EntryList.ItemsSource = null;
                InfoText.Text = "";
                return;
            }
            PathText.Text = Catalogue.JoinRoot(cat.Root, folder);
            var rows = cat.Children(folder).Select(e => Row(cat, e, e.Name)).ToList();
            EntryList.ItemsSource = rows;
            if (selectName != null) EntryList.SelectedItem = rows.FirstOrDefault(r => r.Entry?.Name == selectName);
            int files = rows.Count(r => r.Entry?.IsDirectory == false), dirs = rows.Count - files;
            InfoText.Text = $"{dirs:N0} folders, {files:N0} files here · {ProgressFormat.Bytes(cat.BytesBelow(folder))} in all · catalogued {cat.Made:g}";
        }

        private static EntryRow Row(Catalogue cat, CatalogueEntry e, string name) => new()
        {
            Catalogue = cat,
            Entry = e,
            Name = e.IsDirectory ? name + "/" : name,
            SizeText = e.IsDirectory ? "" : ProgressFormat.Bytes(e.Size),
            DateText = e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("g"),
            Tip = cat.FullPathOf(e),
        };

        private void OpenSelected()
        {
            if (EntryList.SelectedItem is not EntryRow { Entry: { } e } row) return;
            if (_searching)
            {
                // A search result: show it where it is in its catalogue.
                SelectCatalogue(row.Catalogue);
                Browse(row.Catalogue, e.Parent, e.Name);
                return;
            }
            if (e.IsDirectory) Browse(row.Catalogue, e.RelPath);
        }

        private void SelectCatalogue(Catalogue cat)
        {
            if (CatalogueList.ItemsSource is List<CatalogueRow> rows)
            {
                var r = rows.FirstOrDefault(x => ReferenceEquals(x.Catalogue, cat));
                if (r != null && !ReferenceEquals(CatalogueList.SelectedItem, r)) CatalogueList.SelectedItem = r;
            }
        }

        private void GoUp()
        {
            if (_searching) { Browse(_browsing, _folder); return; }
            if (_browsing == null || _folder.Length == 0) return;
            string name = _folder.Contains('/') ? _folder[(_folder.LastIndexOf('/') + 1)..] : _folder;
            Browse(_browsing, _folder.Contains('/') ? _folder[.._folder.LastIndexOf('/')] : "", name);
        }

        private void Search()
        {
            string text = (SearchBox.Text ?? "").Trim();
            if (text.Length == 0) { Browse(_browsing, _folder); return; }
            const int limit = 2000;
            var rows = new List<EntryRow>();
            foreach (var cat in _catalogues)
                foreach (var e in cat.Search(text, limit - rows.Count))
                    rows.Add(Row(cat, e, $"{cat.Name}: {e.RelPath}"));
            if (rows.Count == 0)
                rows.Add(new EntryRow { Catalogue = _catalogues.FirstOrDefault()!, Name = $"Nothing in any catalogue matches \"{text}\"." });
            _searching = true;
            EntryList.ItemsSource = rows;
            PathText.Text = $"Found in all catalogues: {text}";
            UpButton.IsEnabled = true;
            InfoText.Text = (rows.Count >= limit ? $"The first {limit:N0} matches" : $"{rows.Count(r => r.Entry != null):N0} matches") +
                            " · double-click one to see it in its catalogue";
        }

        private void ShowSelected()
        {
            var cat = (EntryList.SelectedItem as EntryRow)?.Catalogue ?? _browsing;
            if (cat == null) return;
            var e = (EntryList.SelectedItem as EntryRow)?.Entry;
            string folder = Catalogue.JoinRoot(cat.Root, e?.Parent ?? _folder);
            if (!Reachable(cat.Root))
            {
                _ = new MessageBox($"{cat.Name} isn't connected: there is no {cat.Root} right now.", "Disk Catalogues").ShowDialog(this);
                return;
            }
            _showInPanel(folder, e?.Name);
        }

        private static bool Reachable(string root)
        {
            try { return ProviderRegistry.For(root).DirectoryExists(root); } catch { return false; }
        }

        private async Task NewFromPanelAsync()
        {
            string? folder = _panelFolder();
            if (string.IsNullOrEmpty(folder) || ArchivePathCheck(folder))
            {
                await new MessageBox("Go to the drive or folder you want to catalogue in the panel first (not inside an archive).", "Disk Catalogues").ShowDialog(this);
                return;
            }
            string suggested = System.IO.Path.GetFileName(folder.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(suggested)) suggested = folder;
            string? name = await new TextPromptDialog("New Catalogue",
                $"Catalogue everything under\n{folder}\n\nName for the catalogue (such as the drive's name):", suggested).ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (_catalogues.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                await new MessageBox($"There is already a catalogue called {name}. Choose another name, or select it and press Update.", "Disk Catalogues").ShowDialog(this);
                return;
            }
            var cat = await ScanAsync(name, folder);
            if (cat == null) return;
            await Task.Run(() => cat.Save());
            await LoadAsync(cat.FilePath);
            StatusText.Text = $"Catalogued {cat.FileCount:N0} files in {cat.FolderCount:N0} folders ({ProgressFormat.Bytes(cat.TotalBytes)}) as {cat.Name}.";
        }

        private static bool ArchivePathCheck(string path) => FileSystem.Archive.ArchivePath.IsArchiveUri(path);

        private async Task UpdateAsync()
        {
            if (_browsing is not { } old) return;
            if (!Reachable(old.Root))
            {
                await new MessageBox($"{old.Name} isn't connected: there is no {old.Root} right now. Connect the drive, then press Update.",
                                     "Disk Catalogues").ShowDialog(this);
                return;
            }
            var fresh = await ScanAsync(old.Name, old.Root);
            if (fresh == null) return;
            var changes = CatalogueChanges.Between(old, fresh);
            fresh.FilePath = old.FilePath;
            await Task.Run(() => fresh.Save());
            ReportBox.Text = $"{old.Name}: changes since {old.Made:g}\n\n" + changes.Report();
            ReportBox.IsVisible = true;
            await LoadAsync(fresh.FilePath);
            StatusText.Text = changes.None
                ? $"{fresh.Name} is up to date: nothing changed since {old.Made:g}."
                : $"{fresh.Name} updated: {changes.Added.Count:N0} new, {changes.Removed.Count:N0} gone, {changes.Changed.Count:N0} changed.";
        }

        private async Task DeleteAsync()
        {
            if (_browsing is not { } cat || cat.FilePath == null) return;
            bool sure = await new MessageBox($"Delete the catalogue {cat.Name}? The drive itself isn't touched.", true, "Delete", "Cancel", "Disk Catalogues")
                .ShowDialog<bool>(this);
            if (!sure) return;
            try { System.IO.File.Delete(cat.FilePath); }
            catch (Exception ex) { await new MessageBox("It couldn't be deleted: " + ex.Message, "Disk Catalogues").ShowDialog(this); }
            await LoadAsync();
        }

        /// <summary>Walks the folder with a running count and Cancel. Null when cancelled or failed.</summary>
        private async Task<Catalogue?> ScanAsync(string name, string root)
        {
            _work = new CancellationTokenSource();
            var ct = _work.Token;
            NewButton.IsEnabled = UpdateButton.IsEnabled = DeleteButton.IsEnabled = false;
            CancelButton.IsVisible = true;
            StatusText.Text = $"Reading {root}…";
            var progress = new Progress<int>(n => StatusText.Text = $"Reading {root}… {n:N0} items so far");
            try
            {
                return await Task.Run(() => Catalogue.Scan(name, root, progress, ct), ct);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Stopped; nothing was saved.";
                return null;
            }
            catch (Exception ex)
            {
                StatusText.Text = "It couldn't be read: " + ex.Message;
                return null;
            }
            finally
            {
                CancelButton.IsVisible = false;
                NewButton.IsEnabled = true;
                UpdateButton.IsEnabled = DeleteButton.IsEnabled = _browsing != null;
                _work = null;
            }
        }
    }
}
