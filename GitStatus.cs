using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined
{
    /// <summary>
    /// Git status for the files a panel shows: inside a git work tree each row gets a word in the
    /// Git column (Modified, Staged, Untracked, Ignored, Conflict…), and a folder shows the
    /// strongest state of what is inside it. Asks the <c>git</c> program, so nothing happens when
    /// git isn't installed or the folder isn't in a repository.
    /// </summary>
    public static class GitStatus
    {
        /// <summary>One line of <c>git status --porcelain -z</c>: the two status letters and the path from the repository root.</summary>
        public readonly record struct Entry(char Index, char WorkTree, string Path)
        {
            public bool IsDirectory => Path.EndsWith('/');
        }

        /// <summary>Reads <c>git status --porcelain=v1 -z</c> output. A rename's second (old) path is skipped.</summary>
        public static List<Entry> Parse(string output)
        {
            var list = new List<Entry>();
            var parts = output.Split('\0');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length < 4 || p[2] != ' ') continue;
                list.Add(new Entry(p[0], p[1], p.Substring(3)));
                if (p[0] is 'R' or 'C') i++;   // "R  new\0old\0"
            }
            return list;
        }

        /// <summary>The word shown for one status, or "" for none.</summary>
        public static string Word(char x, char y)
        {
            if (x == '!' && y == '!') return "Ignored";
            if (x == '?' && y == '?') return "Untracked";
            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D')) return "Conflict";
            if (y is 'M' or 'T' or 'D') return "Modified";
            if (x == 'R') return "Renamed";
            if (x == 'A') return "Added";
            if (x is 'M' or 'T' or 'D' or 'C') return "Staged";
            return "";
        }

        // Which word wins when a folder holds several: the one most worth noticing.
        private static int Rank(string word) => word switch
        {
            "Conflict" => 6, "Modified" => 5, "Staged" => 4, "Renamed" => 4, "Added" => 4, "Untracked" => 3, "Ignored" => 1, _ => 0,
        };

        /// <summary>
        /// The word for each of <paramref name="names"/> (as a panel lists them: "file", "folder", or
        /// in flat view "sub/file") in the folder <paramref name="prefix"/> ("sub/dir/", "" at the
        /// root, as <c>git rev-parse --show-prefix</c> gives it). Names with nothing to say are left out.
        /// </summary>
        public static Dictionary<string, string> ForNames(IReadOnlyList<Entry> entries, string prefix, IEnumerable<string> names)
        {
            prefix = prefix.Replace('\\', '/');
            if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";

            // Each status path made relative to the panel's folder.
            var rel = entries.Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal))
                             .Select(e => (Path: e.Path.Substring(prefix.Length), Word: Word(e.Index, e.WorkTree), e.IsDirectory))
                             .Where(e => e.Word.Length > 0 && e.Path.Length > 0)
                             .ToList();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (rel.Count == 0) return result;
            foreach (var raw in names)
            {
                string name = raw.Replace('\\', '/').TrimEnd('/');
                string best = "";
                foreach (var e in rel)
                {
                    string path = e.Path.TrimEnd('/');
                    string word;
                    if (path == name) word = e.Word;                                              // the item itself
                    else if (e.IsDirectory && name.StartsWith(path + "/", StringComparison.Ordinal)) word = e.Word;  // inside an untracked/ignored folder
                    else if (path.StartsWith(name + "/", StringComparison.Ordinal))                // something inside this folder
                        word = e.Word == "Ignored" ? "" : e.Word;
                    else continue;
                    if (Rank(word) > Rank(best)) best = word;
                }
                if (best.Length > 0) result[raw] = best;
            }
            return result;
        }

        /// <summary>
        /// Runs git for <paramref name="folder"/> and gives the word for each name, or null when the
        /// folder isn't in a work tree, git isn't installed, or it took longer than a few seconds.
        /// </summary>
        public static async Task<Dictionary<string, string>?> ForFolderAsync(string folder, IReadOnlyList<string> names, CancellationToken ct = default)
        {
            if (!Directory.Exists(folder) || !InsideWorkTree(folder)) return null;
            string? prefix = await RunGitAsync(folder, ct, "rev-parse", "--show-prefix").ConfigureAwait(false);
            if (prefix == null) return null;
            string? status = await RunGitAsync(folder, ct, "status", "--porcelain=v1", "-z", "--ignored=matching",
                                               "--untracked-files=normal", "--", ".").ConfigureAwait(false);
            if (status == null) return null;
            return ForNames(Parse(status), prefix.Trim(), names);
        }

        /// <summary>Whether a <c>.git</c> folder or file is in <paramref name="folder"/> or above it, without starting git.</summary>
        public static bool InsideWorkTree(string folder)
        {
            try
            {
                for (var d = new DirectoryInfo(folder); d != null; d = d.Parent)
                {
                    string git = Path.Combine(d.FullName, ".git");
                    if (Directory.Exists(git) || File.Exists(git)) return true;
                }
            }
            catch { }
            return false;
        }

        private static async Task<string?> RunGitAsync(string folder, CancellationToken ct, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                psi.ArgumentList.Add("-C");
                psi.ArgumentList.Add(folder);
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("core.quotepath=off");
                foreach (var a in args) psi.ArgumentList.Add(a);
                psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";   // don't take the index lock just to look

                using var p = Process.Start(psi);
                if (p == null) return null;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var read = p.StandardOutput.ReadToEndAsync(timeout.Token);
                _ = p.StandardError.ReadToEndAsync(timeout.Token);
                try { await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { try { p.Kill(true); } catch { } return null; }
                string text = await read.ConfigureAwait(false);
                return p.ExitCode == 0 ? text : null;
            }
            catch { return null; }   // git not installed
        }
    }
}
