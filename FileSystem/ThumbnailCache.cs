using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Generates and caches small image thumbnails for the panel's Thumbnails view. Thumbnails are
/// decoded to a bounded width (so full-resolution photos never blow up memory) and cached on disk,
/// keyed by the source path plus its size and modified time — so a changed file re-generates while an
/// unchanged one is a cheap disk read on the next visit.
/// <para>
/// This is a pure-image utility: it never reasons about remote/latency concerns — callers gate
/// generation on <see cref="IFileProvider.IsRemote"/> (and archives) so cloud folders don't trigger
/// full downloads. All failures collapse to <c>null</c> (caller shows a generic icon instead).
/// </para>
/// </summary>
public static class ThumbnailCache
{
    // Extensions we attempt to decode as images. Lower-case, no leading dot.
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "ico", "tif", "tiff"
    };

    /// <summary>True if the name's extension is one we can render as an image thumbnail.</summary>
    public static bool IsImageFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext)) return false;
        return ImageExtensions.Contains(ext.TrimStart('.'));
    }

    /// <summary>
    /// Stable cache key for a (path, size, modified-time, target-width) tuple. Same inputs always
    /// yield the same key; changing any of them (a re-saved file, a different tile size) yields a new
    /// one, so a stale thumbnail is never served. Returns a hex string safe as a filename.
    /// </summary>
    public static string KeyFor(string path, long fileSize, long mtimeTicks, int targetWidth)
    {
        // Normalise separators so the same file keys identically regardless of how the path was built.
        string normalized = (path ?? "").Replace('\\', '/');
        string payload = $"{normalized}|{fileSize}|{mtimeTicks}|{targetWidth}";
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(payload));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Returns a cached thumbnail for the file, generating and caching it on first use. Runs entirely
    /// off the UI thread. Returns <c>null</c> for non-images, unreadable files, or any decode failure —
    /// callers treat null as "show a generic icon".
    /// </summary>
    public static Task<Bitmap?> GetOrCreateAsync(
        IFileProvider provider, string path, string name, int targetWidth,
        long fileSize, long mtimeTicks, CancellationToken ct = default)
        => Task.Run<Bitmap?>(() =>
        {
            try
            {
                if (!IsImageFile(name)) return null;
                ct.ThrowIfCancellationRequested();

                string key = KeyFor(path, fileSize, mtimeTicks, targetWidth);
                string cacheFile = Path.Combine(CacheDir(), key + ".png");

                // Fast path: previously generated. A corrupt cache entry falls through to regeneration.
                if (File.Exists(cacheFile))
                {
                    try
                    {
                        using var cached = File.OpenRead(cacheFile);
                        return new Bitmap(cached);
                    }
                    catch
                    {
                        try { File.Delete(cacheFile); } catch { /* best effort */ }
                    }
                }

                ct.ThrowIfCancellationRequested();

                // Decode the source down to the target width; this bounds memory for huge images.
                Bitmap thumb;
                using (var src = provider.OpenRead(path))
                {
                    thumb = Bitmap.DecodeToWidth(src, targetWidth);
                }

                // Persist to the disk cache (temp + move so a concurrent read never sees a half-written
                // file). A write race between two workers is harmless — last writer wins.
                try
                {
                    string tmp = cacheFile + "." + Environment.CurrentManagedThreadId + ".tmp";
                    using (var outStream = File.Create(tmp))
                        thumb.Save(outStream);
                    File.Move(tmp, cacheFile, overwrite: true);
                }
                catch { /* caching is best-effort; still return the decoded bitmap */ }

                return thumb;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }, ct);

    private static string? _cacheDir;

    /// <summary>The on-disk thumbnail cache directory (created on first use), under the same
    /// platform app-data location as Configuration.xml.</summary>
    private static string CacheDir()
    {
        if (_cacheDir != null) return _cacheDir;

        string baseDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "dori");
        else if (Environment.OSVersion.Platform == PlatformID.Unix)
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "dori");
        else
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dori");

        string dir = Path.Combine(baseDir, "thumbnails");
        try { Directory.CreateDirectory(dir); } catch { /* fall through; writes will just fail */ }
        _cacheDir = dir;
        return dir;
    }
}
