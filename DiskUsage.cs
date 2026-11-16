using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DirOpusReImagined
{
    /// <summary>One file or folder in a disk usage scan. A folder's size is everything below it.</summary>
    public sealed class UsageNode
    {
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public bool IsDirectory { get; init; }
        public bool IsLink { get; init; }
        public long Size { get; internal set; }
        public long FileCount { get; internal set; }
        public UsageNode? Parent { get; internal set; }

        /// <summary>Set when part of the folder couldn't be read (no permission), so its size is a lower bound.</summary>
        public bool Incomplete { get; internal set; }

        internal readonly List<UsageNode> ChildList = new();
        public IReadOnlyList<UsageNode> Children => ChildList;

        /// <summary>The children, largest first; ties by name.</summary>
        public List<UsageNode> BySize()
            => ChildList.OrderByDescending(c => c.Size).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Measures what takes the space under a local folder. Linked folders and linked files count
    /// as nothing and are never walked into (they may point back up the tree, or at another disk).
    /// </summary>
    public static class DiskUsage
    {
        /// <summary>
        /// Scans <paramref name="root"/>. <paramref name="progress"/> gets the number of files seen
        /// so far now and then. Cancelling throws <see cref="OperationCanceledException"/>.
        /// </summary>
        public static UsageNode Scan(string root, IProgress<long>? progress = null, CancellationToken ct = default)
        {
            var top = new UsageNode
            {
                Name = System.IO.Path.GetFileName(root.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : root,
                Path = root,
                IsDirectory = true,
            };
            long seen = 0;
            var pending = new Stack<UsageNode>();
            pending.Push(top);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = pending.Pop();
                List<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(dir.Path).EnumerateFileSystemInfos().ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    dir.Incomplete = true;
                    continue;
                }
                foreach (var e in entries)
                {
                    bool isLink;
                    try { isLink = e.LinkTarget != null; } catch { isLink = false; }
                    bool isDir = e is DirectoryInfo;
                    var node = new UsageNode { Name = e.Name, Path = e.FullName, IsDirectory = isDir, IsLink = isLink, Parent = dir };
                    dir.ChildList.Add(node);
                    if (isDir)
                    {
                        if (!isLink) pending.Push(node);
                        continue;
                    }
                    if (!isLink)
                    {
                        try { node.Size = ((FileInfo)e).Length; } catch { }
                        node.FileCount = 1;
                    }
                    if (++seen % 2000 == 0) progress?.Report(seen);
                }
            }
            progress?.Report(seen);
            Total(top);
            return top;
        }

        /// <summary>Adds each folder's contents into its size and file count, deepest first, without recursion.</summary>
        private static void Total(UsageNode top)
        {
            var order = new List<UsageNode>();
            var stack = new Stack<UsageNode>();
            stack.Push(top);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                order.Add(n);
                foreach (var c in n.ChildList) if (c.IsDirectory) stack.Push(c);
            }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var d = order[i];
                d.Size = 0;
                d.FileCount = 0;
                foreach (var c in d.ChildList)
                {
                    d.Size += c.Size;
                    d.FileCount += c.FileCount;
                    if (c.Incomplete) d.Incomplete = true;
                }
            }
        }

        /// <summary>The share of <paramref name="part"/> in <paramref name="whole"/>, 0 to 1.</summary>
        public static double Share(long part, long whole) => whole <= 0 ? 0 : Math.Clamp((double)part / whole, 0, 1);
    }
}
