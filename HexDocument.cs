using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// The bytes behind the hex editor. A file on this computer is read in pages as they are
    /// shown, so a file of any size opens at once; a file in an archive or the cloud is read whole
    /// (up to <see cref="MaxInMemory"/>). Edits overwrite bytes in place, never inserting or
    /// removing, and are kept aside until saved: a local file then has only the changed bytes
    /// written; any other file is written whole through its provider.
    /// </summary>
    public sealed class HexDocument : IDisposable
    {
        public const long MaxInMemory = 256L * 1024 * 1024;
        private const int PageSize = 64 * 1024;

        private readonly string _path;
        private readonly FileStream? _file;
        private readonly byte[]? _memory;
        private readonly Dictionary<long, byte> _edits = new();
        private readonly Stack<(long Offset, bool Had, byte Old)> _undo = new();
        private long _pageStart = -1;
        private byte[] _page = Array.Empty<byte>();

        public long Length { get; }
        public string Path => _path;
        public bool IsLocal => _file != null;

        /// <summary>Why the file can't be changed, or null when it can.</summary>
        public string? ReadOnlyReason { get; }

        public int ChangeCount => _edits.Count;
        public bool IsChanged => _edits.Count > 0;
        public bool CanUndo => _undo.Count > 0;

        private HexDocument(string path, FileStream? file, byte[]? memory, long length, string? readOnly)
        {
            _path = path;
            _file = file;
            _memory = memory;
            Length = length;
            ReadOnlyReason = readOnly;
        }

        /// <summary>Opens a file from any provider. Throws when it can't be read (or is too large to read whole).</summary>
        public static HexDocument Open(string path)
        {
            var provider = ProviderRegistry.For(path);
            if (provider is LocalFileProvider)
            {
                FileStream fs;
                string? why = null;
                try { fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
                catch (UnauthorizedAccessException) { throw; }
                try
                {
                    // Opening for writing up front would lock the file; just ask whether it could be.
                    var attrs = File.GetAttributes(path);
                    if ((attrs & FileAttributes.ReadOnly) != 0) why = "the file is read-only";
                    else if (!OperatingSystem.IsWindows() && !CanWriteUnix(path)) why = "you don't have permission to change it";
                }
                catch { }
                return new HexDocument(path, fs, null, fs.Length, why);
            }

            byte[] data;
            using (var s = provider.OpenRead(path))
            {
                var ms = new MemoryStream();
                var buffer = new byte[81920];
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, n);
                    if (ms.Length > MaxInMemory)
                        throw new IOException($"The hex editor reads files in archives and the cloud whole, up to {MaxInMemory / (1024 * 1024)} MB. Copy this one to a local folder first.");
                }
                data = ms.ToArray();
            }
            string? reason = null;
            if (FileSystem.Archive.ArchivePath.IsArchiveUri(path))
                reason = FileSystem.Archive.ArchiveEditor.WhyReadOnly(FileSystem.Archive.ArchivePath.Parse(path).ArchiveFsPath);
            return new HexDocument(path, null, data, data.Length, reason);
        }

        /// <summary>A document over bytes already in memory (for tests); saving is not possible.</summary>
        public static HexDocument FromBytes(byte[] data) => new("", null, data, data.Length, null);

        private static bool CanWriteUnix(string path)
        {
            try { using var _ = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); return true; }
            catch { return false; }
        }

        /// <summary>Reads up to <paramref name="buffer"/>.Length bytes at <paramref name="offset"/>, with edits applied. Returns how many.</summary>
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= Length) return 0;
            int count = (int)Math.Min(buffer.Length, Length - offset);
            int done = 0;
            while (done < count)
            {
                long at = offset + done;
                if (_memory != null)
                {
                    _memory.AsSpan((int)at, count - done).CopyTo(buffer[done..]);
                    done = count;
                    break;
                }
                EnsurePage(at);
                int inPage = (int)(at - _pageStart);
                int n = Math.Min(count - done, _page.Length - inPage);
                if (n <= 0) break;
                _page.AsSpan(inPage, n).CopyTo(buffer[done..]);
                done += n;
            }
            if (_edits.Count > 0)
                for (int i = 0; i < done; i++)
                    if (_edits.TryGetValue(offset + i, out byte b)) buffer[i] = b;
            return done;
        }

        private void EnsurePage(long offset)
        {
            long start = offset / PageSize * PageSize;
            if (start == _pageStart) return;
            var page = new byte[(int)Math.Min(PageSize, Length - start)];
            _file!.Position = start;
            int got = 0;
            while (got < page.Length)
            {
                int n = _file.Read(page, got, page.Length - got);
                if (n <= 0) break;
                got += n;
            }
            _page = got == page.Length ? page : page[..got];
            _pageStart = start;
        }

        public byte Get(long offset)
        {
            Span<byte> one = stackalloc byte[1];
            return Read(offset, one) == 1 ? one[0] : (byte)0;
        }

        public bool IsEdited(long offset) => _edits.ContainsKey(offset);

        /// <summary>Changes one byte (remembered for undo). Setting it back to the original drops the edit.</summary>
        public void Set(long offset, byte value)
        {
            if (ReadOnlyReason != null) throw new InvalidOperationException("This file can't be changed: " + ReadOnlyReason + ".");
            if (offset < 0 || offset >= Length) throw new ArgumentOutOfRangeException(nameof(offset));
            bool had = _edits.TryGetValue(offset, out byte old);
            byte current = had ? old : Original(offset);
            if (current == value) return;
            _undo.Push((offset, had, old));
            if (Original(offset) == value) _edits.Remove(offset);
            else _edits[offset] = value;
        }

        private byte Original(long offset)
        {
            if (_memory != null) return _memory[offset];
            EnsurePage(offset);
            return _page[(int)(offset - _pageStart)];
        }

        /// <summary>Takes back the last change; returns its offset, or -1 when there was none.</summary>
        public long Undo()
        {
            if (_undo.Count == 0) return -1;
            var (offset, had, old) = _undo.Pop();
            if (had) _edits[offset] = old; else _edits.Remove(offset);
            return offset;
        }

        /// <summary>
        /// Finds <paramref name="pattern"/> starting at <paramref name="from"/> (forwards) or just
        /// before it (backwards), with edits applied. -1 when it isn't there.
        /// </summary>
        public long Find(byte[] pattern, long from, bool forward = true, Func<bool>? cancelled = null)
        {
            if (pattern.Length == 0 || pattern.Length > Length) return -1;
            const int chunk = 1 << 20;
            var buffer = new byte[chunk + pattern.Length - 1];
            if (forward)
            {
                for (long start = Math.Max(0, from); start <= Length - pattern.Length; start += chunk)
                {
                    if (cancelled?.Invoke() == true) return -1;
                    int n = Read(start, buffer);
                    int hit = buffer.AsSpan(0, n).IndexOf(pattern);
                    if (hit >= 0) return start + hit;
                    if (n < pattern.Length) break;
                }
            }
            else
            {
                long end = Math.Min(from, Length - pattern.Length);   // last position a match may start at
                while (end >= 0)
                {
                    if (cancelled?.Invoke() == true) return -1;
                    long start = Math.Max(0, end - chunk + 1);
                    int n = Read(start, buffer.AsSpan(0, (int)(end - start + pattern.Length)));
                    int hit = buffer.AsSpan(0, n).LastIndexOf(pattern);
                    if (hit >= 0) return start + hit;
                    end = start - 1;
                }
            }
            return -1;
        }

        /// <summary>Writes the changes. A local file has only its changed bytes written, in place.</summary>
        public void Save()
        {
            if (_edits.Count == 0) return;
            if (ReadOnlyReason != null) throw new InvalidOperationException("This file can't be changed: " + ReadOnlyReason + ".");
            if (_file != null)
            {
                using (var w = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (w.Length != Length) throw new IOException("The file's size changed since it was opened, so the changes weren't written. Open it again.");
                    foreach (var run in Runs())
                    {
                        w.Position = run.Start;
                        w.Write(run.Bytes);
                    }
                    w.Flush(true);
                }
                _pageStart = -1;   // read the written bytes back fresh
            }
            else if (_memory != null)
            {
                foreach (var (offset, value) in _edits) _memory[offset] = value;
                using var s = ProviderRegistry.For(_path).OpenWrite(_path);
                s.Write(_memory, 0, _memory.Length);
            }
            _edits.Clear();
            _undo.Clear();
        }

        /// <summary>The changed bytes as runs of neighbours, in order.</summary>
        public List<(long Start, byte[] Bytes)> Runs()
        {
            var runs = new List<(long, byte[])>();
            var current = new List<byte>();
            long start = -1, last = -2;
            foreach (var offset in _edits.Keys.OrderBy(k => k))
            {
                if (offset != last + 1 && current.Count > 0)
                {
                    runs.Add((start, current.ToArray()));
                    current.Clear();
                }
                if (current.Count == 0) start = offset;
                current.Add(_edits[offset]);
                last = offset;
            }
            if (current.Count > 0) runs.Add((start, current.ToArray()));
            return runs;
        }

        public void Dispose() => _file?.Dispose();

        // ---- Text helpers ----

        /// <summary>
        /// What to look for: hex bytes ("DE AD be ef", "deadbeef", "0xDE 0xAD") when
        /// <paramref name="asHex"/>, otherwise the text in UTF-8. Null when the hex isn't hex.
        /// </summary>
        public static byte[]? ParsePattern(string text, bool asHex)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (!asHex) return Encoding.UTF8.GetBytes(text);
            var digits = new StringBuilder();
            foreach (var token in text.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
                if (t.Length == 1) t = "0" + t;
                digits.Append(t);
            }
            string all = digits.ToString();
            if (all.Length == 0 || all.Length % 2 != 0 || !all.All(Uri.IsHexDigit)) return null;
            return Convert.FromHexString(all);
        }

        /// <summary>Reads an offset: "0x1F00", "1F00h", "$1F00", "8000" (decimal) or "50%" of the length. Null when it isn't one.</summary>
        public static long? ParseOffset(string text, long length)
        {
            text = (text ?? "").Trim().Replace("_", "").Replace(",", "");
            if (text.Length == 0) return null;
            if (text.EndsWith('%') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) && pct is >= 0 and <= 100)
                return Math.Min(Math.Max(0, length - 1), (long)(length * pct / 100));
            string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..]
                       : text.StartsWith('$') ? text[1..]
                       : text.EndsWith('h') || text.EndsWith('H') ? text[..^1] : "";
            if (hex.Length > 0)
                return long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long h) && h >= 0 ? h : null;
            // Plain digits are decimal; letters A–F with no marker are read as hex, since that's what a hex editor shows.
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long d)) return d;
            return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long x) && x >= 0 ? x : null;
        }

        /// <summary>A byte as shown in the text column: printable ASCII as itself, anything else as a dot.</summary>
        public static char Printable(byte b) => b is >= 0x20 and < 0x7F ? (char)b : '.';

        /// <summary>"00001F40  48 65 6C 6C 6F …  Hello…" for one row.</summary>
        public static string FormatRow(long offset, ReadOnlySpan<byte> bytes, int perRow, int offsetDigits)
        {
            var sb = new StringBuilder();
            sb.Append(offset.ToString("X" + offsetDigits)).Append("  ");
            for (int i = 0; i < perRow; i++)
            {
                if (i < bytes.Length) sb.Append(bytes[i].ToString("X2")); else sb.Append("  ");
                sb.Append(i == perRow / 2 - 1 ? "  " : " ");
            }
            sb.Append(' ');
            for (int i = 0; i < bytes.Length; i++) sb.Append(Printable(bytes[i]));
            return sb.ToString();
        }

        /// <summary>How many hex digits the offsets need (at least 8).</summary>
        public static int OffsetDigits(long length) => Math.Max(8, (Math.Max(1, length - 1)).ToString("X").Length);

        /// <summary>The value at the cursor read as numbers, for the status line: "72 · 0x48 · int16 25928 · int32 …".</summary>
        public string DescribeAt(long offset)
        {
            if (offset < 0 || offset >= Length) return "";
            Span<byte> b = stackalloc byte[8];
            int n = Read(offset, b);
            var parts = new List<string> { $"byte {b[0]} (0x{b[0]:X2}, signed {(sbyte)b[0]})" };
            if (n >= 2) parts.Add($"int16 {BitConverter.ToInt16(b[..2])}");
            if (n >= 4) parts.Add($"int32 {BitConverter.ToInt32(b[..4])}");
            if (n >= 8) parts.Add($"int64 {BitConverter.ToInt64(b[..8])}");
            return string.Join(" · ", parts) + " (little-endian)";
        }
    }
}
