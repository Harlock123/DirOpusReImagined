using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>The path arithmetic for the folder tree: which root a folder is under, and the names leading down to it.</summary>
    public static class FolderTreePaths
    {
        private static StringComparison Cmp => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        /// <summary>The root (of <paramref name="roots"/>) that holds <paramref name="path"/>, the longest one when several do (home inside /), or null.</summary>
        public static string? RootOf(string path, IEnumerable<string> roots)
            => roots.Where(r => IsUnder(path, r)).OrderByDescending(r => r.Length).FirstOrDefault();

        public static bool IsUnder(string path, string root)
        {
            string r = Trim(root), p = Trim(path);
            if (string.Equals(p, r, Cmp)) return true;
            string withSep = r.EndsWith('/') || r.EndsWith('\\') ? r : r + Path.DirectorySeparatorChar;
            return p.StartsWith(withSep, Cmp) || (Path.DirectorySeparatorChar == '\\' && p.StartsWith(r + "/", Cmp));
        }

        /// <summary>The folder names from <paramref name="root"/> down to <paramref name="path"/> ("/home/me/a/b" under "/home/me" → a, b).</summary>
        public static List<string> StepsBelow(string root, string path)
        {
            string r = Trim(root), p = Trim(path);
            if (string.Equals(p, r, Cmp)) return new List<string>();
            string rest = p.Substring(r.Length).TrimStart('/', '\\');
            return rest.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>"/" and "C:\" stay as they are; anything else loses a trailing separator.</summary>
        public static string Trim(string p)
            => p.Length <= 1 || (p.Length == 3 && p[1] == ':') ? p : p.TrimEnd('/', '\\');
    }
}
