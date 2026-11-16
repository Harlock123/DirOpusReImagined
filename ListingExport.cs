using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace DirOpusReImagined
{
    public enum ListingFormat { Csv, Tsv, Text, Html }

    /// <summary>
    /// Writes a panel's rows (right-click → File Tools → Export Listing) as CSV (RFC 4180, which
    /// spreadsheets open), tab-separated values, plain text in aligned columns, or an HTML page.
    /// </summary>
    public static class ListingExport
    {
        /// <summary>The format for a file name's ending (.csv, .tsv/.tab, .htm/.html); anything else is plain text.</summary>
        public static ListingFormat FormatFor(string fileName)
        {
            string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".csv" => ListingFormat.Csv,
                ".tsv" or ".tab" => ListingFormat.Tsv,
                ".htm" or ".html" => ListingFormat.Html,
                _ => ListingFormat.Text,
            };
        }

        public static string Write(ListingFormat format, string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
            => format switch
            {
                ListingFormat.Csv => Delimited(headers, rows, ',', CsvField),
                ListingFormat.Tsv => Delimited(headers, rows, '\t', TsvField),
                ListingFormat.Html => Html(title, headers, rows),
                _ => Text(title, headers, rows),
            };

        private static string Delimited(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, char sep, Func<string, string> field)
        {
            var sb = new StringBuilder();
            void Line(IEnumerable<string> cells) => sb.Append(string.Join(sep, cells.Select(field))).Append("\r\n");
            Line(headers);
            foreach (var r in rows) Line(r);
            return sb.ToString();
        }

        /// <summary>Quoted when it holds a comma, quote or line break, with quotes doubled.</summary>
        internal static string CsvField(string s)
            => s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || s.StartsWith(' ') || s.EndsWith(' ')
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;

        /// <summary>Tabs and line breaks become spaces, since TSV has no quoting.</summary>
        internal static string TsvField(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static string Text(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            int n = headers.Count;
            var width = new int[n];
            for (int c = 0; c < n; c++)
                width[c] = Math.Max(headers[c].Length, rows.Count == 0 ? 0 : rows.Max(r => c < r.Count ? r[c].Length : 0));
            var sb = new StringBuilder();
            sb.Append(title).Append('\n').Append('\n');
            void Line(IReadOnlyList<string> cells)
                => sb.Append(string.Join("  ", Enumerable.Range(0, n).Select(c => (c < cells.Count ? cells[c] : "").PadRight(width[c]))).TrimEnd()).Append('\n');
            Line(headers);
            sb.Append(string.Join("  ", width.Select(w => new string('-', w)))).Append('\n');
            foreach (var r in rows) Line(r);
            sb.Append('\n').Append(rows.Count == 1 ? "1 item" : $"{rows.Count.ToString("N0", CultureInfo.CurrentCulture)} items").Append('\n');
            return sb.ToString();
        }

        private static string Html(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            string E(string s) => WebUtility.HtmlEncode(s);
            var sb = new StringBuilder();
            sb.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append("</title>\n")
              .Append("<style>body{font-family:system-ui,sans-serif;margin:24px;color:#222}h1{font-size:18px}" +
                      "table{border-collapse:collapse;font-size:13px}th,td{padding:4px 10px;border-bottom:1px solid #ddd;text-align:left}" +
                      "th{background:#f3f3f3}tr:nth-child(even) td{background:#fafafa}p{color:#666;font-size:12px}</style>\n")
              .Append("</head><body>\n<h1>").Append(E(title)).Append("</h1>\n<table>\n<tr>");
            foreach (var h in headers) sb.Append("<th>").Append(E(h)).Append("</th>");
            sb.Append("</tr>\n");
            foreach (var r in rows)
            {
                sb.Append("<tr>");
                foreach (var cell in r) sb.Append("<td>").Append(E(cell)).Append("</td>");
                sb.Append("</tr>\n");
            }
            sb.Append("</table>\n<p>").Append(rows.Count == 1 ? "1 item" : $"{rows.Count:N0} items")
              .Append(" · exported by DORI on ").Append(E(DateTime.Now.ToString("g"))).Append("</p>\n</body></html>\n");
            return sb.ToString();
        }
    }
}
