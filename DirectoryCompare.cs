using System;
using System.Collections.Generic;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined
{
    /// <summary>How a row in one panel compares to the same-named entry in the other panel.</summary>
    public enum RowCompareState
    {
        None,      // not compared / not applicable
        Same,      // present on both sides and equivalent (recursively, for folders)
        Unique,    // present only on this side
        Newer,     // file is newer than the other side's copy
        Older,     // file is older than the other side's copy
        Different, // file: same time, different size — or folder whose subtree differs
    }

    /// <summary>
    /// Compares two directories by entry name and plans one-way syncs. Reads through
    /// <see cref="IFileProvider"/>, so it works for local and cloud paths alike and benefits from
    /// the rclone listing cache. Comparison can be shallow or recursive; sync planning is always
    /// recursive, descending into folders present on both sides so nested differences are resolved
    /// file-by-file rather than by overwriting whole folders.
    /// </summary>
    public static class DirectoryComparer
    {
        // Cloud and local filesystems report modification times at different resolutions, so treat
        // timestamps within this window as equal rather than flagging spurious newer/older.
        private static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

        public sealed record CompareResult(
            Dictionary<string, RowCompareState> Left,
            Dictionary<string, RowCompareState> Right);

        /// <param name="recursive">
        /// When true, a folder present on both sides is marked Same only if its entire subtree
        /// matches, otherwise Different. When false, such folders are always Same (shallow).
        /// </param>
        public static CompareResult Compare(string leftPath, string rightPath, bool recursive = false)
        {
            var left = Snapshot(leftPath);
            var right = Snapshot(rightPath);

            var leftStates = new Dictionary<string, RowCompareState>(StringComparer.OrdinalIgnoreCase);
            var rightStates = new Dictionary<string, RowCompareState>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, l) in left)
            {
                if (!right.TryGetValue(name, out var r))
                {
                    leftStates[name] = RowCompareState.Unique;
                    continue;
                }

                if (l.IsDir && r.IsDir)
                {
                    var s = recursive && !SubtreesEqual(Child(leftPath, name), Child(rightPath, name))
                        ? RowCompareState.Different
                        : RowCompareState.Same;
                    leftStates[name] = s;
                    rightStates[name] = s;
                }
                else if (l.IsDir != r.IsDir)
                {
                    // Same name is a folder on one side and a file on the other.
                    leftStates[name] = RowCompareState.Different;
                    rightStates[name] = RowCompareState.Different;
                }
                else
                {
                    var (ls, rs) = ClassifyFiles(l, r);
                    leftStates[name] = ls;
                    rightStates[name] = rs;
                }
            }

            foreach (var (name, _) in right)
            {
                if (!left.ContainsKey(name))
                    rightStates[name] = RowCompareState.Unique;
            }

            return new CompareResult(leftStates, rightStates);
        }

        /// <summary>A recursive sync plan: files/folders to copy, and destination-only items to delete.</summary>
        public sealed record SyncPlan(List<TransferItem> Copies, List<(string Path, bool IsDir)> Deletes);

        /// <summary>
        /// Plans a one-way sync from <paramref name="source"/> to <paramref name="dest"/>: copies
        /// new, newer, and changed items (recursing into shared folders); records destination-only
        /// items as deletion candidates (the caller decides whether to apply them).
        /// </summary>
        public static SyncPlan PlanSync(string source, string dest)
        {
            var copies = new List<TransferItem>();
            var deletes = new List<(string, bool)>();
            PlanInto(source, dest, copies, deletes);
            return new SyncPlan(copies, deletes);
        }

        private static void PlanInto(string src, string dst,
            List<TransferItem> copies, List<(string, bool)> deletes)
        {
            var left = Snapshot(src);
            var right = Snapshot(dst);

            foreach (var (name, l) in left)
            {
                string sChild = Child(src, name);
                string dChild = Child(dst, name);

                if (!right.TryGetValue(name, out var r))
                {
                    // Unique on source — copy the file, or the whole folder (nothing to overwrite).
                    copies.Add(new TransferItem(sChild, dst, dChild, l.IsDir));
                }
                else if (l.IsDir && r.IsDir)
                {
                    PlanInto(sChild, dChild, copies, deletes); // descend into shared folders
                }
                else if (l.IsDir != r.IsDir)
                {
                    // Type mismatch (dir vs file) — skip rather than guess.
                }
                else if (IsNewerOrDifferent(l, r))
                {
                    copies.Add(new TransferItem(sChild, dst, dChild, false));
                }
            }

            foreach (var (name, r) in right)
            {
                if (!left.ContainsKey(name))
                    deletes.Add((Child(dst, name), r.IsDir));
            }
        }

        private readonly record struct Item(bool IsDir, long Size, DateTime Modified);

        private static Dictionary<string, Item> Snapshot(string path)
        {
            var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path)) return map;

            var provider = ProviderRegistry.For(path);
            foreach (var d in provider.EnumerateDirectories(path))
                map[d.Name] = new Item(true, 0, d.LastModified);
            foreach (var f in provider.EnumerateFiles(path))
                map[f.Name] = new Item(false, f.Size, f.LastModified);
            return map;
        }

        private static bool SubtreesEqual(string leftPath, string rightPath)
        {
            var left = Snapshot(leftPath);
            var right = Snapshot(rightPath);
            if (left.Count != right.Count) return false;

            foreach (var (name, l) in left)
            {
                if (!right.TryGetValue(name, out var r)) return false;
                if (l.IsDir != r.IsDir) return false;

                if (l.IsDir)
                {
                    if (!SubtreesEqual(Child(leftPath, name), Child(rightPath, name))) return false;
                }
                else if (!FilesEqual(l, r))
                {
                    return false;
                }
            }
            return true;
        }

        private static (RowCompareState left, RowCompareState right) ClassifyFiles(Item l, Item r)
        {
            var delta = l.Modified - r.Modified;
            if (delta > TimeTolerance)  return (RowCompareState.Newer, RowCompareState.Older);
            if (delta < -TimeTolerance) return (RowCompareState.Older, RowCompareState.Newer);

            return l.Size == r.Size
                ? (RowCompareState.Same, RowCompareState.Same)
                : (RowCompareState.Different, RowCompareState.Different);
        }

        private static bool FilesEqual(Item l, Item r)
        {
            var delta = l.Modified - r.Modified;
            if (delta > TimeTolerance || delta < -TimeTolerance) return false;
            return l.Size == r.Size;
        }

        // Source should be copied over dest when it is newer, or the same age but a different size.
        private static bool IsNewerOrDifferent(Item src, Item dst)
        {
            var delta = src.Modified - dst.Modified;
            if (delta > TimeTolerance)  return true;   // source newer
            if (delta < -TimeTolerance) return false;  // source older — never overwrite a newer dest
            return src.Size != dst.Size;               // same age, different content
        }

        private static string Child(string parent, string name)
        {
            if (CloudPath.IsCloudUri(parent))
                return CloudPath.Parse(parent).Join(name).FullUri;

            var sep = OperatingSystem.IsWindows() ? '\\' : '/';
            return parent.TrimEnd('/', '\\') + sep + name;
        }
    }
}
