using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Renders image files. Reads through <see cref="IFileProvider"/>, so it previews images inside
/// archives and on cloud remotes exactly like local ones.
///
/// <para>Decoding is bounded rather than trusting the file: dimensions come from the header first
/// (<see cref="ImageDimensions"/>), and anything wider than <see cref="MaxDecodeWidth"/> is decoded
/// straight down to that width. A 100-megapixel photo therefore costs a few megabytes, not the
/// ~400 MB its full RGBA buffer would take.</para>
/// </summary>
public sealed class ImagePreviewProvider : IPreviewProvider
{
    /// <summary>Widest bitmap handed to the UI. Comfortably above any preview pane, far below the
    /// point where a single decode is a memory problem.</summary>
    public const int MaxDecodeWidth = 2048;

    /// <summary>Files larger than this are refused outright. A file this big is either not really an
    /// image or not one worth blocking a preview on.</summary>
    public const long MaxSourceBytes = 256L * 1024 * 1024;

    /// <summary>Below this, a full decode is safe even when the header gave us no dimensions.</summary>
    private const long SafeFullDecodeBytes = 8L * 1024 * 1024;

    /// <summary>
    /// Cap on buffering a non-seekable source into memory. Archive entries and cloud reads are
    /// forward-only, and the image decoder needs to seek, so those have to be materialised first —
    /// but not at any size.
    /// </summary>
    private const long MaxBufferedBytes = 64L * 1024 * 1024;

    public int Priority => 100;

    public bool CanPreview(PreviewRequest r)
    {
        if (r.Size > MaxSourceBytes) return false;

        // Trust the magic bytes when we have them; fall back to the extension only when the header
        // was unreadable or too short to identify (an empty or truncated file).
        if (FileSignature.IsImage(r.Signature)) return true;
        return r.Signature == FileSignature.Kind.Unknown
               && r.Head.Length < FileSignature.HeadBytes
               && ThumbnailCache.IsImageFile(r.DisplayName);
    }

    public Task<PreviewResult> LoadAsync(PreviewRequest r, CancellationToken ct)
        => Task.Run<PreviewResult>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                // The decoder seeks, so the stream has to support it. A local file already does;
                // an archive entry or a cloud read is forward-only and must be buffered first.
                using var content = OpenSeekable(r, ct, out bool tooLarge);
                if (tooLarge)
                    return new PreviewResult.Error(
                        $"Image is too large to preview from this source (over {MaxBufferedBytes / (1024 * 1024)} MB).");

                // Read the true dimensions from the header before committing to a decode.
                byte[] probe = PreviewText.ReadUpTo(content, ImageDimensions.ProbeBytes, ct);
                bool known = ImageDimensions.TryParse(probe, r.Signature, out int srcW, out int srcH);
                content.Position = 0;

                ct.ThrowIfCancellationRequested();

                long size = r.Size >= 0 ? r.Size : content.Length;

                Bitmap bitmap;
                bool scaled;
                if (known && srcW > MaxDecodeWidth)
                {
                    bitmap = Bitmap.DecodeToWidth(content, MaxDecodeWidth);
                    scaled = true;
                }
                else if (known || size <= SafeFullDecodeBytes)
                {
                    // Known and already within bounds, or small enough to be safe regardless.
                    // DecodeToWidth would upscale here, wasting memory for a blurrier result.
                    bitmap = new Bitmap(content);
                    scaled = false;
                }
                else
                {
                    // Unknown dimensions and a large file: bound it.
                    bitmap = Bitmap.DecodeToWidth(content, MaxDecodeWidth);
                    scaled = true;
                }

                if (!known)
                {
                    srcW = bitmap.PixelSize.Width;
                    srcH = bitmap.PixelSize.Height;
                }

                string format = FileSignature.IsImage(r.Signature)
                    ? FileSignature.Describe(r.Signature)
                    : "Image";

                return new PreviewResult.Image(bitmap, srcW, srcH, format, size, scaled);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A file that claims to be an image but will not decode is an ordinary outcome
                // (truncated download, wrong extension), not something to crash or blank the pane on.
                return new PreviewResult.Error($"Could not decode image: {ex.Message}");
            }
        }, ct);

    /// <summary>
    /// Returns a seekable stream over the file, buffering into memory only when the provider's
    /// stream cannot seek. Local files stream straight off disk with no copy.
    /// </summary>
    private static Stream OpenSeekable(PreviewRequest r, CancellationToken ct, out bool tooLarge)
    {
        tooLarge = false;

        var stream = r.Provider.OpenRead(r.Path);
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        using (stream)
        {
            var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                total += read;
                if (total > MaxBufferedBytes)
                {
                    tooLarge = true;
                    buffer.Dispose();
                    return new MemoryStream();
                }
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return buffer;
        }
    }
}
