using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Securely Delete (right-click → File Tools): each file is overwritten with random bytes,
    /// written through to the disk, renamed to a random name, emptied and then deleted, so its old
    /// contents and name aren't left where a recovery tool would find them. Folders are emptied the
    /// same way and removed. Links are removed, never followed: what they point to is left alone.
    /// <para>
    /// This can't be relied on everywhere: SSDs and flash drives move writes to fresh cells,
    /// copy-on-write file systems (Btrfs, ZFS, APFS) write new blocks, and snapshots, backups and
    /// cloud sync keep their own copies. Whole-disk encryption is the dependable answer there.
    /// </para>
    /// </summary>
    public static class SecureEraser
    {
        private const int BufferSize = 1024 * 1024;

        /// <summary>Total bytes the paths hold (files inside folders included, links counted as nothing).</summary>
        public static long BytesIn(IEnumerable<string> paths)
        {
            long total = 0;
            foreach (var p in paths) Walk(p, f => total += SafeLength(f), _ => { }, _ => { });
            return total;
        }

        /// <summary>Erases every path. <paramref name="progress"/> gets (bytes done, total). Throws on the first failure.</summary>
        public static void Erase(IReadOnlyList<string> paths, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
        {
            long total = BytesIn(paths), done = 0;
            progress?.Report((0, total));
            foreach (var path in paths)
            {
                Walk(path,
                    file =>
                    {
                        ct.ThrowIfCancellationRequested();
                        EraseFile(file, n => { done += n; progress?.Report((done, total)); }, ct);
                    },
                    link => File.Delete(link),
                    dir => RemoveFolder(dir));
            }
            progress?.Report((total, total));
        }

        /// <summary>
        /// Visits a path depth first: files to <paramref name="file"/>, links (to files or folders) to
        /// <paramref name="link"/>, and each folder to <paramref name="folderAfter"/> once what is inside it is done.
        /// </summary>
        private static void Walk(string path, Action<string> file, Action<string> link, Action<string> folderAfter)
        {
            if (LocalFileProvider.IsLink(path)) { link(path); return; }
            if (File.Exists(path)) { file(path); return; }
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.EnumerateFiles(path)) Walk(f, file, link, folderAfter);
            foreach (var d in Directory.EnumerateDirectories(path)) Walk(d, file, link, folderAfter);
            folderAfter(path);
        }

        internal static void EraseFile(string path, Action<long>? wrote, CancellationToken ct)
        {
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);

            long length = new FileInfo(path).Length;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough))
            {
                var buffer = new byte[(int)Math.Min(BufferSize, Math.Max(1, length))];
                long left = length;
                while (left > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(buffer.Length, left);
                    RandomNumberGenerator.Fill(buffer.AsSpan(0, n));
                    fs.Write(buffer, 0, n);
                    left -= n;
                    wrote?.Invoke(n);
                }
                fs.Flush(flushToDisk: true);
                fs.SetLength(0);
                fs.Flush(flushToDisk: true);
            }
            string renamed = RandomName(path);
            File.Move(path, renamed);
            File.Delete(renamed);
        }

        private static void RemoveFolder(string dir)
        {
            string renamed = RandomName(dir);
            Directory.Move(dir, renamed);
            Directory.Delete(renamed, recursive: false);
        }

        /// <summary>A free random name of the same length in the same folder, so the old name isn't left in the folder's entries.</summary>
        private static string RandomName(string path)
        {
            string parent = Path.GetDirectoryName(path.TrimEnd('/', '\\')) ?? "";
            int len = Math.Clamp(Path.GetFileName(path.TrimEnd('/', '\\')).Length, 4, 64);
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var name = new char[len];
                for (int i = 0; i < len; i++) name[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
                string candidate = Path.Combine(parent, new string(name));
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine(parent, Guid.NewGuid().ToString("N"));
        }

        private static long SafeLength(string f)
        {
            try { return new FileInfo(f).Length; } catch { return 0; }
        }
    }
}
