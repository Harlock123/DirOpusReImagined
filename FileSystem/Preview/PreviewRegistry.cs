using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Chooses a preview provider for a file and runs it. The counterpart to
/// <see cref="ProviderRegistry"/>: that one decides who can <em>read</em> a path, this one decides
/// who can <em>display</em> it.
///
/// <para>Adding a format means writing an <see cref="IPreviewProvider"/> and registering it — no
/// edits here and none in the viewer, provided it produces one of the existing
/// <see cref="PreviewResult"/> shapes.</para>
/// </summary>
public static class PreviewRegistry
{
    private static readonly List<IPreviewProvider> _providers = new()
    {
        new ImagePreviewProvider(),
        new PdfPreviewProvider(),
        new OfficePreviewProvider(),
        new ArchivePreviewProvider(),
        new BytesPreviewProvider(),   // Priority int.MinValue - always last, always succeeds.
    };

    /// <summary>Adds a provider. Ordering is by <see cref="IPreviewProvider.Priority"/> descending,
    /// so registration order does not matter.</summary>
    public static void Register(IPreviewProvider provider)
    {
        _providers.Add(provider);
        _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>The registered providers, highest priority first.</summary>
    public static IReadOnlyList<IPreviewProvider> Providers =>
        _providers.OrderByDescending(p => p.Priority).ToList();

    /// <summary>
    /// Builds a preview for <paramref name="path"/>.
    ///
    /// <para>Reads the file's leading bytes exactly once and sniffs them, then offers that to each
    /// provider in priority order — so selection costs one small read regardless of how many
    /// providers are registered. Never throws for an unreadable file; returns
    /// <see cref="PreviewResult.Error"/> instead, because a preview failing is not a reason to
    /// disturb whatever the user was doing.</para>
    /// </summary>
    public static async Task<PreviewResult> LoadAsync(
        string path, string displayName, int maxBytes = 256 * 1024, CancellationToken ct = default)
    {
        try
        {
            var provider = ProviderRegistry.For(path);

            long size = -1;
            try { size = provider.Stat(path)?.Size ?? -1; }
            catch { /* size is a hint; a provider that cannot stat still previews fine */ }

            byte[] head = Array.Empty<byte>();
            try
            {
                using var stream = provider.OpenRead(path);
                head = PreviewText.ReadUpTo(stream, FileSignature.HeadBytes, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // If the file cannot be opened at all, no provider will do better.
                return new PreviewResult.Error($"Could not read file: {ex.Message}");
            }

            var request = new PreviewRequest
            {
                Path = path,
                DisplayName = displayName,
                Provider = provider,
                Size = size,
                Head = head,
                Signature = FileSignature.Detect(head),
                MaxBytes = maxBytes,
            };

            foreach (var candidate in _providers.OrderByDescending(p => p.Priority))
            {
                ct.ThrowIfCancellationRequested();
                if (!candidate.CanPreview(request)) continue;
                return await candidate.LoadAsync(request, ct).ConfigureAwait(false);
            }

            return new PreviewResult.Message(displayName, "No preview available for this file type.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PreviewResult.Error(ex.Message);
        }
    }
}
