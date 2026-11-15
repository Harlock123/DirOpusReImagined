using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>
    /// Choosing which columns each panel shows: right-click a column header and tick or untick
    /// them. Name is always shown. The choice is per panel and kept in Configuration.xml as
    /// <c>&lt;Columns&gt;&lt;Left hidden="Dirs,Files"/&gt;&lt;Right hidden=""/&gt;&lt;/Columns&gt;</c>.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Every column a panel can show, as (property name, label), in display order.</summary>
        internal static IReadOnlyList<(string Name, string Label)> AllColumns { get; } =
            typeof(AFileEntry).GetProperties().Select(p => (p.Name, ColumnLabel(p.Name))).ToList();

        /// <summary>What a column is called in the menu; the header itself shows the property name.</summary>
        internal static string ColumnLabel(string name) => name switch
        {
            "Typ" => "Icon",
            "FileSize" => "Size",
            "FileDate" => "Date",
            "Dirs" => "Folders inside",
            "Files" => "Files inside",
            "Flags" => "Attributes",
            _ => name,
        };

        /// <summary>The column choices for the header's right-click menu.</summary>
        private IEnumerable<MenuItem> ColumnMenuItems(TaiDataGrid grid)
        {
            foreach (var (name, label) in AllColumns)
            {
                bool shown = grid.IsColumnVisible(name);
                bool locked = name == TaiDataGrid.AlwaysShownColumn;
                var mi = new MenuItem
                {
                    Header = (shown ? "✓ " : "    ") + label + (locked ? "  (always shown)" : ""),
                    IsEnabled = !locked,
                };
                string column = name;
                mi.Click += (_, _) => SetColumnShown(grid, column, !shown);
                yield return mi;
            }

            if (grid.HiddenColumns.Count > 0)
            {
                var all = new MenuItem { Header = "    Show all columns" };
                all.Click += (_, _) => grid.SetHiddenColumns(Array.Empty<string>());
                yield return all;
            }
        }

        private static void SetColumnShown(TaiDataGrid grid, string column, bool show)
        {
            var hidden = new HashSet<string>(grid.HiddenColumns);
            if (show) hidden.Remove(column); else hidden.Add(column);
            grid.SetHiddenColumns(hidden);
        }

        /// <summary>The hidden columns as saved: a comma-separated list of property names.</summary>
        internal static string FormatHiddenColumns(IEnumerable<string> hidden)
            => string.Join(",", AllColumns.Select(c => c.Name).Where(hidden.Contains));

        /// <summary>Reads a saved list, keeping only columns that exist (and never "Name").</summary>
        internal static IEnumerable<string> ParseHiddenColumns(string? saved)
        {
            if (string.IsNullOrWhiteSpace(saved)) return Array.Empty<string>();
            var known = AllColumns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            return saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(n => known.Contains(n) && n != TaiDataGrid.AlwaysShownColumn)
                        .Distinct();
        }

        private void InitColumns()
        {
            Closing += (_, _) => SaveColumnsToConfig();
        }

        private void LoadColumnsFromConfig()
        {
            try
            {
                if (_configFilePath == null || !File.Exists(_configFilePath)) return;
                var el = XDocument.Load(_configFilePath).Root?.Element("Columns");
                if (el == null) return;
                LPgrid.SetHiddenColumns(ParseHiddenColumns(el.Element("Left")?.Attribute("hidden")?.Value));
                RPgrid.SetHiddenColumns(ParseHiddenColumns(el.Element("Right")?.Attribute("hidden")?.Value));
            }
            catch { /* show every column */ }
        }

        private void SaveColumnsToConfig()
        {
            try
            {
                string path = _configFilePath ?? GetWritableConfigPath();
                XDocument doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Settings"));
                var root = doc.Root ?? new XElement("Settings");
                if (doc.Root == null) doc.Add(root);

                root.Element("Columns")?.Remove();
                root.Add(new XElement("Columns",
                    new XElement("Left", new XAttribute("hidden", FormatHiddenColumns(LPgrid.HiddenColumns))),
                    new XElement("Right", new XAttribute("hidden", FormatHiddenColumns(RPgrid.HiddenColumns)))));

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                doc.Save(path);
                _configFilePath = path;
            }
            catch { /* best-effort, like the other settings saved on close */ }
        }
    }
}
