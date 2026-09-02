using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Describes a PDF rather than rendering it: version, page count, encryption, and whatever the
/// document's <c>/Info</c> dictionary carries (title, author, producer, dates).
///
/// <para>Rendering a page would mean a native PDFium binary per platform — around 10 MB each, in
/// every single-file executable the publish scripts produce — which is a poor trade for a preview
/// pane. The metadata, by contrast, is plain text a short way into the file, so an honest summary
/// costs nothing. Without this the previous fallback was a hex dump, which tells the reader nothing
/// they wanted to know.</para>
///
/// <para>This is deliberately a scanner, not a PDF parser. It does not resolve the cross-reference
/// table, so a document that stores its metadata inside a compressed object stream (legal from
/// PDF 1.5 on) yields only what is readable in the clear. That case reports the fields it has and
/// says the rest is compressed, rather than guessing or showing nothing.</para>
/// </summary>
public sealed class PdfPreviewProvider : IPreviewProvider
{
    /// <summary>How much of the file to scan from the front. Comfortably covers a whole ordinary
    /// document; for larger ones the header, and usually the <c>/Info</c> dictionary, are here.</summary>
    private const int MaxHeadBytes = 4 * 1024 * 1024;

    /// <summary>How much to read from the end when the file is bigger than the head scan. The
    /// trailer — which carries <c>/Encrypt</c> and the <c>/Info</c> reference — lives there.</summary>
    private const int TailBytes = 256 * 1024;

    /// <summary>Sits below <see cref="ImagePreviewProvider"/> and above the byte fallback.</summary>
    public int Priority => 90;

    public bool CanPreview(PreviewRequest r) => r.Signature == FileSignature.Kind.Pdf;

    public Task<PreviewResult> LoadAsync(PreviewRequest r, CancellationToken ct)
        => Task.Run<PreviewResult>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                string scan = ReadScanText(r, ct);
                var fields = new List<InfoField>();

                string version = DescribeVersion(scan);
                fields.Add(new InfoField("Format", version));
                fields.Add(new InfoField("Size", PreviewText.FormatSize(r.Size)));

                string? pages = FindPageCount(scan);
                if (pages != null) fields.Add(new InfoField("Pages", pages));

                bool encrypted = scan.Contains("/Encrypt", StringComparison.Ordinal);
                if (encrypted) fields.Add(new InfoField("Encrypted", "Yes — content is protected"));

                int infoFieldCount = AppendInfoDictionary(scan, fields);

                if (infoFieldCount == 0)
                {
                    // Two different causes look identical from here - the document may carry no
                    // /Info dictionary at all, or may keep it in a compressed object stream this
                    // scanner does not decompress. Say both rather than assert the wrong one.
                    fields.Add(new InfoField("Metadata",
                        "None found — either absent, or stored in a compressed object stream."));
                }

                string subtitle = version + (pages != null ? $" · {pages} page" + (pages == "1" ? "" : "s") : "");
                return new PreviewResult.Info(r.DisplayName, fields, subtitle);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new PreviewResult.Error($"Could not read PDF: {ex.Message}");
            }
        }, ct);

    /// <summary>
    /// Reads the parts of the file worth scanning as Latin-1 text.
    ///
    /// <para>Latin-1 is the right decoding here precisely because it is not a guess at the content:
    /// it maps every byte to the character of the same value, so string offsets stay byte offsets
    /// and binary stream data passes through without throwing or being replaced. PDF syntax itself
    /// is ASCII, so structure survives intact; string values are decoded properly later.</para>
    /// </summary>
    private static string ReadScanText(PreviewRequest r, CancellationToken ct)
    {
        byte[] head;
        using (var stream = r.Provider.OpenRead(r.Path))
            head = PreviewText.ReadUpTo(stream, MaxHeadBytes, ct);

        // A file larger than the head scan keeps its trailer out of reach; fetch the end too when
        // the provider's stream can seek (a local file can, an archive entry cannot).
        if (r.Size > head.Length)
        {
            try
            {
                using var stream = r.Provider.OpenRead(r.Path);
                if (stream.CanSeek)
                {
                    long from = Math.Max(0, r.Size - TailBytes);
                    stream.Position = from;
                    byte[] tail = PreviewText.ReadUpTo(stream, TailBytes, ct);
                    return Encoding.Latin1.GetString(head) + "\n" + Encoding.Latin1.GetString(tail);
                }
            }
            catch
            {
                // The head alone is still worth reporting on.
            }
        }

        return Encoding.Latin1.GetString(head);
    }

    private static string DescribeVersion(string scan)
    {
        // "%PDF-1.7" / "%PDF-2.0"
        int i = scan.IndexOf("%PDF-", StringComparison.Ordinal);
        if (i >= 0 && i + 8 <= scan.Length)
        {
            string v = scan.Substring(i + 5, 3);
            if (v.Length == 3 && char.IsDigit(v[0]) && v[1] == '.' && char.IsDigit(v[2]))
                return $"PDF {v}";
        }
        return "PDF";
    }

    /// <summary>
    /// The document's page count, taken from the page tree's <c>/Count</c>.
    ///
    /// <para>Intermediate nodes carry their own subtree counts, so the largest value is the root's
    /// total. Counting <c>/Type /Page</c> occurrences instead would undercount any pages held in
    /// compressed object streams, which is worse than reporting nothing.</para>
    /// </summary>
    private static string? FindPageCount(string scan)
    {
        int best = -1;
        int i = 0;
        while ((i = scan.IndexOf("/Count", i, StringComparison.Ordinal)) >= 0)
        {
            int p = i + "/Count".Length;
            while (p < scan.Length && (scan[p] == ' ' || scan[p] == '\r' || scan[p] == '\n' || scan[p] == '\t')) p++;

            int start = p;
            while (p < scan.Length && char.IsDigit(scan[p])) p++;

            if (p > start && int.TryParse(scan.AsSpan(start, p - start), out int count) && count > best)
                best = count;

            i += "/Count".Length;
        }

        return best > 0 ? best.ToString(CultureInfo.InvariantCulture) : null;
    }

    // Keys worth showing, in the order a reader wants them.
    private static readonly (string Key, string Label)[] InfoKeys =
    {
        ("Title", "Title"),
        ("Author", "Author"),
        ("Subject", "Subject"),
        ("Keywords", "Keywords"),
        ("Creator", "Created with"),
        ("Producer", "Producer"),
        ("CreationDate", "Created"),
        ("ModDate", "Modified"),
    };

    /// <summary>Adds any readable <c>/Info</c> fields to <paramref name="fields"/>, returning how
    /// many were found.</summary>
    private static int AppendInfoDictionary(string scan, List<InfoField> fields)
    {
        if (!TryFindInfoDictionary(scan, out int start, out int end)) return 0;

        string dict = scan.Substring(start, end - start);
        int found = 0;

        foreach (var (key, label) in InfoKeys)
        {
            string? raw = ExtractValue(dict, key);
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string value = key is "CreationDate" or "ModDate" ? FormatPdfDate(raw) : raw;
            fields.Add(new InfoField(label, Collapse(value)));
            found++;
        }

        return found;
    }

    /// <summary>
    /// Locates the <c>/Info</c> dictionary by anchoring on a key that only it uses
    /// (<c>/Producer</c>, <c>/CreationDate</c>, <c>/ModDate</c>) and expanding to the enclosing
    /// <c>&lt;&lt; &gt;&gt;</c>.
    ///
    /// <para>Anchoring matters: <c>/Title</c> alone is not distinctive, because every outline
    /// (bookmark) entry has one — keying on that would happily report a chapter heading as the
    /// document title.</para>
    /// </summary>
    private static bool TryFindInfoDictionary(string scan, out int start, out int end)
    {
        start = end = 0;

        int anchor = -1;
        foreach (string key in new[] { "/Producer", "/CreationDate", "/ModDate" })
        {
            int at = scan.IndexOf(key, StringComparison.Ordinal);
            if (at >= 0 && (anchor < 0 || at < anchor)) anchor = at;
        }
        if (anchor < 0) return false;

        // Nearest dictionary opening before the anchor.
        int open = scan.LastIndexOf("<<", anchor, StringComparison.Ordinal);
        if (open < 0) return false;

        // Matching close, tracking nesting so a nested dictionary does not end it early.
        int depth = 0;
        for (int i = open; i < scan.Length - 1; i++)
        {
            if (scan[i] == '<' && scan[i + 1] == '<') { depth++; i++; continue; }
            if (scan[i] == '>' && scan[i + 1] == '>')
            {
                depth--;
                i++;
                if (depth == 0)
                {
                    start = open;
                    end = i + 1;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Reads the string value following <c>/Key</c> in a dictionary, or null.</summary>
    private static string? ExtractValue(string dict, string key)
    {
        int i = dict.IndexOf("/" + key, StringComparison.Ordinal);
        if (i < 0) return null;

        int p = i + key.Length + 1;

        // A longer key starting with this one (/CreationDate vs /Creation) would mis-match; require
        // the next character to end the name.
        if (p < dict.Length && (char.IsLetterOrDigit(dict[p]) || dict[p] == '-' || dict[p] == '_')) return null;

        while (p < dict.Length && char.IsWhiteSpace(dict[p])) p++;
        if (p >= dict.Length) return null;

        return dict[p] switch
        {
            '(' => ReadLiteralString(dict, p),
            '<' => ReadHexString(dict, p),
            _ => null,                       // a reference or number - nothing useful to show
        };
    }

    /// <summary>Reads a <c>(literal)</c> string, honouring escapes and nested parentheses.</summary>
    private static string? ReadLiteralString(string dict, int open)
    {
        var bytes = new List<byte>();
        int depth = 0;

        for (int i = open; i < dict.Length; i++)
        {
            char c = dict[i];

            if (c == '\\' && i + 1 < dict.Length)
            {
                char n = dict[++i];
                bytes.Add(n switch
                {
                    'n' => (byte)'\n',
                    'r' => (byte)'\r',
                    't' => (byte)'\t',
                    'b' => (byte)'\b',
                    'f' => (byte)'\f',
                    _ => (byte)n,            // covers \( \) \\ and stray escapes
                });
                continue;
            }

            if (c == '(') { depth++; if (depth == 1) continue; }
            else if (c == ')') { depth--; if (depth == 0) return DecodeTextBytes(bytes.ToArray()); }

            if (depth >= 1) bytes.Add((byte)c);
        }
        return null;
    }

    /// <summary>Reads a <c>&lt;hex&gt;</c> string.</summary>
    private static string? ReadHexString(string dict, int open)
    {
        int close = dict.IndexOf('>', open);
        if (close < 0) return null;

        var bytes = new List<byte>();
        int hi = -1;
        for (int i = open + 1; i < close; i++)
        {
            char c = dict[i];
            if (!Uri.IsHexDigit(c)) continue;            // whitespace inside a hex string is legal

            int v = Uri.FromHex(c);
            if (hi < 0) hi = v;
            else { bytes.Add((byte)((hi << 4) | v)); hi = -1; }
        }
        if (hi >= 0) bytes.Add((byte)(hi << 4));   // odd digit count: pad with 0, per the spec

        return DecodeTextBytes(bytes.ToArray());
    }

    /// <summary>
    /// Decodes PDF text-string bytes: UTF-16 when a byte-order mark says so, otherwise PDFDocEncoding,
    /// which agrees with Latin-1 across the range these fields realistically use.
    /// </summary>
    private static string DecodeTextBytes(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Turns a PDF date (<c>D:YYYYMMDDHHmmSS±HH'mm'</c>) into something readable. Everything after
    /// the year is optional, so each component is taken only if present.
    /// </summary>
    private static string FormatPdfDate(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith("D:", StringComparison.Ordinal)) s = s.Substring(2);

        if (s.Length < 4 || !int.TryParse(s.AsSpan(0, 4), out int year)) return raw;

        int Part(int offset) =>
            s.Length >= offset + 2 && int.TryParse(s.AsSpan(offset, 2), out int v) ? v : 0;

        int month = Math.Clamp(Part(4), 1, 12);
        int day = Math.Clamp(Part(6), 1, 31);
        int hour = Math.Clamp(Part(8), 0, 23);
        int minute = Math.Clamp(Part(10), 0, 59);
        int second = Math.Clamp(Part(12), 0, 59);

        try
        {
            var dt = new DateTime(year, month, day, hour, minute, second);
            return s.Length > 8
                ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>Flattens whitespace and trims a value so one field cannot take over the card.</summary>
    private static string Collapse(string value)
    {
        var sb = new StringBuilder(value.Length);
        bool space = false;
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || c == '\0')
            {
                if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
                continue;
            }
            sb.Append(c);
            space = false;
        }

        string text = sb.ToString().Trim();
        return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
    }
}
