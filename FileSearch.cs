using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>A single search match.</summary>
    /// <param name="Folder">The folder the match was found in (where to navigate for a file hit).</param>
    /// <param name="FullPath">Full path / cloud URI of the matched item.</param>
    public sealed record SearchHit(string Folder, string FullPath, string Name, bool IsDir, long Size, DateTime Modified);

    public sealed record SearchOptions(bool MatchCase, bool IncludeFolders);

    /// <summary>
    /// Recursive name search rooted at a folder, reading through <see cref="IFileProvider"/> so it
    /// works for local and cloud paths alike. The pattern is a case-insensitive substring by default;
    /// if it contains <c>*</c> or <c>?</c> it is treated as a wildcard matched against the whole name.
    /// Inaccessible folders are skipped rather than aborting the search.
    /// </summary>
    public static class FileSearch
    {
        public static void Search(string root, string pattern, SearchOptions opt,
            Action<SearchHit> onHit, CancellationToken ct, IProgress<string>? progress = null)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrWhiteSpace(pattern)) return;
            var matcher = MakeNameMatcher(pattern, opt.MatchCase);
            SearchInto(root, matcher, opt, onHit, ct, progress);
        }

        /// <summary>
        /// Builds a name matcher: a wildcard match against the whole name when the pattern contains
        /// <c>*</c> or <c>?</c> (e.g. <c>*.jpg</c>), otherwise a substring match (e.g. <c>jpg</c>).
        /// Shared by the recursive search and the per-panel filter box.
        /// </summary>
        public static Regex MakeNameMatcher(string pattern, bool matchCase = false)
        {
            var opts = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0)
                return new Regex(Regex.Escape(pattern), opts); // substring (contains)

            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(rx, opts);
        }

        private static void SearchInto(string root, Regex matcher, SearchOptions opt,
            Action<SearchHit> onHit, CancellationToken ct, IProgress<string>? progress)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(root);

            var provider = ProviderRegistry.For(root);
            List<FileEntry> dirs, files;
            try
            {
                dirs = provider.EnumerateDirectories(root).ToList();
                files = provider.EnumerateFiles(root).ToList();
            }
            catch
            {
                return; // inaccessible folder (permissions) — skip it
            }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (matcher.IsMatch(f.Name))
                    onHit(new SearchHit(root, f.Path, f.Name, false, f.Size, f.LastModified));
            }

            foreach (var d in dirs)
            {
                ct.ThrowIfCancellationRequested();
                if (opt.IncludeFolders && matcher.IsMatch(d.Name))
                    onHit(new SearchHit(root, d.Path, d.Name, true, 0, d.LastModified));

                SearchInto(d.Path, matcher, opt, onHit, ct, progress);
            }
        }
    }
}
