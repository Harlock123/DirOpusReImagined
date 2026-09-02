using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Produces a preview for one family of file types. Mirrors <see cref="IFileProvider"/>: providers
/// are registered, asked in priority order whether they can handle a request, and the first that
/// says yes owns it.
/// </summary>
public interface IPreviewProvider
{
    /// <summary>Higher wins. The catch-all byte previewer sits at <see cref="int.MinValue"/> so it
    /// is always last and every file gets some preview.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this provider handles the request. Must be cheap and must not read the file —
    /// decide from <see cref="PreviewRequest.Signature"/>, <see cref="PreviewRequest.Head"/>,
    /// the name and the size only.
    /// </summary>
    bool CanPreview(PreviewRequest request);

    /// <summary>
    /// Reads and builds the preview. Called on a background thread; implementations must honour
    /// <paramref name="ct"/> and should never throw for an ordinary unreadable file — return
    /// <see cref="PreviewResult.Error"/> instead.
    /// </summary>
    Task<PreviewResult> LoadAsync(PreviewRequest request, CancellationToken ct);
}
