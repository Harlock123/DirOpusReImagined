using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem.Archive;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined
{
    /// <summary>
    /// Saved layouts (the Layouts ▾ button beside the button bank tabs): both panels' tabs, which
    /// tab is showing, hidden columns, list or thumbnail view, and the button bank, saved under a
    /// name in Configuration.xml and restored in one go.
    /// </summary>
    public partial class MainWindow
    {
        private void InitLayouts()
        {
            LayoutsButton.Click += (_, _) => ShowLayoutsMenu();
        }

        private List<SavedLayout> ReadLayouts()
        {
            try
            {
                string path = _configFilePath ?? GetWritableConfigPath();
                return File.Exists(path) ? Layouts.Read(XDocument.Load(path).Root) : new List<SavedLayout>();
            }
            catch { return new List<SavedLayout>(); }
        }

        private void WriteLayouts(IEnumerable<SavedLayout> layouts)
        {
            string path = _configFilePath ?? GetWritableConfigPath();
            XDocument doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Settings"));
            if (doc.Root == null) doc.Add(new XElement("Settings"));
            Layouts.Write(doc.Root!, layouts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            doc.Save(path);
            _configFilePath = path;
        }

        private void ShowLayoutsMenu()
        {
            var layouts = ReadLayouts();
            var items = new List<Control>();
            foreach (var layout in layouts)
            {
                var mi = new MenuItem { Header = layout.Name };
                ToolTip.SetTip(mi, Describe(layout));
                var chosen = layout;
                mi.Click += async (_, _) => await ApplyLayoutAsync(chosen);
                items.Add(mi);
            }
            if (layouts.Count == 0)
                items.Add(new MenuItem { Header = "No saved layouts yet", IsEnabled = false });

            items.Add(new Separator());
            var save = new MenuItem { Header = "Save current layout…" };
            save.Click += async (_, _) => await SaveCurrentLayoutAsync(layouts);
            items.Add(save);

            if (layouts.Count > 0)
            {
                var delete = new MenuItem { Header = "Delete" };
                delete.ItemsSource = layouts.Select(l =>
                {
                    var d = new MenuItem { Header = l.Name };
                    d.Click += async (_, _) =>
                    {
                        var ok = await new MessageBox($"Delete the layout \"{l.Name}\"?", showCancel: true,
                                                      okText: "Delete", cancelText: "Cancel", title: "Layouts").ShowDialog<bool>(this);
                        if (ok) WriteLayouts(ReadLayouts().Where(x => !string.Equals(x.Name, l.Name, StringComparison.OrdinalIgnoreCase)));
                    };
                    return d;
                }).ToList();
                items.Add(delete);
            }

            new MenuFlyout { ItemsSource = items, Placement = Avalonia.Controls.PlacementMode.TopEdgeAlignedRight }.ShowAt(LayoutsButton);
        }

        private static string Describe(SavedLayout l)
        {
            static string Side(PanelLayout p) => p.Tabs.Count == 1 ? p.Tabs[0] : $"{p.Tabs[p.ActiveTab]} (+{p.Tabs.Count - 1} more tabs)";
            return $"Left: {Side(l.Left)}\nRight: {Side(l.Right)}" + (l.ButtonBank != null ? $"\nButtons: {l.ButtonBank}" : "");
        }

        /// <summary>Both panels and the button bank as they are now.</summary>
        private SavedLayout CaptureLayout(string name)
        {
            SaveActiveTabState(true);
            SaveActiveTabState(false);
            PanelLayout Panel(List<PanelTab> tabs, int active, TaiDataGrid grid)
                => new(tabs.Select(t => t.Path).ToList(), active, grid.HiddenColumns.ToList(), grid.ViewMode);
            return new SavedLayout(name, Panel(_lpTabs, _lpActiveTab, LPgrid), Panel(_rpTabs, _rpActiveTab, RPgrid),
                                   _banks.Count > 1 ? _banks[_bankIndex].Name : null);
        }

        private async Task SaveCurrentLayoutAsync(List<SavedLayout> existing)
        {
            string? name = await new TextPromptDialog("Save Layout",
                "Name this layout (both panels' tabs, columns and view, and the button bank):").ShowDialog<string?>(this);
            if (name == null) return;

            if (existing.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                bool replace = await new MessageBox($"There is already a layout called \"{name}\". Replace it?", showCancel: true,
                                                    okText: "Replace", cancelText: "Cancel", title: "Save Layout").ShowDialog<bool>(this);
                if (!replace) return;
            }

            try { WriteLayouts(Layouts.Upsert(ReadLayouts(), CaptureLayout(name))); }
            catch (Exception ex) { await new MessageBox("The layout couldn't be saved: " + ex.Message, "Save Layout").ShowDialog(this); }
        }

        /// <summary>Puts both panels (and the button bank) back as the layout has them, skipping folders that are gone.</summary>
        private async Task ApplyLayoutAsync(SavedLayout layout)
        {
            // Cloud and archive tabs are kept as they are (checking them could mean a network call).
            static bool Exists(string p) => CloudPath.IsCloudUri(p) || ArchivePath.IsArchiveUri(p) || Directory.Exists(p);
            var (left, right) = await Task.Run(() => (Layouts.Usable(layout.Left, Exists), Layouts.Usable(layout.Right, Exists)));

            if (left != null) ApplyPanelLayout(isLeft: true, left);
            if (right != null) ApplyPanelLayout(isLeft: false, right);

            if (layout.ButtonBank != null)
            {
                int bank = _banks.FindIndex(b => string.Equals(b.Name, layout.ButtonBank, StringComparison.OrdinalIgnoreCase));
                if (bank >= 0) ShowButtonBank(bank);
            }

            var gone = layout.Left.Tabs.Concat(layout.Right.Tabs).Where(p => !Exists(p)).Distinct().ToList();
            if (gone.Count > 0)
                await new MessageBox("These folders in the layout no longer exist and were left out:\n\n" +
                                     string.Join("\n", gone.Take(10)), "Layouts").ShowDialog(this);
        }

        private void ApplyPanelLayout(bool isLeft, PanelLayout panel)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            var tabs = isLeft ? _lpTabs : _rpTabs;
            grid.FlatRoot = null;
            tabs.Clear();
            foreach (var p in panel.Tabs) tabs.Add(new PanelTab(p));
            if (isLeft) _lpActiveTab = panel.ActiveTab; else _rpActiveTab = panel.ActiveTab;
            grid.SetHiddenColumns(panel.HiddenColumns);
            ApplyViewMode(grid, isLeft, panel.View);
            LoadTab(isLeft, panel.ActiveTab);
        }
    }
}
