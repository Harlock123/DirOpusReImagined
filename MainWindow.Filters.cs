using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>
    /// Saved filters: the ▾ button beside each panel's filter box lists filters kept for reuse
    /// (shared by both panels), and saves the one typed in.
    /// </summary>
    public partial class MainWindow
    {
        private void InitSavedFilters()
        {
            LPfilterMenu.Click += (_, _) => ShowSavedFilters(isLeft: true);
            RPfilterMenu.Click += (_, _) => ShowSavedFilters(isLeft: false);
        }

        private List<SavedFilter> ReadSavedFilters()
        {
            try
            {
                string path = _configFilePath ?? GetWritableConfigPath();
                return File.Exists(path) ? SavedFilters.Read(XDocument.Load(path).Root) : new List<SavedFilter>();
            }
            catch { return new List<SavedFilter>(); }
        }

        private void WriteSavedFilters(IEnumerable<SavedFilter> filters)
        {
            string path = _configFilePath ?? GetWritableConfigPath();
            XDocument doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Settings"));
            if (doc.Root == null) doc.Add(new XElement("Settings"));
            SavedFilters.Write(doc.Root!, filters);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            doc.Save(path);
            _configFilePath = path;
        }

        private void ShowSavedFilters(bool isLeft)
        {
            var box = isLeft ? LPfilter : RPfilter;
            var filters = ReadSavedFilters();
            var items = new List<Control>();
            foreach (var f in filters)
            {
                var mi = new MenuItem { Header = f.MenuLabel };
                var chosen = f;
                mi.Click += (_, _) => SetPanelFilter(isLeft, chosen.Pattern);
                items.Add(mi);
            }
            if (filters.Count == 0)
                items.Add(new MenuItem { Header = "No saved filters yet — type one, then Save", IsEnabled = false });

            items.Add(new Separator());
            string typed = (box.Text ?? "").Trim();
            var save = new MenuItem { Header = typed.Length > 0 ? $"Save \"{typed}\"…" : "Save the filter typed in…", IsEnabled = typed.Length > 0 };
            save.Click += async (_, _) => await SaveFilterAsync(typed);
            items.Add(save);

            if (filters.Count > 0)
            {
                items.Add(new MenuItem
                {
                    Header = "Remove",
                    ItemsSource = filters.Select(f =>
                    {
                        var r = new MenuItem { Header = f.MenuLabel };
                        r.Click += (_, _) => WriteSavedFilters(ReadSavedFilters()
                            .Where(x => !string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase)));
                        return r;
                    }).ToList(),
                });
            }

            new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedRight }
                .ShowAt(isLeft ? LPfilterMenu : RPfilterMenu);
        }

        private void SetPanelFilter(bool isLeft, string pattern)
        {
            if (isLeft)
            {
                LPfilter.Text = pattern;
                ApplyFilter(LPgrid, pattern, _lpUnfilteredItems);
            }
            else
            {
                RPfilter.Text = pattern;
                ApplyFilter(RPgrid, pattern, _rpUnfilteredItems);
            }
            UpdateStatusBar();
        }

        private async Task SaveFilterAsync(string pattern)
        {
            string? name = await new TextPromptDialog("Save Filter",
                $"A name for \"{pattern}\" (or keep the pattern as its name):", pattern).ShowDialog<string?>(this);
            if (name == null) return;
            try { WriteSavedFilters(SavedFilters.Upsert(ReadSavedFilters(), new SavedFilter(name, pattern))); }
            catch (Exception ex) { await new MessageBox("The filter couldn't be saved: " + ex.Message, "Save Filter").ShowDialog(this); }
        }
    }
}
