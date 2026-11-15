using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace DirOpusReImagined
{
    /// <summary>
    /// A permissions change in chmod's notation: octal ("755", "0644") or symbolic clauses separated
    /// by commas ("u+x", "go-w", "a=r", "u=rwX,g=rX,o="). Symbolic changes act on each item's
    /// current mode; "X" adds execute only to folders and to files someone can already execute.
    /// The setuid, setgid and sticky bits ("s", "t", or a fourth octal digit) are supported too.
    /// </summary>
    public sealed class ModeChange
    {
        private readonly Func<UnixFileMode, bool, UnixFileMode> _apply;
        public string Text { get; }

        private ModeChange(string text, Func<UnixFileMode, bool, UnixFileMode> apply)
        {
            Text = text;
            _apply = apply;
        }

        /// <summary>The mode an item ends up with, given its current mode and whether it's a folder.</summary>
        public UnixFileMode ApplyTo(UnixFileMode current, bool isDirectory) => _apply(current, isDirectory);

        private const UnixFileMode Special = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;

        public static bool TryParse(string? text, out ModeChange change, out string error)
        {
            change = null!;
            error = "";
            string t = (text ?? "").Trim();
            if (t.Length == 0) { error = "Type a mode such as 755 or u+x,go-w."; return false; }

            if (t.All(c => c >= '0' && c <= '7'))
            {
                if (t.Length < 3 || t.Length > 4) { error = "An octal mode has 3 or 4 digits, such as 644 or 0755."; return false; }
                var mode = (UnixFileMode)Convert.ToInt32(t, 8);
                // Three digits leave setuid/setgid/sticky as they were, as chmod does for folders.
                bool keepSpecial = t.Length == 3;
                change = new ModeChange(t, (cur, _) => keepSpecial ? mode | (cur & Special) : mode);
                return true;
            }

            var clauses = new List<Func<UnixFileMode, bool, UnixFileMode>>();
            foreach (var raw in t.Split(','))
            {
                string clause = raw.Trim();
                int i = 0;
                bool u = false, g = false, o = false;
                for (; i < clause.Length && "ugoa".Contains(clause[i]); i++)
                {
                    if (clause[i] is 'u' or 'a') u = true;
                    if (clause[i] is 'g' or 'a') g = true;
                    if (clause[i] is 'o' or 'a') o = true;
                }
                if (i == 0) u = g = o = true;   // no "who" means everyone, as with "+x"
                if (i >= clause.Length || !"+-=".Contains(clause[i]))
                {
                    error = $"\"{clause}\" needs +, - or = (for example u+x).";
                    return false;
                }
                char op = clause[i++];
                string perms = clause[i..];
                if (perms.Any(c => !"rwxXst".Contains(c)))
                {
                    error = $"\"{clause}\": permissions are r, w, x, X, s and t.";
                    return false;
                }

                bool uu = u, gg = g, oo = o;
                clauses.Add((cur, isDir) =>
                {
                    UnixFileMode bits = 0;
                    bool anyExec = (cur & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
                    foreach (char p in perms)
                    {
                        bool x = p == 'x' || (p == 'X' && (isDir || anyExec));
                        if (p == 'r') bits |= (uu ? UnixFileMode.UserRead : 0) | (gg ? UnixFileMode.GroupRead : 0) | (oo ? UnixFileMode.OtherRead : 0);
                        if (p == 'w') bits |= (uu ? UnixFileMode.UserWrite : 0) | (gg ? UnixFileMode.GroupWrite : 0) | (oo ? UnixFileMode.OtherWrite : 0);
                        if (x) bits |= (uu ? UnixFileMode.UserExecute : 0) | (gg ? UnixFileMode.GroupExecute : 0) | (oo ? UnixFileMode.OtherExecute : 0);
                        if (p == 's') bits |= (uu ? UnixFileMode.SetUser : 0) | (gg ? UnixFileMode.SetGroup : 0);
                        if (p == 't') bits |= UnixFileMode.StickyBit;
                    }
                    UnixFileMode who = (uu ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.SetUser : 0)
                                     | (gg ? UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup : 0)
                                     | (oo ? UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute : 0);
                    return op switch
                    {
                        '+' => cur | bits,
                        '-' => cur & ~bits,
                        _ => (cur & ~who) | bits,   // "=": exactly these for the people named
                    };
                });
            }

            change = new ModeChange(t, (cur, isDir) => clauses.Aggregate(cur, (m, c) => c(m, isDir)));
            return true;
        }

        /// <summary>"rwxr-xr-x" for showing a mode.</summary>
        public static string Format(UnixFileMode m)
        {
            char Bit(UnixFileMode b, char c) => m.HasFlag(b) ? c : '-';
            return new string(new[]
            {
                Bit(UnixFileMode.UserRead, 'r'), Bit(UnixFileMode.UserWrite, 'w'), Bit(UnixFileMode.UserExecute, 'x'),
                Bit(UnixFileMode.GroupRead, 'r'), Bit(UnixFileMode.GroupWrite, 'w'), Bit(UnixFileMode.GroupExecute, 'x'),
                Bit(UnixFileMode.OtherRead, 'r'), Bit(UnixFileMode.OtherWrite, 'w'), Bit(UnixFileMode.OtherExecute, 'x'),
            });
        }
    }

    /// <summary>What to change on every selected item. Null means leave it as it is.</summary>
    public sealed record AttributeChanges(DateTime? Modified, ModeChange? Mode, bool? ReadOnly, bool IncludeContents);

    /// <summary>Applies <see cref="AttributeChanges"/> to local files and folders.</summary>
    public static class BatchAttributes
    {
        /// <summary>Reads "2026-11-15 14:30", "2026-11-15 14:30:05" or "2026-11-15" (local time), or the current culture's format.</summary>
        public static bool TryParseDate(string? text, out DateTime value)
        {
            string t = (text ?? "").Trim();
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm" };
            return DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value)
                || DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value);
        }

        /// <summary>
        /// The items to change: the selection, plus everything inside selected folders when asked.
        /// Linked folders are changed themselves but never walked into.
        /// </summary>
        public static List<(string Path, bool IsDirectory)> Targets(IEnumerable<(string Path, bool IsDirectory)> selection, bool includeContents)
        {
            var list = new List<(string, bool)>();
            foreach (var (path, isDir) in selection)
            {
                list.Add((path, isDir));
                if (!isDir || !includeContents || IsLink(path)) continue;
                var pending = new Stack<string>();
                pending.Push(path);
                while (pending.Count > 0)
                {
                    string dir = pending.Pop();
                    IEnumerable<FileSystemInfo> entries;
                    try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
                    catch { continue; }
                    foreach (var e in entries)
                    {
                        bool sub = e is DirectoryInfo;
                        list.Add((e.FullName, sub));
                        if (sub && e.LinkTarget == null) pending.Push(e.FullName);
                    }
                }
            }
            return list;
        }

        private static bool IsLink(string path)
        {
            try { return new FileInfo(path).LinkTarget != null; } catch { return false; }
        }

        /// <summary>Makes the changes, item by item. Returns a message for each item that couldn't be changed.</summary>
        public static List<string> Apply(IReadOnlyList<(string Path, bool IsDirectory)> targets, AttributeChanges changes,
                                         IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var problems = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (path, isDir) = targets[i];
                try
                {
                    if (changes.ReadOnly is { } ro)
                    {
                        var attrs = File.GetAttributes(path);
                        File.SetAttributes(path, ro ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly);
                    }
                    if (changes.Mode != null && !OperatingSystem.IsWindows())
                        File.SetUnixFileMode(path, changes.Mode.ApplyTo(File.GetUnixFileMode(path), isDir));
                    if (changes.Modified is { } when)
                    {
                        if (isDir) Directory.SetLastWriteTime(path, when);
                        else File.SetLastWriteTime(path, when);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
                progress?.Report(i + 1);
            }
            return problems;
        }
    }
}
