using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DirOpusReImagined
{
    /// <summary>What to find and what to put in its place.</summary>
    /// <param name="Regex">Treat <paramref name="Find"/> as a .NET regular expression; the replacement may then use $1, ${name}.</param>
    /// <param name="WholeWord">Only match where the text isn't part of a longer word.</param>
    public sealed record ReplaceOptions(string Find, string Replace, bool Regex = false, bool MatchCase = false, bool WholeWord = false);

    /// <summary>One changed line: its number (from 1), and the text before and after.</summary>
    public sealed record LineChange(int Line, string Before, string After);

    /// <summary>What replacing would do to one file.</summary>
    public sealed record FileReplacement(string Path, int Count, IReadOnlyList<LineChange> Lines);

    /// <summary>A file changed by a replace: its original copy, and its size and time just after writing (to tell if it changed again).</summary>
    public sealed record ReplacedFile(string Path, string Backup, long Length, DateTime Modified);

    /// <summary>
    /// Find and replace across files, a line at a time, so a match never spans lines and each
    /// line keeps its own ending. Files keep their encoding: a byte-order mark stays, UTF-8 stays
    /// UTF-8, and a file that isn't valid UTF-8 is read and written byte for byte as Latin-1, so
    /// bytes outside the matches are never changed. Binary files (a zero byte near the start) are
    /// left alone.
    /// </summary>
    public static class ReplaceInFiles
    {
        public const long MaxFileBytes = 50L * 1024 * 1024;
        private const int SniffBytes = 8192;
        private const int PreviewLinesPerFile = 200;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        /// <summary>The matcher for <paramref name="o"/>; throws <see cref="ArgumentException"/> for a bad pattern.</summary>
        public static Regex MakeRegex(ReplaceOptions o)
        {
            if (string.IsNullOrEmpty(o.Find)) throw new ArgumentException("There is nothing to find.");
            string pattern = o.Regex ? o.Find : System.Text.RegularExpressions.Regex.Escape(o.Find);
            if (o.WholeWord) pattern = $@"(?<!\w)(?:{pattern})(?!\w)";
            var flags = RegexOptions.CultureInvariant | (o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            return new Regex(pattern, flags, Timeout);
        }

        /// <summary>
        /// Replaces every match in <paramref name="text"/>, line by line. In plain mode the
        /// replacement is used exactly as typed ($ means $). Returns the new text and how many
        /// matches were replaced; <paramref name="changes"/> collects the changed lines.
        /// </summary>
        public static string Apply(string text, Regex rx, ReplaceOptions o, out int count, List<LineChange>? changes = null)
        {
            count = 0;
            var sb = new StringBuilder(text.Length);
            int lineNo = 0, pos = 0;
            while (pos < text.Length)
            {
                lineNo++;
                int nl = text.IndexOf('\n', pos);
                int end = nl < 0 ? text.Length : nl + 1;
                int contentEnd = nl < 0 ? text.Length : (nl > pos && text[nl - 1] == '\r' ? nl - 1 : nl);
                string line = text.Substring(pos, contentEnd - pos);
                string ending = text.Substring(contentEnd, end - contentEnd);

                int here = 0;
                string replaced = o.Regex
                    ? rx.Replace(line, m => { here++; return m.Result(o.Replace); })
                    : rx.Replace(line, _ => { here++; return o.Replace; });
                if (here > 0)
                {
                    count += here;
                    if (changes != null && changes.Count < PreviewLinesPerFile && replaced != line)
                        changes.Add(new LineChange(lineNo, line, replaced));
                }
                sb.Append(replaced).Append(ending);
                pos = end;
            }
            return sb.ToString();
        }

        /// <summary>How the file's bytes were turned into text, so they can be turned back the same way.</summary>
        public sealed record DecodedFile(string Text, Encoding Encoding, byte[] Preamble);

        /// <summary>Null for a binary file.</summary>
        public static DecodedFile? Decode(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return new DecodedFile(new UTF8Encoding(false).GetString(data, 3, data.Length - 3), new UTF8Encoding(false), new byte[] { 0xEF, 0xBB, 0xBF });
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return new DecodedFile(Encoding.Unicode.GetString(data, 2, data.Length - 2), new UnicodeEncoding(false, false), new byte[] { 0xFF, 0xFE });
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return new DecodedFile(Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2), new UnicodeEncoding(true, false), new byte[] { 0xFE, 0xFF });

            int sniff = Math.Min(data.Length, SniffBytes);
            for (int i = 0; i < sniff; i++) if (data[i] == 0) return null;

            try
            {
                var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                return new DecodedFile(strict.GetString(data), new UTF8Encoding(false), Array.Empty<byte>());
            }
            catch (DecoderFallbackException)
            {
                return new DecodedFile(Encoding.Latin1.GetString(data), Encoding.Latin1, Array.Empty<byte>());
            }
        }

        public static byte[] Encode(DecodedFile file, string text)
        {
            var body = file.Encoding.GetBytes(text);
            if (file.Preamble.Length == 0) return body;
            var all = new byte[file.Preamble.Length + body.Length];
            file.Preamble.CopyTo(all, 0);
            body.CopyTo(all, file.Preamble.Length);
            return all;
        }

        /// <summary>What replacing would change in <paramref name="path"/>, or null when nothing would (or it's binary or too large).</summary>
        public static FileReplacement? Preview(string path, Regex rx, ReplaceOptions o)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            var decoded = Decode(File.ReadAllBytes(path));
            if (decoded == null) return null;
            var lines = new List<LineChange>();
            Apply(decoded.Text, rx, o, out int count, lines);
            return count == 0 ? null : new FileReplacement(path, count, lines);
        }

        /// <summary>
        /// Replaces in one file, keeping a copy of the original in <paramref name="backupFolder"/>.
        /// The new text goes to a temporary file beside it first, then over the original, so a
        /// failure part way leaves the original as it was. Returns null when nothing matched.
        /// </summary>
        public static ReplacedFile? ReplaceFile(string path, Regex rx, ReplaceOptions o, string backupFolder, out int count)
        {
            count = 0;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            byte[] original = File.ReadAllBytes(path);
            var decoded = Decode(original);
            if (decoded == null) return null;
            string text = Apply(decoded.Text, rx, o, out count);
            if (count == 0) return null;
            byte[] bytes = Encode(decoded, text);

            Directory.CreateDirectory(backupFolder);
            string backup = Path.Combine(backupFolder, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
            File.WriteAllBytes(backup, original);

            string temp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".dori-replace");
            File.WriteAllBytes(temp, bytes);
            try
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            }
            catch { /* keep going: the default mode is fine */ }
            File.Move(temp, path, overwrite: true);
            var after = new FileInfo(path);
            return new ReplacedFile(path, backup, after.Length, after.LastWriteTimeUtc);
        }

        /// <summary>
        /// The files to search: each picked file, and the files inside each picked folder (all the
        /// way down when <paramref name="subfolders"/>), whose names match <paramref name="namePattern"/>
        /// ("*.cs;*.txt", empty for all). Linked folders are never walked into; hidden folders such
        /// as .git are skipped.
        /// </summary>
        public static List<string> CollectFiles(IEnumerable<string> picked, string namePattern, bool subfolders, CancellationToken ct = default)
        {
            var matchers = (namePattern ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                              .Select(p => FileSearch.MakeNameMatcher(p)).ToList();
            bool NameOk(string name) => matchers.Count == 0 || matchers.Any(m => m.IsMatch(name));

            var files = new List<string>();
            var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            void Add(string f) { if (seen.Add(f)) files.Add(f); }

            foreach (var p in picked)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(p)) { Add(p); continue; }
                if (!Directory.Exists(p)) continue;
                var stack = new Stack<string>();
                stack.Push(p);
                while (stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    string dir = stack.Pop();
                    IEnumerable<FileSystemInfo> entries;
                    try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
                    catch { continue; }
                    foreach (var e in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (e is DirectoryInfo d)
                        {
                            if (!subfolders || d.LinkTarget != null || d.Name.StartsWith('.')) continue;
                            stack.Push(d.FullName);
                        }
                        else if (e.LinkTarget == null && NameOk(e.Name)) Add(e.FullName);
                    }
                }
            }
            return files;
        }

        /// <summary>Whether a replaced file is still as the replace left it (so putting the original back loses nothing).</summary>
        public static bool Unchanged(ReplacedFile f)
        {
            try
            {
                var info = new FileInfo(f.Path);
                return info.Exists && info.Length == f.Length && info.LastWriteTimeUtc == f.Modified;
            }
            catch { return false; }
        }
    }
}
