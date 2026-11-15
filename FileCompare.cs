using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DirOpusReImagined
{
    public enum DiffKind { Same, Changed, Removed, Added }

    /// <summary>
    /// One row of a side-by-side comparison. A side with no line (the other side added or removed
    /// one) has a null line number and empty text.
    /// </summary>
    public sealed record DiffRow(int? LeftNo, string LeftText, int? RightNo, string RightText, DiffKind Kind);

    /// <summary>
    /// Line-by-line comparison of two texts (Myers' algorithm), and the byte-level check used for
    /// files that aren't text. Pure, so it is tested without a window.
    /// </summary>
    public static class TextDiff
    {
        /// <summary>
        /// Past this many differences the comparison stops looking for the shortest one and shows the
        /// rest of the middle as changed: the search's memory grows with the square of the count.
        /// </summary>
        public const int MaxEditDistance = 4000;

        /// <summary>Files larger than this are compared byte by byte rather than line by line.</summary>
        public const long MaxTextBytes = 32L * 1024 * 1024;

        /// <summary>Splits text into lines on \r\n, \n or \r. A final line break doesn't add an empty line.</summary>
        public static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\n' && c != '\r') continue;
                lines.Add(text[start..i]);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            if (start < text.Length) lines.Add(text[start..]);
            return lines;
        }

        /// <summary>True when the bytes look like a binary file rather than text: a NUL in the first 8000.</summary>
        public static bool LooksBinary(ReadOnlySpan<byte> start)
            => start[..Math.Min(start.Length, 8000)].IndexOf((byte)0) >= 0;

        /// <summary>The offset of the first byte that differs, or null if the streams are identical.</summary>
        public static long? FirstDifference(Stream a, Stream b)
        {
            var bufA = new byte[81920];
            var bufB = new byte[81920];
            long offset = 0;
            while (true)
            {
                int na = ReadFully(a, bufA);
                int nb = ReadFully(b, bufB);
                int n = Math.Min(na, nb);
                int mismatch = bufA.AsSpan(0, n).CommonPrefixLength(bufB.AsSpan(0, n));
                if (mismatch < n) return offset + mismatch;
                if (na != nb) return offset + n;   // one ended first
                if (na == 0) return null;
                offset += n;
            }
        }

        private static int ReadFully(Stream s, byte[] buf)
        {
            int total = 0;
            while (total < buf.Length)
            {
                int n = s.Read(buf, total, buf.Length - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        /// <summary>
        /// Compares two lists of lines into side-by-side rows. A run of removed lines next to a run of
        /// added ones is paired up as changed lines. With <paramref name="ignoreWhitespace"/>, lines
        /// that differ only in spaces and tabs count as the same.
        /// </summary>
        public static List<DiffRow> Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, bool ignoreWhitespace = false)
        {
            // Lines become numbers so the search compares ints, not strings.
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            int[] Key(IReadOnlyList<string> lines) => lines.Select(l =>
            {
                string k = ignoreWhitespace ? string.Concat(l.Where(c => !char.IsWhiteSpace(c))) : l;
                if (!ids.TryGetValue(k, out int id)) ids[k] = id = ids.Count;
                return id;
            }).ToArray();
            int[] a = Key(left), b = Key(right);

            var ops = EditScript(a, b);
            return Rows(ops, left, right);
        }

        private enum Op : byte { Equal, Delete, Insert }

        /// <summary>The steps that turn <paramref name="a"/> into <paramref name="b"/>, in order.</summary>
        private static List<Op> EditScript(int[] a, int[] b)
        {
            // Lines the two share at the start and end need no searching, and usually that's most of them.
            int prefix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
            int suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix
                   && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;

            var middle = Myers(a.AsSpan(prefix, a.Length - prefix - suffix).ToArray(),
                               b.AsSpan(prefix, b.Length - prefix - suffix).ToArray());

            var ops = new List<Op>(a.Length + b.Length);
            ops.AddRange(Enumerable.Repeat(Op.Equal, prefix));
            ops.AddRange(middle);
            ops.AddRange(Enumerable.Repeat(Op.Equal, suffix));
            return ops;
        }

        /// <summary>
        /// Myers' O(ND) shortest edit script. The furthest-reaching point on each diagonal is kept for
        /// every step so the path can be walked back; only the diagonals in reach are stored.
        /// </summary>
        private static List<Op> Myers(int[] a, int[] b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return Enumerable.Repeat(Op.Insert, m).ToList();
            if (m == 0) return Enumerable.Repeat(Op.Delete, n).ToList();

            int max = n + m;
            int off = max + 1;
            var v = new int[2 * max + 3];
            var trace = new List<int[]>();
            int found = -1;

            for (int d = 0; d <= max && d <= MaxEditDistance; d++)
            {
                // Snapshot diagonals -d-1..d+1 as they stand before this step.
                var snap = new int[2 * d + 3];
                Array.Copy(v, off - d - 1, snap, 0, snap.Length);
                trace.Add(snap);

                for (int k = -d; k <= d; k += 2)
                {
                    int x = k == -d || (k != d && v[off + k - 1] < v[off + k + 1]) ? v[off + k + 1] : v[off + k - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                    v[off + k] = x;
                    if (x >= n && y >= m) { found = d; break; }
                }
                if (found >= 0) break;
            }

            // Too different to search fully: show the whole middle as removed then added.
            if (found < 0)
                return Enumerable.Repeat(Op.Delete, n).Concat(Enumerable.Repeat(Op.Insert, m)).ToList();

            var ops = new List<Op>(n + m);
            int cx = n, cy = m;
            for (int d = found; d >= 0; d--)
            {
                var snap = trace[d];
                int At(int k) => snap[k + d + 1];
                int k = cx - cy;
                int prevK = k == -d || (k != d && At(k - 1) < At(k + 1)) ? k + 1 : k - 1;
                int prevX = d == 0 ? 0 : At(prevK);
                int prevY = prevX - prevK;
                if (d == 0) { prevX = 0; prevY = 0; }
                while (cx > prevX && cy > prevY) { ops.Add(Op.Equal); cx--; cy--; }
                if (d == 0) break;
                if (cx == prevX) ops.Add(Op.Insert); else ops.Add(Op.Delete);
                cx = prevX; cy = prevY;
            }
            ops.Reverse();
            return ops;
        }

        private static List<DiffRow> Rows(List<Op> ops, IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var rows = new List<DiffRow>(ops.Count);
            int li = 0, ri = 0;
            int i = 0;
            while (i < ops.Count)
            {
                if (ops[i] == Op.Equal)
                {
                    rows.Add(new DiffRow(li + 1, left[li], ri + 1, right[ri], DiffKind.Same));
                    li++; ri++; i++;
                    continue;
                }

                // A block of changes: removals and additions until the next equal line.
                int dels = 0, ins = 0;
                while (i < ops.Count && ops[i] != Op.Equal)
                {
                    if (ops[i] == Op.Delete) dels++; else ins++;
                    i++;
                }
                int paired = Math.Min(dels, ins);
                for (int p = 0; p < paired; p++)
                    rows.Add(new DiffRow(li + p + 1, left[li + p], ri + p + 1, right[ri + p], DiffKind.Changed));
                for (int p = paired; p < dels; p++)
                    rows.Add(new DiffRow(li + p + 1, left[li + p], null, "", DiffKind.Removed));
                for (int p = paired; p < ins; p++)
                    rows.Add(new DiffRow(null, "", ri + p + 1, right[ri + p], DiffKind.Added));
                li += dels; ri += ins;
            }
            return rows;
        }

        /// <summary>"3 changed, 1 removed, 2 added", or that the files match.</summary>
        public static string Summary(IReadOnlyList<DiffRow> rows, bool ignoreWhitespace)
        {
            int changed = rows.Count(r => r.Kind == DiffKind.Changed);
            int removed = rows.Count(r => r.Kind == DiffKind.Removed);
            int added = rows.Count(r => r.Kind == DiffKind.Added);
            if (changed + removed + added == 0)
                return ignoreWhitespace ? "The files match, apart from whitespace." : "The files are identical.";
            var parts = new List<string>();
            if (changed > 0) parts.Add($"{changed} line{(changed == 1 ? "" : "s")} changed");
            if (removed > 0) parts.Add($"{removed} removed");
            if (added > 0) parts.Add($"{added} added");
            return string.Join(", ", parts) + $" in {Blocks(rows)} place{(Blocks(rows) == 1 ? "" : "s")}.";
        }

        /// <summary>Where each block of differences starts, for next/previous change.</summary>
        public static List<int> ChangeStarts(IReadOnlyList<DiffRow> rows)
        {
            var starts = new List<int>();
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Kind != DiffKind.Same && (i == 0 || rows[i - 1].Kind == DiffKind.Same))
                    starts.Add(i);
            return starts;
        }

        private static int Blocks(IReadOnlyList<DiffRow> rows) => ChangeStarts(rows).Count;

        /// <summary>
        /// Which two files to compare: two files selected in the active panel, or else one file from
        /// each panel (the first selected, or the one under the cursor). Null when neither applies.
        /// Each result says whether it came from the active panel.
        /// </summary>
        public static ((bool Active, string Name) A, (bool Active, string Name) B)? PickFiles(
            IReadOnlyList<string> activeSelectedFiles, string? activeCursorFile,
            IReadOnlyList<string> otherSelectedFiles, string? otherCursorFile)
        {
            if (activeSelectedFiles.Count == 2)
                return ((true, activeSelectedFiles[0]), (true, activeSelectedFiles[1]));

            string? mine = activeSelectedFiles.Count == 1 ? activeSelectedFiles[0]
                         : activeSelectedFiles.Count == 0 ? activeCursorFile : null;
            string? theirs = otherSelectedFiles.Count >= 1 ? otherSelectedFiles[0] : otherCursorFile;
            if (mine == null || theirs == null) return null;
            return ((true, mine), (false, theirs));
        }
    }
}
