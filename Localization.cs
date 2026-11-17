using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace DirOpusReImagined
{
    /// <summary>
    /// The menus, buttons and dialogs in another language. Every window is translated as it
    /// opens: each piece of text that exactly matches an English phrase in <see cref="Translations"/>
    /// is replaced, including right-click menus and tooltips. Text not in the table (file names,
    /// messages put together while DORI runs, the help pages) stays English. The language is the
    /// &lt;Language&gt; setting (en, de, fr, es); it takes full effect when DORI next starts.
    /// </summary>
    public static class Localization
    {
        public const string Setting = "Language";

        public static readonly (string Code, string Name)[] Languages =
        {
            ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
        };

        private static Dictionary<string, string>? _table;
        public static string Current { get; private set; } = "en";

        /// <summary>Reads the setting and, for a language other than English, translates each window as it opens.</summary>
        public static void Install()
        {
            Use(ConfigSettings.Load(Setting) ?? "en");
            Control.LoadedEvent.AddClassHandler<Window>((w, _) => Translate(w));
        }

        /// <summary>Picks the table for <paramref name="code"/> (English, or an unknown code, means none).</summary>
        public static void Use(string code)
        {
            code = (code ?? "en").Trim().ToLowerInvariant();
            int column = Array.FindIndex(Languages, l => l.Code == code);
            Current = column < 0 ? "en" : code;
            _table = column <= 0 ? null : Translations.Table.ToDictionary(r => r[0], r => r[column], StringComparer.Ordinal);
        }

        /// <summary>The text in the current language, or as it is when there's no translation.</summary>
        public static string T(string english)
        {
            if (_table == null || string.IsNullOrEmpty(english)) return english;
            if (_table.TryGetValue(english, out var t)) return t;
            // "Find" and "Find…" are the same phrase.
            if (english.EndsWith('…') && _table.TryGetValue(english[..^1], out var u)) return u + "…";
            return english;
        }

        /// <summary>Translates a window, its controls, their menus and tooltips.</summary>
        public static void Translate(Control root)
        {
            if (_table == null) return;
            if (root is Window w && w.Title is { } title) w.Title = T(title);
            foreach (var c in new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>()))
                TranslateControl(c);
        }

        private static void TranslateControl(Control c)
        {
            switch (c)
            {
                case TextBlock tb when tb.Text is { } text: tb.Text = T(text); break;
                case TextBox box when box.Watermark is { } wm: box.Watermark = T(wm); break;
                case HeaderedSelectingItemsControl h when h.Header is string hs: h.Header = T(hs); break;
                case HeaderedContentControl hc when hc.Header is string hcs: hc.Header = T(hcs); break;
                case ContentControl cc when cc.Content is string s: cc.Content = T(s); break;
            }
            if (ToolTip.GetTip(c) is string tip) ToolTip.SetTip(c, T(tip));
            if (c.ContextMenu is { } menu) TranslateItems(menu.Items);
            if (c is Button { Flyout: MenuFlyout flyout }) TranslateItems(flyout.Items);
        }

        private static void TranslateItems(IEnumerable<object?> items)
        {
            foreach (var item in items)
            {
                if (item is not MenuItem mi) continue;
                if (mi.Header is string h) mi.Header = T(h);
                if (ToolTip.GetTip(mi) is string tip) ToolTip.SetTip(mi, T(tip));
                TranslateItems(mi.Items);
            }
        }
    }
}
