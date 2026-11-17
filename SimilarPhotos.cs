using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using SkiaSharp;

namespace DirOpusReImagined
{
    /// <summary>One photo looked at by the similar-photo finder: its fingerprint, size in pixels and on disk.</summary>
    public sealed record PhotoPrint(string Path, ulong Hash, int Width, int Height, long Bytes, DateTime Modified)
    {
        public long Pixels => (long)Width * Height;
    }

    /// <summary>
    /// Finds photos that look alike — a resized copy, a re-saved JPEG, burst shots — which Find
    /// Duplicates (identical bytes only) misses. Each picture is shrunk to 9×8 shades of grey and
    /// fingerprinted by whether each pixel is brighter than the one to its right (a "difference
    /// hash"); pictures whose fingerprints differ in few of their 64 bits look alike.
    /// </summary>
    public static class SimilarPhotos
    {
        public const int Strict = 4, Normal = 10, Loose = 16;

        /// <summary>The fingerprint of an image, or null when it can't be read. Decoded at a reduced size, so large photos are quick.</summary>
        public static PhotoPrint? Fingerprint(string path)
        {
            try
            {
                using var data = SKData.Create(path);
                if (data == null) return null;
                using var codec = SKCodec.Create(data);
                if (codec == null) return null;
                int w = codec.Info.Width, h = codec.Info.Height;
                // Ask the decoder for a small version when it can make one cheaply (JPEG can, by 1/2, 1/4, 1/8).
                var scaled = codec.GetScaledDimensions(Math.Min(1f, 256f / Math.Max(1, Math.Max(w, h))));
                var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var bitmap = new SKBitmap(info);
                var result = codec.GetPixels(info, bitmap.GetPixels());
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    using var full = SKBitmap.Decode(codec);
                    if (full == null) return null;
                    return Make(path, full, w, h);
                }
                return Make(path, bitmap, w, h);
            }
            catch { return null; }
        }

        private static PhotoPrint Make(string path, SKBitmap bitmap, int w, int h)
        {
            var fi = new FileInfo(path);
            return new PhotoPrint(path, DHash(bitmap), w, h, fi.Length, fi.LastWriteTime);
        }

        /// <summary>The 64-bit difference hash of a picture.</summary>
        public static ulong DHash(SKBitmap bitmap)
        {
            var info = new SKImageInfo(9, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var small = bitmap.Resize(info, SKFilterQuality.Medium) ?? throw new IOException("It couldn't be shrunk.");
            ulong hash = 0;
            int bit = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++, bit++)
                    if (Grey(small.GetPixel(x, y)) > Grey(small.GetPixel(x + 1, y))) hash |= 1UL << bit;
            return hash;
        }

        private static double Grey(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

        public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

        /// <summary>
        /// Groups photos that look alike (each within <paramref name="threshold"/> bits of another in
        /// its group), largest group first; photos like no other are left out. Within a group the
        /// best is first: the most pixels, then the largest file, then the oldest.
        /// </summary>
        public static List<List<PhotoPrint>> Group(IReadOnlyList<PhotoPrint> prints, int threshold, CancellationToken ct = default)
        {
            int n = prints.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < n; i++)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                for (int j = i + 1; j < n; j++)
                    if (Distance(prints[i].Hash, prints[j].Hash) <= threshold)
                        parent[Root(j)] = Root(i);
            }
            return Enumerable.Range(0, n).GroupBy(Root)
                .Where(g => g.Count() > 1)
                .Select(g => g.Select(i => prints[i]).OrderByDescending(p => p.Pixels).ThenByDescending(p => p.Bytes).ThenBy(p => p.Modified).ToList())
                .OrderByDescending(g => g.Count).ThenBy(g => g[0].Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The pictures under <paramref name="folder"/> (and below it), never walking into linked folders.</summary>
        public static List<string> CollectPictures(string folder, bool subfolders, CancellationToken ct = default)
        {
            var found = new List<string>();
            var stack = new Stack<string>();
            stack.Push(folder);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
                catch { continue; }
                foreach (var e in entries)
                {
                    if (e is DirectoryInfo d) { if (subfolders && d.LinkTarget == null && !d.Name.StartsWith('.')) stack.Push(d.FullName); }
                    else if (ImageTools.CanRead(e.Name) && !e.Name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) found.Add(e.FullName);
                }
            }
            return found;
        }
    }
}
