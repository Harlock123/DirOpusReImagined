using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Files with identical contents: their size, and each copy.</summary>
    public sealed record DuplicateGroup(long Size, IReadOnlyList<FileEntry> Files)
    {
        /// <summary>Bytes freed by deleting every copy but one.</summary>
        public long Wasted => Size * (Files.Count - 1);
    }

    public enum KeepRule { Oldest, Newest, ShortestPath }

    /// <summary>
    /// Finds files with identical contents. Cheap checks first: only files of the same size can
    /// match, then only those whose first 64 KB hash the same, and only those are read in full
    /// (SHA-256). Empty files are left out (they all "match" and free nothing).
    /// </summary>
    public static class DuplicateFinder
    {
        public const int PartialBytes = 64 * 1024;

        /// <summary>Progress: what is happening ("Comparing…") and how far through it is.</summary>
        public sealed record Progress(string Stage, int Done, int Total);

        /// <summary>
        /// Every file below the <paramref name="roots"/> (never inside linked folders; hidden ones
        /// only when <paramref name="includeHidden"/>), each listed once even when the roots overlap.
        /// </summary>
        public static List<FileEntry> CollectFiles(IEnumerable<string> roots, bool includeHidden, bool recursive,
                                                   CancellationToken ct = default)
        {
            var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var files = new List<FileEntry>();
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                var provider = ProviderRegistry.For(root);
                IEnumerable<FileEntry> found = recursive
                    ? FileUtility.CollectFlat(provider, root, includeHidden, int.MaxValue).Files
                    : provider.EnumerateFiles(root).Where(f => includeHidden || !f.Flags.HasFlag(FileEntryFlags.Hidden));
                foreach (var f in found)
                    if (seen.Add(f.Path)) files.Add(f);
            }
            return files;
        }

        /// <summary>
        /// Groups <paramref name="files"/> by identical contents, largest waste first. Files that
        /// can't be read are skipped (and counted in <paramref name="unreadable"/>).
        /// </summary>
        public static List<DuplicateGroup> Find(IReadOnlyList<FileEntry> files, out int unreadable,
                                                IProgress<Progress>? progress = null, CancellationToken ct = default)
        {
            int skipped = 0;
            var sameSize = files.Where(f => f.Size > 0 && !f.IsLink)
                                .GroupBy(f => f.Size)
                                .Where(g => g.Count() > 1)
                                .ToList();

            // Narrow each size group by the hash of the first 64 KB, then (if larger) of everything.
            int total = sameSize.Sum(g => g.Count()), done = 0;
            var groups = new List<DuplicateGroup>();
            foreach (var bySize in sameSize)
            {
                var byStart = new Dictionary<string, List<FileEntry>>();
                foreach (var f in bySize)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new Progress("Comparing the start of each file", ++done, total));
                    string? h = TryHash(f.Path, PartialBytes, ct);
                    if (h == null) { skipped++; continue; }
                    if (!byStart.TryGetValue(h, out var list)) byStart[h] = list = new List<FileEntry>();
                    list.Add(f);
                }

                foreach (var candidates in byStart.Values.Where(l => l.Count > 1))
                {
                    if (bySize.Key <= PartialBytes)
                    {
                        groups.Add(new DuplicateGroup(bySize.Key, Ordered(candidates)));
                        continue;
                    }
                    var byWhole = new Dictionary<string, List<FileEntry>>();
                    foreach (var f in candidates)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new Progress($"Reading {f.Name} in full", done, total));
                        string? h = TryHash(f.Path, long.MaxValue, ct);
                        if (h == null) { skipped++; continue; }
                        if (!byWhole.TryGetValue(h, out var list)) byWhole[h] = list = new List<FileEntry>();
                        list.Add(f);
                    }
                    groups.AddRange(byWhole.Values.Where(l => l.Count > 1).Select(l => new DuplicateGroup(bySize.Key, Ordered(l))));
                }
            }

            unreadable = skipped;
            return groups.OrderByDescending(g => g.Wasted).ThenBy(g => g.Files[0].Path, StringComparer.Ordinal).ToList();
        }

        private static List<FileEntry> Ordered(List<FileEntry> files)
            => files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>SHA-256 of the first <paramref name="limit"/> bytes (or all of them), or null if the file can't be read.</summary>
        private static string? TryHash(string path, long limit, CancellationToken ct)
        {
            try
            {
                using var stream = ProviderRegistry.For(path).OpenRead(path);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long left = limit;
                while (left > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n == 0) break;
                    hash.AppendData(buffer, 0, n);
                    left -= n;
                }
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// Which copies to mark for deletion in a group: all but the one to keep. Ties keep the
        /// first in path order, so marking is predictable.
        /// </summary>
        public static IEnumerable<FileEntry> AllButKept(DuplicateGroup group, KeepRule rule)
        {
            var keep = rule switch
            {
                KeepRule.Oldest => group.Files.OrderBy(f => f.LastModified).First(),
                KeepRule.Newest => group.Files.OrderByDescending(f => f.LastModified).First(),
                _ => group.Files.OrderBy(f => f.Path.Length).First(),
            };
            return group.Files.Where(f => !ReferenceEquals(f, keep));
        }
    }
}
