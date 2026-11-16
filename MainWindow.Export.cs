using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Export Listing (right-click → File Tools): saves the panel's rows, with the columns it shows
    /// plus exact bytes and the full path, as CSV, tab-separated, plain text or HTML. Works in flat
    /// view and on search-filtered panels too: what the panel shows is what is written, or only
    /// the selected rows when several are selected.
    /// </summary>
    public partial class MainWindow
    {
        private async void ExportListing(bool isLeft)
        {
            try
            {
                var grid = isLeft ? LPgrid : RPgrid;
                string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
                if (string.IsNullOrEmpty(folder)) return;

                var selected = grid.SelectedItems.OfType<AFileEntry>().Where(e => e.Name != "..").ToList();
                var rows = selected.Count > 1
                    ? selected
                    : grid.Items.OfType<AFileEntry>().Where(e => e.Name != "..").ToList();
                if (rows.Count == 0)
                {
                    await new MessageBox("There is nothing in this panel to export.", "Export Listing").ShowDialog(this);
                    return;
                }

                string leaf = Path.GetFileName(folder.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(leaf)) leaf = "listing";
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = selected.Count > 1 ? $"Export {selected.Count} selected rows" : $"Export the listing of {leaf}",
                    SuggestedFileName = leaf + " listing.csv",
                    DefaultExtension = "csv",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("CSV (spreadsheets)") { Patterns = new[] { "*.csv" } },
                        new FilePickerFileType("Tab-separated values") { Patterns = new[] { "*.tsv" } },
                        new FilePickerFileType("Plain text") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType("Web page") { Patterns = new[] { "*.html" } },
                    },
                });
                string? target = file?.TryGetLocalPath();
                if (target == null) return;

                var columns = grid.ShownColumns();
                bool local = ProviderRegistry.For(folder) is LocalFileProvider;
                var (headers, table) = await Task.Run(() => ListingTable(folder, columns, rows, local));
                string title = (grid.FlatRoot != null ? "Everything below " : "") + folder;
                string text = ListingExport.Write(ListingExport.FormatFor(target), title, headers, table);
                await File.WriteAllTextAsync(target, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: ListingExport.FormatFor(target) == ListingFormat.Csv));
                if (isLeft) LeftStatusText.Text = $"Exported {rows.Count} rows to {target}";
                else RightStatusText.Text = $"Exported {rows.Count} rows to {target}";
                RefreshPanelsShowing(Path.GetDirectoryName(target) ?? "");
            }
            catch (Exception ex)
            {
                CrashLog.Write("Export listing", ex);
                await new MessageBox("The listing couldn't be saved: " + ex.Message, "Export Listing").ShowDialog(this);
            }
        }

        /// <summary>The headers and cells: the shown columns, then Bytes (exact, for files on this computer and measured folders) and Path.</summary>
        internal static (List<string> Headers, List<IReadOnlyList<string>> Rows) ListingTable(
            string folder, IReadOnlyList<string> columns, IReadOnlyList<AFileEntry> rows, bool local)
        {
            var props = columns.Select(c => typeof(AFileEntry).GetProperty(c)).Where(p => p != null).ToList();
            var headers = props.Select(p => ExportHeader(p!.Name)).Concat(new[] { "Bytes", "Path" }).ToList();
            var table = new List<IReadOnlyList<string>>(rows.Count);
            foreach (var row in rows)
            {
                var cells = new List<string>(headers.Count);
                foreach (var p in props)
                {
                    object? v = p!.GetValue(row);
                    cells.Add(p.Name == nameof(AFileEntry.Typ) ? (row.Typ ? "Folder" : "File") : v?.ToString() ?? "");
                }
                string path = FileUtility.JoinPanelPath(folder, row.Name);
                long bytes = row.Typ ? row.FolderBytes : local ? SafeLength(path) : -1;
                cells.Add(bytes >= 0 ? bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                cells.Add(path);
                table.Add(cells);
            }
            return (headers, table);
        }

        private static string ExportHeader(string column) => column switch
        {
            "Typ" => "Kind",
            "FileSize" => "Size",
            "FileDate" => "Date",
            "Dirs" => "Folders",
            "Files" => "Files",
            "Flags" => "Attributes",
            "Tags" => "Labels",
            _ => column,
        };

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return -1; }
        }
    }
}
