using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>One panel's part of a layout: its tabs, which is showing, its hidden columns and its view.</summary>
    public sealed record PanelLayout(IReadOnlyList<string> Tabs, int ActiveTab, IReadOnlyList<string> HiddenColumns, GridViewMode View);

    /// <summary>A named arrangement of both panels and the button bank, to switch back to in one go.</summary>
    public sealed record SavedLayout(string Name, PanelLayout Left, PanelLayout Right, string? ButtonBank);

    /// <summary>
    /// Saved layouts in Configuration.xml:
    /// <code>
    /// &lt;Layouts&gt;
    ///   &lt;Layout name="Photo sorting" bank="Tools"&gt;
    ///     &lt;Left active="0" columns="Dirs,Files" view="Thumbnails"&gt;&lt;Tab&gt;/home/me/Pictures&lt;/Tab&gt;&lt;/Left&gt;
    ///     &lt;Right active="0" columns="" view="List"&gt;&lt;Tab&gt;/home/me/Sorted&lt;/Tab&gt;&lt;/Right&gt;
    ///   &lt;/Layout&gt;
    /// &lt;/Layouts&gt;
    /// </code>
    /// </summary>
    public static class Layouts
    {
        public const string ElementName = "Layouts";

        public static List<SavedLayout> Read(XElement? root)
        {
            var list = new List<SavedLayout>();
            foreach (var el in root?.Element(ElementName)?.Elements("Layout") ?? Enumerable.Empty<XElement>())
            {
                string name = ((string?)el.Attribute("name") ?? "").Trim();
                var left = ReadPanel(el.Element("Left"));
                var right = ReadPanel(el.Element("Right"));
                if (name.Length == 0 || left == null || right == null) continue;
                string? bank = (string?)el.Attribute("bank");
                list.Add(new SavedLayout(name, left, right, string.IsNullOrWhiteSpace(bank) ? null : bank));
            }
            return list;
        }

        private static PanelLayout? ReadPanel(XElement? el)
        {
            if (el == null) return null;
            var tabs = el.Elements("Tab").Select(t => t.Value).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (tabs.Count == 0) return null;
            int.TryParse((string?)el.Attribute("active"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int active);
            if (active < 0 || active >= tabs.Count) active = 0;
            var hidden = MainWindow.ParseHiddenColumns((string?)el.Attribute("columns")).ToList();
            var view = Enum.TryParse((string?)el.Attribute("view"), true, out GridViewMode v) ? v : GridViewMode.List;
            return new PanelLayout(tabs, active, hidden, view);
        }

        /// <summary>Replaces the &lt;Layouts&gt; section with <paramref name="layouts"/> (removing it when there are none).</summary>
        public static void Write(XElement root, IEnumerable<SavedLayout> layouts)
        {
            root.Element(ElementName)?.Remove();
            var list = layouts.ToList();
            if (list.Count == 0) return;
            root.Add(new XElement(ElementName, list.Select(l =>
            {
                var el = new XElement("Layout", new XAttribute("name", l.Name),
                                      WritePanel("Left", l.Left), WritePanel("Right", l.Right));
                if (l.ButtonBank != null) el.Add(new XAttribute("bank", l.ButtonBank));
                return el;
            })));
        }

        private static XElement WritePanel(string name, PanelLayout p)
            => new(name,
                   new XAttribute("active", p.ActiveTab.ToString(CultureInfo.InvariantCulture)),
                   new XAttribute("columns", MainWindow.FormatHiddenColumns(p.HiddenColumns)),
                   new XAttribute("view", p.View.ToString()),
                   p.Tabs.Select(t => new XElement("Tab", t)));

        /// <summary>
        /// Adds <paramref name="layout"/>, replacing one of the same name (ignoring case) in its place,
        /// or adding it at the end. Returns the new list.
        /// </summary>
        public static List<SavedLayout> Upsert(IEnumerable<SavedLayout> layouts, SavedLayout layout)
        {
            var list = layouts.ToList();
            int i = list.FindIndex(l => string.Equals(l.Name, layout.Name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = layout; else list.Add(layout);
            return list;
        }

        /// <summary>
        /// The panel as it can be restored now: only tabs that still exist (<paramref name="exists"/>),
        /// with the showing tab kept if it survived. Null if none survived.
        /// </summary>
        public static PanelLayout? Usable(PanelLayout panel, Func<string, bool> exists)
        {
            var kept = panel.Tabs.Select((t, i) => (t, i)).Where(x => exists(x.t)).ToList();
            if (kept.Count == 0) return null;
            int active = Math.Max(0, kept.FindIndex(x => x.i == panel.ActiveTab));
            return panel with { Tabs = kept.Select(x => x.t).ToList(), ActiveTab = active };
        }
    }
}
