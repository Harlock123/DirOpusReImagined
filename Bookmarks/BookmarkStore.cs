using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DirOpusReImagined.Bookmarks
{
    /// <summary>A single named folder bookmark (local path or cloud:// URI).</summary>
    public sealed class Bookmark
    {
        public Bookmark(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; set; }
        public string Path { get; set; }
    }

    /// <summary>
    /// Loads and saves the user's folder bookmarks to <c>BOOKMARKS.MD</c> beside the
    /// application executable, so the file ships with the app and stays human-readable.
    ///
    /// The format is an ordinary Markdown list, one bookmark per line:
    /// <code>- [Name](path)</code>
    /// which renders as a clickable list in any Markdown viewer while remaining trivial
    /// to hand-edit.
    /// </summary>
    public static class BookmarkStore
    {
        private const string FileName = "BOOKMARKS.MD";

        // "- [Name](path)". Path is greedy up to the final ')', so paths that themselves
        // contain parentheses (e.g. "C:\Program Files (x86)\Foo") round-trip correctly.
        private static readonly Regex LinePattern =
            new(@"^\s*-\s*\[(?<name>[^\]]*)\]\((?<path>.*)\)\s*$", RegexOptions.Compiled);

        /// <summary>Full path to BOOKMARKS.MD in the application's own folder.</summary>
        public static string FilePath => System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

        /// <summary>
        /// Reads all bookmarks. Returns an empty list if the file is missing or unreadable,
        /// so callers never have to handle a null or an exception.
        /// </summary>
        public static List<Bookmark> Load()
        {
            var result = new List<Bookmark>();

            try
            {
                if (!File.Exists(FilePath)) return result;

                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var match = LinePattern.Match(line);
                    if (!match.Success) continue;

                    var name = match.Groups["name"].Value.Trim();
                    var path = match.Groups["path"].Value.Trim();
                    if (name.Length == 0 || path.Length == 0) continue;

                    result.Add(new Bookmark(name, path));
                }
            }
            catch
            {
                // A missing/locked/corrupt file simply yields no bookmarks.
            }

            return result;
        }

        /// <summary>
        /// Writes all bookmarks back to BOOKMARKS.MD. Returns false if the location is
        /// read-only (e.g. an installed copy under Program Files) rather than throwing.
        /// </summary>
        public static bool Save(IEnumerable<Bookmark> bookmarks)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# DirOpus Reimagined — Bookmarks");
                sb.AppendLine();
                sb.AppendLine("Folder bookmarks, one per line as `- [Name](path)`. Edit freely.");
                sb.AppendLine();

                foreach (var b in bookmarks)
                {
                    var name = (b.Name ?? string.Empty).Trim();
                    var path = (b.Path ?? string.Empty).Trim();
                    if (name.Length == 0 || path.Length == 0) continue;

                    sb.AppendLine($"- [{name}]({path})");
                }

                File.WriteAllText(FilePath, sb.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
