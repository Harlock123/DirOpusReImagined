using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace DirOpusReImagined
{
    /// <summary>The format pictures are saved in. <see cref="Same"/> keeps each picture's own format when it can be written.</summary>
    public enum ImageFormatChoice { Same, Jpeg, Png, Webp }

    /// <summary>What to do to each picture.</summary>
    /// <param name="MaxWidth">Shrink to at most this wide (0 = any width). Pictures are never enlarged.</param>
    /// <param name="MaxHeight">Shrink to at most this high (0 = any height).</param>
    /// <param name="Turn">Extra quarter turns clockwise after standing it upright: 0, 90, 180 or 270.</param>
    /// <param name="Upright">Turn photos taken sideways upright (from their EXIF orientation).</param>
    /// <param name="Quality">JPEG and WebP quality, 1–100.</param>
    /// <param name="NameSuffix">Added to each name before the extension ("_small"); may be empty when saving elsewhere.</param>
    public sealed record ImageJob(int MaxWidth, int MaxHeight, int Turn, bool Upright, ImageFormatChoice Format, int Quality, string NameSuffix);

    /// <summary>
    /// Resizing, converting and turning pictures in a batch (right-click → Image Tools…). Pictures
    /// are decoded and written with SkiaSharp, which Avalonia already ships: it reads JPEG, PNG, WebP,
    /// GIF, BMP and ICO, and writes JPEG, PNG and WebP. A new file is always written; the original is
    /// never changed. Photo details (EXIF) aren't copied into the new file, so a photo is stood
    /// upright in its pixels rather than by its orientation tag.
    /// </summary>
    public static class ImageTools
    {
        public static readonly string[] ReadableExtensions = { ".jpg", ".jpeg", ".jpe", ".png", ".webp", ".gif", ".bmp", ".ico" };

        public static bool CanRead(string path) => ReadableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// <summary>The size a w×h picture ends up, fitting inside the limits and keeping its shape. Never larger.</summary>
        public static (int Width, int Height) FitWithin(int w, int h, int maxW, int maxH)
        {
            double scale = 1;
            if (maxW > 0 && w > maxW) scale = Math.Min(scale, (double)maxW / w);
            if (maxH > 0 && h > maxH) scale = Math.Min(scale, (double)maxH / h);
            if (scale >= 1) return (w, h);
            return (Math.Max(1, (int)Math.Round(w * scale)), Math.Max(1, (int)Math.Round(h * scale)));
        }

        /// <summary>The format a picture is saved in: its own when that can be written, otherwise PNG (GIF, BMP, ICO).</summary>
        public static SKEncodedImageFormat OutputFormat(string sourcePath, ImageFormatChoice choice) => choice switch
        {
            ImageFormatChoice.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageFormatChoice.Png => SKEncodedImageFormat.Png,
            ImageFormatChoice.Webp => SKEncodedImageFormat.Webp,
            _ => Path.GetExtension(sourcePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".jpe" => SKEncodedImageFormat.Jpeg,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png,
            },
        };

        /// <summary>The new file's name: "photo.jpg" with suffix "_small" as PNG → "photo_small.png". Keeps the original extension's spelling when the format is unchanged.</summary>
        public static string OutputName(string sourcePath, ImageJob job)
        {
            string stem = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            var format = OutputFormat(sourcePath, job.Format);
            string sameExt = ext.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".jpe" when format == SKEncodedImageFormat.Jpeg => ext,
                ".png" when format == SKEncodedImageFormat.Png => ext,
                ".webp" when format == SKEncodedImageFormat.Webp => ext,
                _ => "",
            };
            string newExt = sameExt.Length > 0 ? sameExt : format switch
            {
                SKEncodedImageFormat.Jpeg => ".jpg",
                SKEncodedImageFormat.Webp => ".webp",
                _ => ".png",
            };
            return stem + job.NameSuffix + newExt;
        }

        /// <summary>
        /// Makes the new picture for <paramref name="source"/> in <paramref name="targetFolder"/> and
        /// returns its path. Throws when it can't be read, or the new name is taken.
        /// </summary>
        public static string Process(string source, string targetFolder, ImageJob job)
        {
            string target = Path.Combine(targetFolder, OutputName(source, job));
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The new picture would replace the original. Add something to the name, or save it in the other panel.");
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException($"{Path.GetFileName(target)} already exists.");

            using var data = SKData.Create(source) ?? throw new IOException("It couldn't be read.");
            using var codec = SKCodec.Create(data) ?? throw new IOException("It isn't a picture DORI can read.");
            using var decoded = SKBitmap.Decode(codec) ?? throw new IOException("It couldn't be decoded.");

            SKBitmap current = decoded;
            var owned = new List<SKBitmap>();
            try
            {
                if (job.Upright && codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
                    current = Keep(owned, Orient(current, codec.EncodedOrigin));
                int turns = ((job.Turn / 90) % 4 + 4) % 4;
                if (turns != 0)
                    current = Keep(owned, Orient(current, turns switch { 1 => SKEncodedOrigin.RightTop, 2 => SKEncodedOrigin.BottomRight, _ => SKEncodedOrigin.LeftBottom }));

                var (w, h) = FitWithin(current.Width, current.Height, job.MaxWidth, job.MaxHeight);
                if (w != current.Width || h != current.Height)
                {
                    var info = new SKImageInfo(w, h, current.ColorType, current.AlphaType, current.ColorSpace);
                    current = Keep(owned, current.Resize(info, SKFilterQuality.High) ?? throw new IOException("It couldn't be resized."));
                }

                var format = OutputFormat(source, job.Format);
                using var image = SKImage.FromBitmap(format == SKEncodedImageFormat.Jpeg ? Keep(owned, Flatten(current)) : current);
                using var encoded = image.Encode(format, Math.Clamp(job.Quality, 1, 100))
                                    ?? throw new IOException($"It couldn't be saved as {format}.");
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    encoded.SaveTo(output);
                return target;
            }
            finally
            {
                foreach (var b in owned) b.Dispose();
            }
        }

        private static SKBitmap Keep(List<SKBitmap> owned, SKBitmap b) { owned.Add(b); return b; }

        /// <summary>A copy drawn on white, for JPEG, which has no transparency (transparent areas would otherwise turn black).</summary>
        private static SKBitmap Flatten(SKBitmap source)
        {
            var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
            return result;
        }

        /// <summary>The picture turned and mirrored the way <paramref name="origin"/> says, so it stands upright.</summary>
        internal static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
        {
            bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            int w = swap ? src.Height : src.Width, h = swap ? src.Width : src.Height;
            var result = new SKBitmap(new SKImageInfo(w, h, src.ColorType, src.AlphaType, src.ColorSpace));
            // Where each source pixel goes, as ImageOrientation.Target has it (w and h are the source's).
            int sw = src.Width, sh = src.Height;
            var m = origin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, sw, 0, 1, 0, 0, 0, 1),      // mirrored
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, sw, 0, -1, sh, 0, 0, 1), // upside down
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, sh, 0, 0, 1),    // upside down, mirrored
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),         // mirrored along the diagonal
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, sh, 1, 0, 0, 0, 0, 1),      // a quarter turn clockwise
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, sh, -1, 0, sw, 0, 0, 1), // the other diagonal
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, sw, 0, 0, 1),    // a quarter turn anticlockwise
                _ => SKMatrix.Identity,
            };
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(m);
            canvas.DrawBitmap(src, 0, 0);
            return result;
        }
    }
}
