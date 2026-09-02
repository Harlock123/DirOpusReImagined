using System;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Everything a provider needs to decide whether it can handle a file, and to read it.
///
/// <para><see cref="Head"/> is read once by <see cref="PreviewRegistry"/> and handed to every
/// provider's <c>CanPreview</c>, so provider selection costs a single small read no matter how many
/// providers are registered. A provider that accepts the file then opens its own stream through
/// <see cref="Provider"/>, because how much it needs to read is format-specific — an image decoder
/// streams, a ZIP reader seeks to one entry, a hex dump wants a fixed prefix.</para>
/// </summary>
public sealed class PreviewRequest
{
    /// <summary>Provider path: a plain filesystem path, an <c>archive://</c> URI, or a cloud path.</summary>
    public required string Path { get; init; }

    /// <summary>Friendly name shown in headers and titles.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The provider that owns <see cref="Path"/>.</summary>
    public required IFileProvider Provider { get; init; }

    /// <summary>File size in bytes, or -1 when it could not be determined.</summary>
    public long Size { get; init; } = -1;

    /// <summary>First bytes of the file, for magic-number detection. May be shorter than requested
    /// (or empty) for a short or unreadable file.</summary>
    public byte[] Head { get; init; } = Array.Empty<byte>();

    /// <summary>Detected type, sniffed from <see cref="Head"/> before any provider is consulted.</summary>
    public FileSignature.Kind Signature { get; init; } = FileSignature.Kind.Unknown;

    /// <summary>Upper bound on bytes a text/hex preview should read, so a huge file cannot hang the UI.</summary>
    public int MaxBytes { get; init; } = 256 * 1024;
}
