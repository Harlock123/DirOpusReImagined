using System;
using System.Collections.Generic;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>One thing the command palette can run: a command, a button, a layout, a filter…</summary>
    public sealed record PaletteItem(string Group, string Title, string Keys, Action Run)
    {
        public string Text => $"{Group}: {Title}";
    }

    /// <summary>Choosing what the command palette shows for what's typed. Pure, so it is tested without a window.</summary>
    public static class CommandPalette
    {
        /// <summary>
        /// The items matching <paramref name="query"/>, best first. Every word typed must appear in
        /// the item (its group or title, ignoring case); items whose title starts with a word, or
        /// has a word starting with it, rank above ones that merely contain it. Ties keep their order.
        /// </summary>
        public static List<PaletteItem> Rank(IReadOnlyList<PaletteItem> items, string query)
        {
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0) return items.ToList();

            return items
                .Select((item, index) => (item, index, score: Score(item, words)))
                .Where(x => x.score >= 0)
                .OrderBy(x => x.score)
                .ThenBy(x => x.index)
                .Select(x => x.item)
                .ToList();
        }

        /// <summary>Lower is better; -1 when a word isn't found at all.</summary>
        private static int Score(PaletteItem item, string[] words)
        {
            int total = 0;
            foreach (var w in words)
            {
                int s = WordScore(item, w);
                if (s < 0) return -1;
                total += s;
            }
            return total;
        }

        private static int WordScore(PaletteItem item, string word)
        {
            const StringComparison ic = StringComparison.OrdinalIgnoreCase;
            string title = item.Title;
            if (title.StartsWith(word, ic)) return 0;
            int at = title.IndexOf(word, ic);
            while (at > 0)
            {
                if (!char.IsLetterOrDigit(title[at - 1])) return 1;   // starts a word: "fla" in "Flat view"
                at = title.IndexOf(word, at + 1, ic);
            }
            if (title.Contains(word, ic)) return 2;
            if (item.Group.Contains(word, ic) || item.Keys.Contains(word, ic)) return 3;
            return -1;
        }
    }
}
