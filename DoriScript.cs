using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined
{
    /// <summary>What a script can ask of DORI. <see cref="MainWindow"/> is the real one; tests use a fake.</summary>
    public interface IScriptHost
    {
        /// <summary>The panel commands act on; false is the right panel.</summary>
        bool LeftActive { get; set; }
        string PanelPath(bool left);
        Task GoAsync(bool left, string path);
        Task NewTabAsync(bool left, string path);
        /// <summary>Selects the files (and folders) whose names match; returns how many are selected.</summary>
        int Select(bool left, string patterns, bool add);
        void SelectAll(bool left);
        void Deselect(bool left);
        void Invert(bool left);
        int SelectedCount(bool left);
        void Filter(bool left, string text);
        Task RefreshAsync(bool left);
        /// <summary>Runs a panel command by its name (the names &lt;Shortcuts&gt; uses); false when there's no such command.</summary>
        bool Command(bool left, string name);
        /// <summary>Runs a button by its label; false when no button has that label.</summary>
        bool Button(string label);
        Task RunProgramAsync(bool left, string commandLine);
        Task WaitForOperationsAsync(CancellationToken ct);
        Task MessageAsync(string text);
        bool Exists(string path);
    }

    /// <summary>One line of a script, or an if-block holding more.</summary>
    public sealed record ScriptStatement(int Line, string Verb, string Argument)
    {
        public List<ScriptStatement> Then { get; } = new();
        public List<ScriptStatement> Else { get; } = new();
    }

    /// <summary>A mistake in a script, with its line.</summary>
    public sealed class ScriptException(int line, string message) : Exception($"Line {line}: {message}")
    {
        public int Line { get; } = line;
    }

    /// <summary>
    /// DORI's small command language, after the ARexx port of the Amiga original: scripts and
    /// other programs tell a running DORI what to do (<c>DORI --cmd "go ~/Pictures; select *.jpg; copy"</c>,
    /// <c>DORI --script tidy.dori</c>), and buttons can run one (<c>%SCRIPT%</c>). One command per line,
    /// or several separated by <c>;</c>; <c>#</c> starts a comment.
    /// <code>
    /// left | right                 commands that follow act on that panel
    /// go PATH        tab PATH      go to a folder, or open it in a new tab
    /// select PATTERNS              select names matching *.jpg *.png (adds with "select +")
    /// select all | deselect | invert
    /// filter TEXT | filter         show only matching names, or everything again
    /// copy | move | delete         the selection, to the other panel (asking as usual)
    /// command NAME                 any panel command: FlatView, ExportListing, NewTab…
    /// button LABEL                 any button, by its label
    /// run PROGRAM ARGS             a program, in the panel's folder
    /// refresh | wait [SECONDS]     wait alone waits for copies and moves to finish
    /// message TEXT
    /// if exists PATH / if not exists PATH / if selected … else … end
    /// </code>
    /// In paths and text, %PANEL% is the panel's folder, %OTHER% the other one's, %LEFT% and %RIGHT%
    /// the two panels' folders, and ~ the home folder.
    /// </summary>
    public static class DoriScript
    {
        public static readonly string[] Verbs =
        {
            "left", "right", "go", "tab", "select", "deselect", "invert", "filter", "copy", "move", "delete",
            "command", "button", "run", "refresh", "wait", "message", "if", "else", "end",
        };

        /// <summary>Splits a script into statements, nesting if-blocks. Throws <see cref="ScriptException"/> for a mistake.</summary>
        public static List<ScriptStatement> Parse(string text)
        {
            var root = new List<ScriptStatement>();
            var stack = new Stack<(ScriptStatement If, bool InElse)>();
            List<ScriptStatement> Current() => stack.Count == 0 ? root : stack.Peek().InElse ? stack.Peek().If.Else : stack.Peek().If.Then;

            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var part in SplitStatements(lines[i]))
                {
                    string line = part.Trim();
                    if (line.Length == 0) continue;
                    int space = line.IndexOfAny(new[] { ' ', '\t' });
                    string verb = (space < 0 ? line : line[..space]).ToLowerInvariant();
                    string arg = space < 0 ? "" : line[(space + 1)..].Trim();
                    if (!Verbs.Contains(verb))
                        throw new ScriptException(i + 1, $"\"{verb}\" isn't a command. The commands are: {string.Join(", ", Verbs)}.");

                    var st = new ScriptStatement(i + 1, verb, Unquote(arg));
                    switch (verb)
                    {
                        case "if":
                            if (!IsCondition(arg)) throw new ScriptException(i + 1, "if needs \"exists PATH\", \"not exists PATH\" or \"selected\".");
                            Current().Add(st);
                            stack.Push((st, false));
                            break;
                        case "else":
                            if (stack.Count == 0 || stack.Peek().InElse) throw new ScriptException(i + 1, "else without an if.");
                            stack.Push((stack.Pop().If, true));
                            break;
                        case "end":
                            if (stack.Count == 0) throw new ScriptException(i + 1, "end without an if.");
                            stack.Pop();
                            break;
                        case "go" or "tab" or "command" or "button" or "run" or "message" when arg.Length == 0:
                            throw new ScriptException(i + 1, $"{verb} needs something after it.");
                        default:
                            Current().Add(st);
                            break;
                    }
                }
            }
            if (stack.Count > 0) throw new ScriptException(stack.Peek().If.Line, "this if has no end.");
            return root;
        }

        private static bool IsCondition(string arg)
        {
            string a = arg.Trim().ToLowerInvariant();
            return a == "selected" || a == "not selected" || a.StartsWith("exists ") || a.StartsWith("not exists ");
        }

        /// <summary>Splits on ';' outside quotes, and drops a '#' comment.</summary>
        public static IEnumerable<string> SplitStatements(string line)
        {
            var sb = new StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"') quoted = !quoted;
                if (!quoted && c == '#') break;
                if (!quoted && c == ';') { yield return sb.ToString(); sb.Clear(); continue; }
                sb.Append(c);
            }
            yield return sb.ToString();
        }

        private static string Unquote(string s)
            => s.Length >= 2 && s[0] == '"' && s[^1] == '"' && s.IndexOf('"', 1) == s.Length - 1 ? s[1..^1] : s;

        /// <summary>Puts in %PANEL%, %OTHER%, %LEFT%, %RIGHT% and a leading ~.</summary>
        public static string Expand(string text, IScriptHost host)
        {
            string left = host.PanelPath(true), right = host.PanelPath(false);
            string r = text.Replace("%PANEL%", host.LeftActive ? left : right, StringComparison.OrdinalIgnoreCase)
                           .Replace("%OTHER%", host.LeftActive ? right : left, StringComparison.OrdinalIgnoreCase)
                           .Replace("%LEFT%", left, StringComparison.OrdinalIgnoreCase)
                           .Replace("%RIGHT%", right, StringComparison.OrdinalIgnoreCase);
            if (r == "~" || r.StartsWith("~/") || r.StartsWith("~\\"))
                r = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + r[1..];
            return r;
        }

        /// <summary>Runs statements one after another. <paramref name="log"/> hears what each did.</summary>
        public static async Task RunAsync(IReadOnlyList<ScriptStatement> statements, IScriptHost host, Action<string>? log = null, CancellationToken ct = default)
        {
            foreach (var st in statements)
            {
                ct.ThrowIfCancellationRequested();
                await RunOneAsync(st, host, log, ct);
            }
        }

        private static async Task RunOneAsync(ScriptStatement st, IScriptHost host, Action<string>? log, CancellationToken ct)
        {
            bool left = host.LeftActive;
            string arg = Expand(st.Argument, host);
            switch (st.Verb)
            {
                case "left": host.LeftActive = true; break;
                case "right": host.LeftActive = false; break;
                case "go": await host.GoAsync(left, arg); log?.Invoke($"went to {arg}"); break;
                case "tab": await host.NewTabAsync(left, arg); log?.Invoke($"opened {arg} in a new tab"); break;
                case "select":
                    if (arg.Equals("all", StringComparison.OrdinalIgnoreCase)) host.SelectAll(left);
                    else
                    {
                        bool add = arg.StartsWith('+');
                        int n = host.Select(left, add ? arg[1..].Trim() : arg, add);
                        log?.Invoke($"{n} selected");
                    }
                    break;
                case "deselect": host.Deselect(left); break;
                case "invert": host.Invert(left); break;
                case "filter": host.Filter(left, arg); break;
                case "copy": Need(host.Command(left, "Copy"), st); break;
                case "move": Need(host.Command(left, "Move"), st); break;
                case "delete": Need(host.Command(left, "Delete"), st); break;
                case "command":
                    if (!host.Command(left, arg)) throw new ScriptException(st.Line, $"there's no panel command called {arg}.");
                    break;
                case "button":
                    if (!host.Button(arg)) throw new ScriptException(st.Line, $"no button is labelled {arg}.");
                    break;
                case "run": await host.RunProgramAsync(left, arg); break;
                case "refresh": await host.RefreshAsync(left); break;
                case "wait":
                    if (arg.Length == 0) await host.WaitForOperationsAsync(ct);
                    else if (double.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sec) && sec >= 0)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(sec, 3600)), ct);
                    else throw new ScriptException(st.Line, "wait takes a number of seconds, or nothing.");
                    break;
                case "message": await host.MessageAsync(arg); break;
                case "if":
                    bool yes = Condition(arg, host, left);
                    await RunAsync(yes ? st.Then : st.Else, host, log, ct);
                    break;
            }
        }

        private static void Need(bool ok, ScriptStatement st)
        {
            if (!ok) throw new ScriptException(st.Line, $"{st.Verb} couldn't be done.");
        }

        private static bool Condition(string arg, IScriptHost host, bool left)
        {
            string a = arg.Trim();
            bool not = a.StartsWith("not ", StringComparison.OrdinalIgnoreCase);
            if (not) a = a[4..].Trim();
            bool result = a.Equals("selected", StringComparison.OrdinalIgnoreCase)
                ? host.SelectedCount(left) > 0
                : host.Exists(Unquote(a[7..].Trim()));    // "exists PATH"
            return not ? !result : result;
        }
    }
}
