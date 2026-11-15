using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>
    /// Folders recently shown in either panel, newest first, without duplicates. Kept to
    /// <see cref="MaxItems"/> and saved in Configuration.xml as
    /// <c>&lt;RecentFolders&gt;&lt;Folder&gt;…&lt;/Folder&gt;&lt;/RecentFolders&gt;</c>.
    /// </summary>
    public sealed class RecentFolders
    {
        public const int MaxItems = 25;
        public const string ElementName = "RecentFolders";

        private readonly List<string> _items = new();
        private readonly StringComparison _comparison;

        /// <param name="ignoreCase">Treat paths differing only in case as the same folder
        /// (Windows and macOS). Defaults to what the current platform's file system does.</param>
        public RecentFolders(bool? ignoreCase = null)
        {
            _comparison = ignoreCase ?? !OperatingSystem.IsLinux()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        /// <summary>Newest first.</summary>
        public IReadOnlyList<string> Items => _items;

        /// <summary>Moves <paramref name="path"/> to the top, adding it if it's new.</summary>
        public void Visit(string path)
        {
            string p = Normalize(path);
            if (p.Length == 0) return;
            Remove(p);
            _items.Insert(0, p);
            if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }

        public bool Remove(string path)
        {
            string p = Normalize(path);
            int i = _items.FindIndex(x => Same(x, p));
            if (i < 0) return false;
            _items.RemoveAt(i);
            return true;
        }

        public void Clear() => _items.Clear();

        /// <summary>
        /// Adds saved folders below the ones already here, skipping any already listed. Used at
        /// start-up, when the panels may have visited folders before the saved list is read.
        /// </summary>
        public void AddOlder(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (_items.Count >= MaxItems) break;
                string p = Normalize(path);
                if (p.Length == 0 || _items.Any(x => Same(x, p))) continue;
                _items.Add(p);
            }
        }

        /// <summary>The list, newest first, minus <paramref name="exclude"/> (the folder a panel is already in).</summary>
        public IEnumerable<string> Except(string? exclude)
        {
            string e = Normalize(exclude ?? "");
            return _items.Where(x => !Same(x, e));
        }

        public XElement ToXml() => new(ElementName, _items.Select(p => new XElement("Folder", p)));

        /// <summary>The saved folders in <paramref name="settingsRoot"/>, or none.</summary>
        public static IEnumerable<string> FromXml(XElement? settingsRoot)
            => settingsRoot?.Element(ElementName)?.Elements("Folder").Select(e => e.Value) ?? Enumerable.Empty<string>();

        private bool Same(string a, string b) => string.Equals(a, b, _comparison);

        /// <summary>
        /// Trims a trailing separator so "/home/me/" and "/home/me" are one entry, but keeps a root
        /// ("/", "C:\"). Provider URIs (cloud://, archive://) are kept exactly, since their trailing
        /// "/" can be significant (an archive's root is "…zip!/").
        /// </summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string p = path.Trim();
            if (p.Contains("://")) return p;

            string trimmed = p.TrimEnd('/', '\\');
            if (trimmed.Length == 0) return p[..1];                                   // "/" stays "/"
            if (trimmed.Length == 2 && trimmed[1] == ':') return trimmed + "\\";      // "C:" or "C:\" → "C:\"
            return trimmed;
        }
    }
}
