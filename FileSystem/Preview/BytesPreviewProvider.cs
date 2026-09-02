using System;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// The catch-all: reads a capped prefix and hands back the raw bytes for text or hex rendering.
///
/// <para>Registered at the lowest possible priority so every file gets some preview, however
/// unhelpful. Any format-specific provider added later automatically takes precedence over it
/// without this class having to know anything about them.</para>
/// </summary>
public sealed class BytesPreviewProvider : IPreviewProvider
{
    public int Priority => int.MinValue;

    public bool CanPreview(PreviewRequest r) => true;

    public Task<PreviewResult> LoadAsync(PreviewRequest r, CancellationToken ct)
        => Task.Run<PreviewResult>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                byte[] all;
                using (var stream = r.Provider.OpenRead(r.Path))
                    all = PreviewText.ReadUpTo(stream, r.MaxBytes, ct);

                bool truncated = all.Length > r.MaxBytes;
                if (truncated) Array.Resize(ref all, r.MaxBytes);

                long total = r.Size >= 0 ? r.Size : all.Length;
                return new PreviewResult.Bytes(all, PreviewText.LooksBinary(all), truncated, total);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new PreviewResult.Error($"Could not read file: {ex.Message}");
            }
        }, ct);
}
