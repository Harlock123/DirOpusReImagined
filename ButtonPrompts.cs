using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DirOpusReImagined
{
    /// <summary>
    /// The <c>%ASK%</c> button token: a button's arguments can contain <c>%ASK%</c> or
    /// <c>%ASK:Question%</c>, and DORI asks for each one before running the command. The same
    /// question used twice is asked once and fills both places.
    /// </summary>
    public static class ButtonPrompts
    {
        private static readonly Regex Token = new(@"%ASK(?::([^%]*))?%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public const string DefaultQuestion = "Value";

        /// <summary>The questions to ask, in the order they first appear, each once.</summary>
        public static List<string> Questions(string? text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match m in Token.Matches(text))
            {
                string q = QuestionOf(m);
                if (!list.Contains(q)) list.Add(q);
            }
            return list;
        }

        /// <summary>The text with every <c>%ASK…%</c> replaced by the answer to its question.</summary>
        public static string Fill(string text, IReadOnlyDictionary<string, string> answers)
            => Token.Replace(text, m => answers.TryGetValue(QuestionOf(m), out var a) ? a : "");

        private static string QuestionOf(Match m)
        {
            string q = m.Groups[1].Success ? m.Groups[1].Value.Trim() : "";
            return q.Length == 0 ? DefaultQuestion : q;
        }
    }
}
