using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>A place Go to Folder offers: where it came from (Bookmark, Recent…), what it's called, and its path.</summary>
    public sealed record FolderPlace(string Group, string Name, string Path);

    /// <summary>Building and reading the Go to Folder list (Ctrl/Cmd+G).</summary>
    public static class GoToFolder
    {
        /// <summary>
        /// The places in the order given, each path once (the first group that has it wins), so a
        /// bookmarked folder shows as a bookmark rather than again as recent.
        /// </summary>
        public static List<FolderPlace> Merge(IEnumerable<FolderPlace> places)
        {
            var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var list = new List<FolderPlace>();
            foreach (var p in places)
            {
                if (string.IsNullOrWhiteSpace(p.Path)) continue;
                string key = p.Path.Length > 1 ? p.Path.TrimEnd('/', '\\') : p.Path;
                if (seen.Add(key)) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// What was typed as a folder to go to, when it is one: a full path, ~ for the home folder
        /// (~/Documents), or a cloud:// or archive:// path. Null for words to search by.
        /// </summary>
        public static string? TypedPath(string text, string home)
        {
            string t = text.Trim().Trim('"');
            if (t.Length == 0) return null;
            if (t == "~") return home;
            if (t.StartsWith("~/") || t.StartsWith("~\\")) return System.IO.Path.Combine(home, t[2..]);
            if (t.Contains("://")) return t;
            return System.IO.Path.IsPathFullyQualified(t) ? t : null;
        }
    }
}
