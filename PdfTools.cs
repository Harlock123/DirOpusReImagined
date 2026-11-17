using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace DirOpusReImagined
{
    /// <summary>
    /// Merging, splitting, turning and taking out pages of PDFs (with PDFsharp). Every result is a
    /// new file; the originals are never changed. Pages are copied as they are, so nothing is
    /// re-rendered or loses quality. Password-protected PDFs can't be opened.
    /// </summary>
    public static class PdfTools
    {
        public static bool IsPdf(string name) => name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        public static int PageCount(string path)
        {
            using var doc = Open(path, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }

        private static PdfDocument Open(string path, PdfDocumentOpenMode mode)
        {
            try { return PdfReader.Open(path, mode); }
            catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"{Path.GetFileName(path)} is protected with a password, so its pages can't be copied.");
            }
            catch (Exception ex) when (ex is not IOException)
            {
                throw new IOException($"{Path.GetFileName(path)} couldn't be read as a PDF: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads page ranges like "1-3, 5, 8-" (pages counted from 1; "8-" runs to the end, "-3"
        /// from the start) into page numbers, in the order given. Throws <see cref="FormatException"/>
        /// for anything outside 1..<paramref name="pageCount"/>.
        /// </summary>
        public static List<int> ParseRanges(string text, int pageCount)
        {
            var pages = new List<int>();
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.Equals("all", StringComparison.OrdinalIgnoreCase))
                return Enumerable.Range(1, pageCount).ToList();
            foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                int dash = part.IndexOf('-');
                int from, to;
                if (dash < 0) from = to = Page(part);
                else
                {
                    from = dash == 0 ? 1 : Page(part[..dash]);
                    to = dash == part.Length - 1 ? pageCount : Page(part[(dash + 1)..]);
                }
                if (from < 1 || to > pageCount || from > to)
                    throw new FormatException($"\"{part}\" isn't within pages 1 to {pageCount}.");
                for (int p = from; p <= to; p++) pages.Add(p);
            }
            return pages;

            static int Page(string s) => int.TryParse(s, out int n) ? n : throw new FormatException($"\"{s}\" isn't a page number.");
        }

        /// <summary>Joins whole PDFs, in order, into <paramref name="output"/>. Returns the page count.</summary>
        public static int Merge(IReadOnlyList<string> sources, string output)
        {
            using var result = new PdfDocument();
            foreach (var source in sources)
            {
                using var doc = Open(source, PdfDocumentOpenMode.Import);
                foreach (var page in doc.Pages) result.AddPage(page);
            }
            int count = result.PageCount;
            Save(result, output);
            return count;
        }

        /// <summary>Copies the given pages (numbers from 1, in that order) into <paramref name="output"/>.</summary>
        public static int Extract(string source, IReadOnlyList<int> pages, string output)
        {
            using var doc = Open(source, PdfDocumentOpenMode.Import);
            using var result = new PdfDocument();
            foreach (int p in pages) result.AddPage(doc.Pages[p - 1]);
            int count = result.PageCount;
            Save(result, output);
            return count;
        }

        /// <summary>
        /// Splits into one PDF per <paramref name="pagesEach"/> pages ("name 1-10.pdf", "name 11-20.pdf"…)
        /// in <paramref name="folder"/>. Returns the files made.
        /// </summary>
        public static List<string> Split(string source, int pagesEach, string folder, Func<string, bool>? exists = null)
        {
            if (pagesEach < 1) throw new ArgumentOutOfRangeException(nameof(pagesEach));
            exists ??= p => File.Exists(p) || Directory.Exists(p);
            using var doc = Open(source, PdfDocumentOpenMode.Import);
            string stem = Path.GetFileNameWithoutExtension(source);
            var made = new List<string>();
            for (int start = 1; start <= doc.PageCount; start += pagesEach)
            {
                int end = Math.Min(doc.PageCount, start + pagesEach - 1);
                string name = start == end ? $"{stem} page {start}.pdf" : $"{stem} pages {start}-{end}.pdf";
                string target = Unique(Path.Combine(folder, name), exists);
                using var part = new PdfDocument();
                for (int p = start; p <= end; p++) part.AddPage(doc.Pages[p - 1]);
                Save(part, target);
                made.Add(target);
            }
            return made;
        }

        /// <summary>A copy with the given pages turned by <paramref name="degrees"/> (90, 180 or 270, clockwise).</summary>
        public static int Rotate(string source, IReadOnlyCollection<int> pages, int degrees, string output)
        {
            if (degrees % 90 != 0) throw new ArgumentException("Pages turn by 90, 180 or 270 degrees.", nameof(degrees));
            using var doc = Open(source, PdfDocumentOpenMode.Import);
            using var result = new PdfDocument();
            var turn = new HashSet<int>(pages);
            for (int i = 0; i < doc.PageCount; i++)
            {
                var page = result.AddPage(doc.Pages[i]);
                if (turn.Contains(i + 1)) page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
            }
            Save(result, output);
            return turn.Count;
        }

        /// <summary>The page turns (0, 90, 180, 270) of a PDF, for checking.</summary>
        public static List<int> Rotations(string path)
        {
            using var doc = Open(path, PdfDocumentOpenMode.Import);
            return doc.Pages.Cast<PdfPage>().Select(p => ((p.Rotate % 360) + 360) % 360).ToList();
        }

        /// <summary>"name (2).pdf" when "name.pdf" is taken.</summary>
        public static string Unique(string path, Func<string, bool> exists)
        {
            string dir = Path.GetDirectoryName(path) ?? ".", stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            string candidate = path;
            for (int n = 2; exists(candidate); n++) candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            return candidate;
        }

        /// <summary>Saves through a temporary name, so a failure never leaves half a PDF.</summary>
        private static void Save(PdfDocument doc, string output)
        {
            if (doc.PageCount == 0) throw new IOException("There are no pages to save.");
            string temp = Path.Combine(Path.GetDirectoryName(output) ?? ".", "." + Path.GetFileName(output) + ".dori-part");
            try
            {
                doc.Save(temp);
                File.Move(temp, output, overwrite: false);
            }
            finally { try { File.Delete(temp); } catch { } }
        }

        /// <summary>A small blank PDF with <paramref name="pages"/> pages (for tests and examples).</summary>
        public static void MakeBlank(string path, int pages)
        {
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++) doc.AddPage();
            doc.Save(path);
        }
    }
}
