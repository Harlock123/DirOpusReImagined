using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// What a preview provider produced. Deliberately a small closed set of shapes rather than a
/// rendered control: providers stay free of UI concerns and run off the UI thread, and the viewer
/// decides how each shape is drawn. New formats add a case here only when they genuinely need a
/// new shape — a PDF or an Office document reports through <see cref="Info"/>, not a new kind.
/// </summary>
public abstract record PreviewResult
{
    /// <summary>
    /// Raw file bytes for a text-or-hex preview. Both renderings come from the same buffer so the
    /// viewer's Text/Hex toggle never has to re-read the file; <paramref name="IsBinary"/> only
    /// chooses which one is shown first.
    /// </summary>
    public sealed record Bytes(byte[] Data, bool IsBinary, bool Truncated, long TotalBytes) : PreviewResult;

    /// <summary>A decoded image, already bounded in size by the provider.</summary>
    /// <param name="Scaled">True when <paramref name="Bitmap"/> was decoded smaller than the
    /// source, so the viewer can say so rather than implying it is showing full resolution.</param>
    public sealed record Image(
        Bitmap Bitmap, int SourceWidth, int SourceHeight,
        string Format, long TotalBytes, bool Scaled) : PreviewResult;

    /// <summary>A labelled field list — the honest fallback for formats that cannot be rendered
    /// but can still be described (page counts, authors, entry totals, codec details).</summary>
    public sealed record Info(string Title, IReadOnlyList<InfoField> Fields) : PreviewResult;

    /// <summary>Nothing to show, for a reason worth stating: no selection, a folder, a remote file
    /// deliberately not fetched.</summary>
    public sealed record Message(string Header, string Detail = "") : PreviewResult;

    /// <summary>The file could not be previewed. Carries the reason, never a silent blank.</summary>
    public sealed record Error(string Reason) : PreviewResult;
}

/// <summary>One labelled row in a <see cref="PreviewResult.Info"/> card.</summary>
public readonly record struct InfoField(string Label, string Value);
