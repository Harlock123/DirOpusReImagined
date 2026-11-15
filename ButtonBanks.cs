using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>One named set of up to 36 command buttons; the panel at the bottom shows one bank at a time.</summary>
    public sealed class ButtonBank
    {
        public ButtonBank(string name, List<ButtonSettings>? buttons = null)
        {
            Name = name;
            Buttons = buttons ?? new List<ButtonSettings>();
        }

        public string Name { get; set; }
        public List<ButtonSettings> Buttons { get; }

        public ButtonBank Clone() => new(Name, Buttons.Select(b => b.Clone()).ToList());
    }

    /// <summary>
    /// Reading and writing banks of command buttons in Configuration.xml. Each bank is a
    /// &lt;Buttons name="…"&gt; element under the root, in order. A file from before banks existed has a
    /// single unnamed &lt;Buttons&gt;, which reads as one bank, so old files keep working unchanged.
    /// </summary>
    public static class ButtonBanks
    {
        public const int ButtonsPerBank = 36;
        public const string ActiveElement = "ActiveButtonBank";

        /// <summary>Every bank in the file, in order; never empty (a file with none gives one empty bank).</summary>
        public static List<ButtonBank> Read(XElement? root)
        {
            var banks = new List<ButtonBank>();
            if (root != null)
            {
                foreach (var el in root.Elements("Buttons"))
                {
                    string name = ((string?)el.Attribute("name"))?.Trim() ?? "";
                    if (name.Length == 0) name = DefaultName(banks.Count);
                    banks.Add(new ButtonBank(name, el.Elements("Button").Select(ReadButton).ToList()));
                }
            }
            if (banks.Count == 0) banks.Add(new ButtonBank(DefaultName(0)));
            return banks;
        }

        /// <summary>
        /// Replaces every &lt;Buttons&gt; element under <paramref name="root"/> with <paramref name="banks"/>,
        /// where the first one was (or at the end). Everything else in the file is left alone.
        /// </summary>
        public static void Write(XElement root, IReadOnlyList<ButtonBank> banks)
        {
            var old = root.Elements("Buttons").ToList();
            var written = banks.Select(b => new XElement("Buttons",
                new XAttribute("name", b.Name),
                b.Buttons.Select(WriteButton))).ToList();

            if (old.Count > 0) old[0].AddBeforeSelf(written);
            else root.AddFirst(written);
            foreach (var el in old) el.Remove();
        }

        /// <summary>The saved bank to show at start-up, or the first if none was saved or it is out of range.</summary>
        public static int ReadActive(XElement? root, int count)
        {
            string? raw = (string?)root?.Element(ActiveElement);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < count ? i : 0;
        }

        public static void WriteActive(XElement root, int index)
        {
            var el = root.Element(ActiveElement);
            if (el == null) root.Add(el = new XElement(ActiveElement));
            el.Value = index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Steps through the banks, wrapping round at either end.</summary>
        public static int Step(int index, int delta, int count)
            => count <= 0 ? 0 : ((index + delta) % count + count) % count;

        public static string DefaultName(int index) => $"Bank {index + 1}";

        /// <summary>"Bank n" for the lowest n not already used.</summary>
        public static string NewName(IEnumerable<string> existing)
        {
            var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            for (int n = 0; ; n++)
                if (!taken.Contains(DefaultName(n))) return DefaultName(n);
        }

        /// <summary>
        /// What hovering over a command button shows: its full label when the label is cut short
        /// (<paramref name="trimmedLabel"/> is null when it fits), then its own tooltip. Null for nothing.
        /// </summary>
        public static string? HoverText(string? trimmedLabel, string? toolTip)
        {
            bool hasLabel = !string.IsNullOrWhiteSpace(trimmedLabel);
            bool hasTip = !string.IsNullOrWhiteSpace(toolTip);
            if (hasLabel && hasTip) return trimmedLabel + "\n\n" + toolTip;
            if (hasLabel) return trimmedLabel;
            return hasTip ? toolTip : null;
        }

        /// <summary>The button position (1 to 36) a saved name such as "LpButton12" or "LPButton12" refers to; 0 if none.</summary>
        public static int SlotOf(string? name)
        {
            const string prefix = "LPButton";
            if (name == null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;
            return int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                   && n >= 1 && n <= ButtonsPerBank ? n : 0;
        }

        // A missing element reads as null, as it always has: the loader and editor treat null as "not set".
        private static ButtonSettings ReadButton(XElement btn) => new()
        {
            Name = (string?)btn.Element("Name"),
            Content = (string?)btn.Element("Content"),
            Background = (string?)btn.Element("Background"),
            Foreground = (string?)btn.Element("Foreground"),
            HorizontalAlignment = (string?)btn.Element("HorizontalAlignment"),
            VerticalAlignment = (string?)btn.Element("VerticalAlignment"),
            Margin = (string?)btn.Element("Margin"),
            Action = (string?)btn.Element("Action"),
            Args = (string?)btn.Element("Args"),
            ShellExecute = (string?)btn.Element("ShellExecute"),
            ShowWindow = (string?)btn.Element("ShowWindow"),
            ToolTip = (string?)btn.Element("ToolTip"),
        };

        // Same element order as before banks; null fields are left out, as the old serializer did.
        private static XElement WriteButton(ButtonSettings b)
        {
            var el = new XElement("Button");
            void Add(string field, string? value) { if (value != null) el.Add(new XElement(field, value)); }
            Add("Name", b.Name);
            Add("Content", b.Content);
            Add("Background", b.Background);
            Add("Foreground", b.Foreground);
            Add("HorizontalAlignment", b.HorizontalAlignment);
            Add("VerticalAlignment", b.VerticalAlignment);
            Add("Margin", b.Margin ?? "2,2,2,2");
            Add("Action", b.Action);
            Add("Args", b.Args);
            Add("ShellExecute", b.ShellExecute);
            Add("ShowWindow", b.ShowWindow);
            Add("ToolTip", b.ToolTip);
            return el;
        }
    }
}
