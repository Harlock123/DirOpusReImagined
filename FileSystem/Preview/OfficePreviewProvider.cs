using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Reads the text out of Office and OpenDocument files — Word paragraphs, Excel cells, PowerPoint
/// slides — so they preview as their content rather than as the ZIP of XML they physically are.
///
/// <para>No new dependency: OOXML and ODF are ZIP containers holding XML, both of which the app
/// already reads. Entries are fetched through the archive provider using <c>archive://</c> URIs, so
/// this inherits its entry lookup and cache instead of opening the file a second way.</para>
///
/// <para>Parsing matches on local element names and ignores XML namespaces. The namespace URIs
/// differ between OOXML versions and between Office and OpenDocument, and none of that changes
/// which element holds the text — matching on <c>t</c> or <c>text:p</c> alone is both simpler and
/// more tolerant of files this code has never seen.</para>
/// </summary>
public sealed class OfficePreviewProvider : IPreviewProvider
{
    /// <summary>Largest container to open. Comfortably above any ordinary document.</summary>
    private const long MaxContainerBytes = 64L * 1024 * 1024;

    /// <summary>Cap on extracted text, so a thousand-page document cannot stall the viewer.</summary>
    private const int MaxTextChars = 200_000;

    /// <summary>Spreadsheet grid limits — enough to see the shape of a sheet.</summary>
    private const int MaxRows = 200;
    private const int MaxColumns = 20;

    /// <summary>Slides summarised before the listing is truncated.</summary>
    private const int MaxSlides = 100;

    /// <summary>Above <see cref="ArchivePreviewProvider"/>, so these claim their ZIP containers
    /// first; below <see cref="PdfPreviewProvider"/>, which handles a different signature entirely.</summary>
    public int Priority => 85;

    private enum Kind { None, Word, Excel, PowerPoint, OpenText, OpenSheet, OpenSlides }

    public bool CanPreview(PreviewRequest r)
    {
        if (r.Signature != FileSignature.Kind.Zip) return false;
        if (r.Size > MaxContainerBytes) return false;

        // The reader opens a real file by path, so nested or remote containers are out.
        if (ArchivePath.IsArchiveUri(r.Path)) return false;
        if (r.Provider.IsRemote) return false;

        return Classify(r.DisplayName) != Kind.None;
    }

    /// <summary>
    /// Identifies the document type from its extension.
    ///
    /// <para>Extension rather than content, unusually for this codebase, because every one of these
    /// formats has the same ZIP header — the distinguishing parts are entries inside the container,
    /// and <c>CanPreview</c> must decide without opening the file. A misnamed file simply produces
    /// an empty extraction, which is reported honestly.</para>
    /// </summary>
    private static Kind Classify(string name)
    {
        string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
        return ext switch
        {
            ".docx" or ".docm" or ".dotx" => Kind.Word,
            ".xlsx" or ".xlsm" or ".xltx" => Kind.Excel,
            ".pptx" or ".pptm" or ".potx" => Kind.PowerPoint,
            ".odt" => Kind.OpenText,
            ".ods" => Kind.OpenSheet,
            ".odp" => Kind.OpenSlides,
            _ => Kind.None,
        };
    }

    public Task<PreviewResult> LoadAsync(PreviewRequest r, CancellationToken ct)
        => Task.Run<PreviewResult>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var kind = Classify(r.DisplayName);
                var entries = ListEntryKeys(r);

                string label = kind switch
                {
                    Kind.Word => "Word document",
                    Kind.Excel => "Excel workbook",
                    Kind.PowerPoint => "PowerPoint presentation",
                    Kind.OpenText => "OpenDocument text",
                    Kind.OpenSheet => "OpenDocument spreadsheet",
                    Kind.OpenSlides => "OpenDocument presentation",
                    _ => "Document",
                };

                string body = kind switch
                {
                    Kind.Word => ReadWord(r, ct),
                    Kind.Excel => ReadExcel(r, entries, ct),
                    Kind.PowerPoint => ReadPowerPoint(r, entries, ct),
                    _ => ReadOpenDocument(r, kind, ct),
                };

                if (string.IsNullOrWhiteSpace(body))
                {
                    // The container opened but held nothing this code recognises — a macro-only
                    // workbook, an unusual layout, or a file whose extension lies about it.
                    return new PreviewResult.Info(r.DisplayName, new List<InfoField>
                    {
                        new("Type", label),
                        new("Size", PreviewText.FormatSize(r.Size)),
                        new("Parts", $"{entries.Count:N0} in the container"),
                        new("Content", "No readable text found in this document."),
                    }, label);
                }

                bool truncated = body.Length > MaxTextChars;
                if (truncated) body = body.Substring(0, MaxTextChars) + "\n\n… truncated.";

                // Spreadsheets are rendered as fixed-width columns, so they must not wrap.
                bool wrap = kind is not (Kind.Excel or Kind.OpenSheet);
                return new PreviewResult.Text(body, BuildSubtitle(label, kind, body, truncated), wrap);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new PreviewResult.Error($"Could not read document: {ex.Message}");
            }
        }, ct);

    private static string BuildSubtitle(string label, Kind kind, string body, bool truncated)
    {
        string detail = kind is Kind.Excel or Kind.OpenSheet
            ? $"{body.Split('\n').Length:N0} rows"
            : $"{CountWords(body):N0} words";

        return $"{label} · {detail}" + (truncated ? " · truncated" : "");
    }

    private static int CountWords(string text) =>
        text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

    // ---- container access -------------------------------------------------------------------

    /// <summary>The archive:// URI addressing one entry inside the container.</summary>
    private static string EntryUri(string fsPath, string entryKey) =>
        $"{ArchivePath.Scheme}{fsPath}{ArchivePath.Marker}{entryKey}";

    private static IReadOnlyList<string> ListEntryKeys(PreviewRequest r)
    {
        var archives = ProviderRegistry.For(ArchivePath.RootUriFor(r.Path)) as ArchiveFileProvider
                       ?? new ArchiveFileProvider();
        return archives.ListEntries(r.Path).Where(e => !e.IsDirectory).Select(e => e.Key).ToList();
    }

    /// <summary>Parses one XML entry, or null when it is absent or unreadable.</summary>
    private static XDocument? ReadXml(PreviewRequest r, string entryKey, CancellationToken ct)
    {
        try
        {
            string uri = EntryUri(r.Path, entryKey);
            var provider = ProviderRegistry.For(uri);
            using var stream = provider.OpenRead(uri);
            byte[] bytes = PreviewText.ReadUpTo(stream, 32 * 1024 * 1024, ct);
            if (bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes);
            return XDocument.Load(ms);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;               // a missing or malformed part is not fatal to the preview
        }
    }

    /// <summary>All descendants with the given local name, regardless of namespace.</summary>
    private static IEnumerable<XElement> Named(XContainer root, string localName) =>
        root.Descendants().Where(e => e.Name.LocalName == localName);

    // ---- Word -------------------------------------------------------------------------------

    private static string ReadWord(PreviewRequest r, CancellationToken ct)
    {
        var doc = ReadXml(r, "word/document.xml", ct);
        if (doc == null) return "";

        var sb = new StringBuilder();
        foreach (var p in Named(doc, "p"))
        {
            ct.ThrowIfCancellationRequested();

            // A paragraph's text is spread across runs; concatenating its "t" descendants
            // reassembles it, and tabs/breaks inside contribute their own whitespace.
            var line = new StringBuilder();
            foreach (var node in p.Descendants())
            {
                switch (node.Name.LocalName)
                {
                    case "t": line.Append(node.Value); break;
                    case "tab": line.Append('\t'); break;
                    case "br": line.Append('\n'); break;
                }
            }

            sb.Append(line.ToString().TrimEnd()).Append('\n');
            if (sb.Length > MaxTextChars) break;
        }

        return Tidy(sb.ToString());
    }

    // ---- Excel ------------------------------------------------------------------------------

    private static string ReadExcel(PreviewRequest r, IReadOnlyList<string> entries, CancellationToken ct)
    {
        // Shared strings are stored once and referenced by index from the cells.
        var shared = new List<string>();
        var sst = ReadXml(r, "xl/sharedStrings.xml", ct);
        if (sst != null)
        {
            foreach (var si in Named(sst, "si"))
                shared.Add(string.Concat(Named(si, "t").Select(t => t.Value)));
        }

        string? sheetKey = entries
            .Where(k => k.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && k.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (sheetKey == null) return "";

        var sheet = ReadXml(r, sheetKey, ct);
        if (sheet == null) return "";

        var grid = new List<string?[]>();
        int widest = 0;

        foreach (var row in Named(sheet, "row").Take(MaxRows))
        {
            ct.ThrowIfCancellationRequested();

            var cells = new string?[MaxColumns];
            foreach (var c in Named(row, "c"))
            {
                int col = ColumnIndex(c.Attribute("r")?.Value);
                if (col < 0 || col >= MaxColumns) continue;

                cells[col] = CellText(c, shared);
                if (col + 1 > widest) widest = col + 1;
            }
            grid.Add(cells);
        }

        return RenderGrid(grid, widest);
    }

    /// <summary>Resolves a cell's displayed text, following a shared-string reference when present.</summary>
    private static string CellText(XElement c, List<string> shared)
    {
        string type = c.Attribute("t")?.Value ?? "";

        if (type == "s")
        {
            string raw = Named(c, "v").FirstOrDefault()?.Value ?? "";
            return int.TryParse(raw, out int idx) && idx >= 0 && idx < shared.Count ? shared[idx] : "";
        }

        if (type == "inlineStr")
            return string.Concat(Named(c, "t").Select(t => t.Value));

        // Numbers, dates and formula results all surface as the cached value in "v".
        return Named(c, "v").FirstOrDefault()?.Value ?? "";
    }

    /// <summary>Zero-based column index from a cell reference such as "AB12".</summary>
    private static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;

        int index = 0, letters = 0;
        foreach (char ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') { index = index * 26 + (ch - 'A' + 1); letters++; }
            else if (ch is >= 'a' and <= 'z') { index = index * 26 + (ch - 'a' + 1); letters++; }
            else break;
        }
        return letters == 0 ? -1 : index - 1;
    }

    /// <summary>Lays the cells out as fixed-width columns; the viewer renders in a monospaced font.</summary>
    private static string RenderGrid(List<string?[]> grid, int columns)
    {
        if (grid.Count == 0 || columns == 0) return "";

        var widths = new int[columns];
        foreach (var row in grid)
            for (int c = 0; c < columns; c++)
                widths[c] = Math.Max(widths[c], Math.Min((row[c] ?? "").Length, 24));

        var sb = new StringBuilder();
        foreach (var row in grid)
        {
            for (int c = 0; c < columns; c++)
            {
                string cell = row[c] ?? "";
                if (cell.Length > 24) cell = cell.Substring(0, 23) + "…";
                sb.Append(cell.PadRight(widths[c]));
                if (c < columns - 1) sb.Append("  ");
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    // ---- PowerPoint -------------------------------------------------------------------------

    private static string ReadPowerPoint(PreviewRequest r, IReadOnlyList<string> entries, CancellationToken ct)
    {
        // "slide10" must not sort before "slide2", so order by the trailing number, not the name.
        var slides = entries
            .Where(k => k.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                        && k.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(k => (Key: k, Number: SlideNumber(k)))
            .OrderBy(x => x.Number)
            .Take(MaxSlides)
            .ToList();

        var sb = new StringBuilder();
        foreach (var (key, number) in slides)
        {
            ct.ThrowIfCancellationRequested();

            var doc = ReadXml(r, key, ct);
            if (doc == null) continue;

            var texts = Named(doc, "t").Select(t => t.Value)
                                       .Where(v => !string.IsNullOrWhiteSpace(v))
                                       .ToList();
            if (texts.Count == 0) continue;

            sb.Append("── Slide ").Append(number).Append(" ──\n");
            foreach (string line in texts) sb.Append(line.Trim()).Append('\n');
            sb.Append('\n');

            if (sb.Length > MaxTextChars) break;
        }

        return Tidy(sb.ToString());
    }

    private static int SlideNumber(string key)
    {
        string name = Path.GetFileNameWithoutExtension(key);
        string digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    // ---- OpenDocument -----------------------------------------------------------------------

    private static string ReadOpenDocument(PreviewRequest r, Kind kind, CancellationToken ct)
    {
        var doc = ReadXml(r, "content.xml", ct);
        if (doc == null) return "";

        var sb = new StringBuilder();

        if (kind == Kind.OpenSheet)
        {
            foreach (var row in Named(doc, "table-row").Take(MaxRows))
            {
                ct.ThrowIfCancellationRequested();
                var cells = Named(row, "table-cell")
                    .Take(MaxColumns)
                    .Select(c => string.Concat(Named(c, "p").Select(p => p.Value)).Trim());
                sb.Append(string.Join("  ", cells).TrimEnd()).Append('\n');
                if (sb.Length > MaxTextChars) break;
            }
            return Tidy(sb.ToString());
        }

        foreach (var p in Named(doc, "p"))
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(p.Value.Trim()).Append('\n');
            if (sb.Length > MaxTextChars) break;
        }

        return Tidy(sb.ToString());
    }

    // ---- shared -----------------------------------------------------------------------------

    /// <summary>Collapses runs of blank lines left by empty paragraphs, which are common in these
    /// formats and would otherwise dominate the preview.</summary>
    private static string Tidy(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(text.Length);
        int blanks = 0;

        foreach (string line in lines)
        {
            if (line.Trim().Length == 0)
            {
                if (++blanks > 1) continue;
            }
            else
            {
                blanks = 0;
            }
            sb.Append(line).Append('\n');
        }

        return sb.ToString().Trim('\n');
    }
}
