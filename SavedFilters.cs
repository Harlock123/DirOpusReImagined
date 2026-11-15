using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>A filter kept for reuse: a name to pick it by, and the pattern(s) it applies.</summary>
    public sealed record SavedFilter(string Name, string Pattern)
    {
        /// <summary>"Images   *.jpg;*.png", or just the pattern when it is its own name.</summary>
        public string MenuLabel => string.Equals(Name, Pattern, StringComparison.Ordinal) ? Pattern : $"{Name}   {Pattern}";
    }

    /// <summary>
    /// The text in a panel's filter box, and the filters saved for reuse (shared by both panels,
    /// kept in Configuration.xml under &lt;SavedFilters&gt;).
    /// </summary>
    public static class SavedFilters
    {
        public const string ElementName = "SavedFilters";

        /// <summary>
        /// Whether a name passes the filter text. Several patterns can be given separated by ";"
        /// (<c>*.cs;*.axaml</c>), and a name passes if it matches any. Each is a wildcard pattern
        /// when it has * or ?, otherwise text the name must contain. Case is ignored.
        /// </summary>
        public static Func<string, bool> Matcher(string filterText)
        {
            var matchers = filterText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                     .Select(p => FileSearch.MakeNameMatcher(p, matchCase: false))
                                     .ToList();
            if (matchers.Count == 0) return _ => true;
            return name => matchers.Any(m => m.IsMatch(name));
        }

        public static List<SavedFilter> Read(XElement? root)
            => (root?.Element(ElementName)?.Elements("Filter") ?? Enumerable.Empty<XElement>())
               .Select(e => new SavedFilter((((string?)e.Attribute("name")) ?? "").Trim(), e.Value.Trim()))
               .Where(f => f.Pattern.Length > 0)
               .Select(f => f.Name.Length == 0 ? f with { Name = f.Pattern } : f)
               .ToList();

        public static void Write(XElement root, IEnumerable<SavedFilter> filters)
        {
            root.Element(ElementName)?.Remove();
            var list = filters.ToList();
            if (list.Count == 0) return;
            root.Add(new XElement(ElementName, list.Select(f => new XElement("Filter", new XAttribute("name", f.Name), f.Pattern))));
        }

        /// <summary>Adds <paramref name="filter"/>, replacing one with the same name (ignoring case) in its place.</summary>
        public static List<SavedFilter> Upsert(IEnumerable<SavedFilter> filters, SavedFilter filter)
        {
            var list = filters.ToList();
            int i = list.FindIndex(f => string.Equals(f.Name, filter.Name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = filter; else list.Add(filter);
            return list;
        }
    }
}
