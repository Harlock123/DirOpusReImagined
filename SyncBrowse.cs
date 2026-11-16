using System;
using System.Collections.Generic;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>
    /// Synced browsing: with it on, going into a subfolder in one panel takes the other panel into
    /// the subfolder of the same name, and going up takes it up as many levels. The other panel
    /// stays where it is when it has no such folder, so the two can drift apart and come back.
    /// </summary>
    public static class SyncBrowse
    {
        private static readonly char[] Separators = { '/', '\\' };

        /// <summary>
        /// Where the other panel goes when one panel moves from <paramref name="from"/> to
        /// <paramref name="to"/>, or null to stay. <paramref name="folderExists"/> says whether a
        /// folder is there (only asked about the folder it would go to).
        /// </summary>
        public static string? Follow(string from, string to, string other, Func<string, bool> folderExists)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(other)) return null;
            var a = Split(from, out _);
            var b = Split(to, out _);
            var o = Split(other, out string otherRoot);
            if (a.Root != b.Root) return null;

            string? target = null;
            if (b.Parts.Count > a.Parts.Count && StartsWith(b.Parts, a.Parts))
            {
                // Down: the same names below the other panel's folder.
                var below = b.Parts.Skip(a.Parts.Count);
                target = Join(otherRoot, o.Parts.Concat(below), other);
            }
            else if (b.Parts.Count < a.Parts.Count && StartsWith(a.Parts, b.Parts))
            {
                // Up: as many levels as this panel went, if the other panel is that deep.
                int up = a.Parts.Count - b.Parts.Count;
                if (o.Parts.Count < up) return null;
                target = Join(otherRoot, o.Parts.Take(o.Parts.Count - up), other);
            }
            if (target == null || SamePath(target, other)) return null;
            return folderExists(target) ? target : null;
        }

        private static (string Root, List<string> Parts) Split(string path, out string root)
        {
            // The root is everything up to the first folder name: "/", "C:\", "\\server\share\",
            // "cloud://remote/", "archive:///x.zip!/".
            string p = path;
            int start = 0;
            int marker = p.IndexOf("!/", StringComparison.Ordinal);
            int scheme = p.IndexOf("://", StringComparison.Ordinal);
            if (marker >= 0) start = marker + 2;
            else if (scheme >= 0) { int slash = p.IndexOf('/', scheme + 3); start = slash < 0 ? p.Length : slash + 1; }
            else if (p.StartsWith(@"\\", StringComparison.Ordinal))
            {
                int s1 = p.IndexOfAny(Separators, 2);
                int s2 = s1 < 0 ? -1 : p.IndexOfAny(Separators, s1 + 1);
                start = s2 < 0 ? p.Length : s2 + 1;
            }
            else if (p.Length >= 2 && p[1] == ':') start = p.Length > 2 && (p[2] is '/' or '\\') ? 3 : 2;
            else if (p.StartsWith('/')) start = 1;
            root = p.Substring(0, start);
            var parts = p.Substring(start).Split(Separators, StringSplitOptions.RemoveEmptyEntries).ToList();
            return (root, parts);
        }

        private static bool StartsWith(List<string> longer, List<string> prefix)
        {
            var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (int i = 0; i < prefix.Count; i++)
                if (!string.Equals(longer[i], prefix[i], cmp)) return false;
            return true;
        }

        private static string Join(string root, IEnumerable<string> parts, string styleOf)
        {
            char sep = styleOf.Contains('\\') && !styleOf.Contains("://") ? '\\' : '/';
            string body = string.Join(sep, parts);
            if (root.Length > 0 && !(root.EndsWith('/') || root.EndsWith('\\')) && body.Length > 0) root += sep;
            return root + body;
        }

        private static bool SamePath(string x, string y)
            => string.Equals(x.TrimEnd(Separators), y.TrimEnd(Separators),
                             OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}
