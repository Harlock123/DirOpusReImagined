using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>A single search match.</summary>
    /// <param name="Folder">The folder the match was found in (where to navigate for a file hit).</param>
    /// <param name="FullPath">Full path / cloud URI of the matched item.</param>
    /// <param name="Line">For a text search: the 1-based line of the first match, else 0.</param>
    /// <param name="Snippet">For a text search: the first matching line, trimmed and shortened.</param>
    /// <param name="MatchCount">For a text search: how many lines in the file match.</param>
    public sealed record SearchHit(string Folder, string FullPath, string Name, bool IsDir, long Size, DateTime Modified,
                                   int Line = 0, string? Snippet = null, int MatchCount = 0)
    {
        /// <summary>"Line 12: the matching text · 3 matching lines", or null for a name-only hit.</summary>
        public string? MatchSummary => Snippet == null
            ? null
            : $"Line {Line}: {Snippet}" + (MatchCount > 1 ? $"  · {MatchCount} matching lines" : "");
    }

    /// <param name="MatchCase">Applies to both the name pattern and the text.</param>
    /// <param name="IncludeFolders">Report folders whose names match (ignored for a text search).</param>
    /// <param name="ContainingText">Only report files whose contents contain this; null or empty for a name-only search.</param>
    /// <param name="TextIsRegex">Treat <paramref name="ContainingText"/> as a .NET regular expression.</param>
    /// <param name="MaxFileBytes">Files larger than this aren't read for a text search.</param>
    /// <param name="SearchRemoteContents">Read cloud files too. Off by default: it downloads every file it checks.</param>
    public sealed record SearchOptions(bool MatchCase, bool IncludeFolders,
                                       string? ContainingText = null, bool TextIsRegex = false,
                                       long MaxFileBytes = FileSearch.DefaultMaxFileBytes,
                                       bool SearchRemoteContents = false)
    {
        public bool IsTextSearch => !string.IsNullOrEmpty(ContainingText);
    }

    /// <summary>Counts from a text search, for the status line. Updated by the searching thread.</summary>
    public sealed class SearchStats
    {
        private int _read, _binary, _tooLarge, _remote, _unreadable;
        public int FilesRead => _read;
        public int SkippedBinary => _binary;
        public int SkippedTooLarge => _tooLarge;
        public int SkippedRemote => _remote;
        public int Unreadable => _unreadable;

        internal void Read() => Interlocked.Increment(ref _read);
        internal void Binary() => Interlocked.Increment(ref _binary);
        internal void TooLarge() => Interlocked.Increment(ref _tooLarge);
        internal void Remote() => Interlocked.Increment(ref _remote);
        internal void Failed() => Interlocked.Increment(ref _unreadable);

        /// <summary>"340 files read · 5 binary, 1 too large skipped", or "" if nothing was read or skipped.</summary>
        public string Describe()
        {
            var skipped = new List<string>();
            if (SkippedBinary > 0) skipped.Add($"{SkippedBinary:N0} binary");
            if (SkippedTooLarge > 0) skipped.Add($"{SkippedTooLarge:N0} too large");
            if (SkippedRemote > 0) skipped.Add($"{SkippedRemote:N0} cloud");
            if (Unreadable > 0) skipped.Add($"{Unreadable:N0} unreadable");

            var parts = new List<string>();
            if (FilesRead > 0) parts.Add($"{FilesRead:N0} file(s) read");
            if (skipped.Count > 0) parts.Add(string.Join(", ", skipped) + " skipped");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Recursive search rooted at a folder, reading through <see cref="IFileProvider"/> so it
    /// works for local, archive and cloud paths alike. The name pattern is a case-insensitive
    /// substring by default; if it contains <c>*</c> or <c>?</c> it is treated as a wildcard matched
    /// against the whole name. With <see cref="SearchOptions.ContainingText"/> set, files whose names
    /// match are also read line by line and reported only if a line contains the text. Inaccessible
    /// folders and unreadable files are skipped rather than aborting the search.
    /// </summary>
    public static class FileSearch
    {
        public const long DefaultMaxFileBytes = 100L * 1024 * 1024;

        private const int SniffBytes = 8192;
        private const int SnippetMax = 160;
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        public static void Search(string root, string pattern, SearchOptions opt,
            Action<SearchHit> onHit, CancellationToken ct, IProgress<string>? progress = null,
            SearchStats? stats = null)
        {
            if (string.IsNullOrEmpty(root)) return;

            // A text search with no name pattern looks in every file.
            bool noPattern = string.IsNullOrWhiteSpace(pattern);
            if (noPattern && !opt.IsTextSearch) return;

            var nameMatcher = noPattern ? null : MakeNameMatcher(pattern, opt.MatchCase);
            var textMatcher = opt.IsTextSearch ? MakeTextMatcher(opt.ContainingText!, opt.TextIsRegex, opt.MatchCase) : null;
            SearchInto(root, nameMatcher, textMatcher, opt, onHit, ct, progress, stats ?? new SearchStats());
        }

        /// <summary>
        /// Builds a name matcher: a wildcard match against the whole name when the pattern contains
        /// <c>*</c> or <c>?</c> (e.g. <c>*.jpg</c>), otherwise a substring match (e.g. <c>jpg</c>).
        /// Shared by the recursive search and the per-panel filter box.
        /// </summary>
        public static Regex MakeNameMatcher(string pattern, bool matchCase = false)
        {
            var opts = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0)
                return new Regex(Regex.Escape(pattern), opts); // substring (contains)

            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(rx, opts);
        }

        /// <summary>
        /// Builds the matcher for a text search: the literal text, or a regular expression.
        /// </summary>
        /// <exception cref="ArgumentException">The regular expression isn't valid; the message says why.</exception>
        public static Regex MakeTextMatcher(string text, bool isRegex, bool matchCase)
        {
            var opts = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            return new Regex(isRegex ? text : Regex.Escape(text), opts, RegexTimeout);
        }

        private static void SearchInto(string root, Regex? nameMatcher, Regex? textMatcher, SearchOptions opt,
            Action<SearchHit> onHit, CancellationToken ct, IProgress<string>? progress, SearchStats stats)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(root);

            var provider = ProviderRegistry.For(root);
            List<FileEntry> dirs, files;
            try
            {
                dirs = provider.EnumerateDirectories(root).ToList();
                files = provider.EnumerateFiles(root).ToList();
            }
            catch
            {
                return; // inaccessible folder (permissions) — skip it
            }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (nameMatcher != null && !nameMatcher.IsMatch(f.Name)) continue;

                if (textMatcher == null)
                {
                    onHit(new SearchHit(root, f.Path, f.Name, false, f.Size, f.LastModified));
                    continue;
                }

                if (provider.IsRemote && !opt.SearchRemoteContents) { stats.Remote(); continue; }
                if (f.Size > opt.MaxFileBytes) { stats.TooLarge(); continue; }

                var match = SearchContents(provider, f.Path, textMatcher, stats, ct);
                if (match is { } m)
                    onHit(new SearchHit(root, f.Path, f.Name, false, f.Size, f.LastModified, m.Line, m.Snippet, m.Count));
            }

            foreach (var d in dirs)
            {
                ct.ThrowIfCancellationRequested();
                if (textMatcher == null && opt.IncludeFolders && nameMatcher != null && nameMatcher.IsMatch(d.Name))
                    onHit(new SearchHit(root, d.Path, d.Name, true, 0, d.LastModified));

                SearchInto(d.Path, nameMatcher, textMatcher, opt, onHit, ct, progress, stats);
            }
        }

        private readonly record struct ContentMatch(int Line, string Snippet, int Count);

        /// <summary>
        /// Reads one file line by line. Returns the first matching line and how many lines match,
        /// or null if none do or the file was skipped (binary, unreadable).
        /// </summary>
        private static ContentMatch? SearchContents(IFileProvider provider, string path, Regex matcher,
                                                    SearchStats stats, CancellationToken ct)
        {
            try
            {
                using var input = provider.OpenRead(path);

                var head = new byte[SniffBytes];
                int headLen = ReadUpTo(input, head);
                if (LooksBinary(head.AsSpan(0, headLen))) { stats.Binary(); return null; }

                stats.Read();
                // The sniffed bytes are replayed in front of the rest, so this works whether or not
                // the source can seek (archive entries and cloud streams can't).
                using var reader = new StreamReader(
                    new PrefixedStream(head, headLen, input), new UTF8Encoding(false),
                    detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

                int lineNo = 0, count = 0, firstLine = 0;
                string? firstSnippet = null;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNo++;
                    if ((lineNo & 0xFF) == 0) ct.ThrowIfCancellationRequested();

                    var m = matcher.Match(line);
                    if (!m.Success) continue;
                    count++;
                    if (firstSnippet == null)
                    {
                        firstLine = lineNo;
                        firstSnippet = MakeSnippet(line, m.Index, m.Length);
                    }
                }
                return count > 0 ? new ContentMatch(firstLine, firstSnippet!, count) : null;
            }
            catch (OperationCanceledException) { throw; }
            catch (RegexMatchTimeoutException) { stats.Failed(); return null; }
            catch (Exception) { stats.Failed(); return null; }
        }

        private static int ReadUpTo(Stream s, byte[] buffer)
        {
            int total = 0, n;
            while (total < buffer.Length && (n = s.Read(buffer, total, buffer.Length - total)) > 0)
                total += n;
            return total;
        }

        /// <summary>
        /// True for data that isn't text: a NUL byte in the first few KB, unless a UTF-16 or UTF-32
        /// byte-order mark says the NULs are part of the encoding.
        /// </summary>
        internal static bool LooksBinary(ReadOnlySpan<byte> head)
        {
            if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
                return false; // UTF-16 (and UTF-32 LE, which starts the same way)
            if (head.Length >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 0xFE && head[3] == 0xFF)
                return false; // UTF-32 BE
            return head.IndexOf((byte)0) >= 0;
        }

        /// <summary>The line around the match, whitespace trimmed, at most <see cref="SnippetMax"/> characters.</summary>
        internal static string MakeSnippet(string line, int matchIndex, int matchLength)
        {
            // Start a little before the match so it's seen in context, unless the line is short.
            int start = line.Length <= SnippetMax ? 0 : Math.Max(0, matchIndex - 40);
            int end = Math.Min(line.Length, start + SnippetMax);

            string s = line.Substring(start, end - start).Replace('\t', ' ').Trim();
            if (start > 0) s = "…" + s;
            if (end < line.Length) s += "…";
            return s;
        }

        /// <summary>Replays already-read bytes, then continues with the rest of the stream.</summary>
        private sealed class PrefixedStream(byte[] prefix, int prefixLength, Stream rest) : Stream
        {
            private int _pos;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos < prefixLength)
                {
                    int n = Math.Min(count, prefixLength - _pos);
                    Array.Copy(prefix, _pos, buffer, offset, n);
                    _pos += n;
                    return n;
                }
                return rest.Read(buffer, offset, count);
            }
        }
    }
}
