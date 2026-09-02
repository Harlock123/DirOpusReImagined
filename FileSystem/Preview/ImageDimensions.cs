using System;
using System.Buffers.Binary;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Reads an image's pixel dimensions straight from its header, without decoding it.
///
/// <para>This exists to bound memory. Decoding is the only other way to learn how big an image is,
/// but a 100-megapixel photo costs ~400 MB of RGBA once decoded — so "decode it and see" is exactly
/// the thing that must not happen. Knowing the dimensions first lets the image provider decode
/// straight to a bounded width, and lets it report the true source size rather than the scaled one.</para>
///
/// <para>Parses the formats whose headers are trivial and stable. Anything else returns false and
/// the caller falls back to a conservative bounded decode.</para>
/// </summary>
public static class ImageDimensions
{
    /// <summary>How much of the file to buffer when looking for dimensions. PNG/GIF/BMP put them in
    /// the first 32 bytes; JPEG hides them behind a chain of segments, but well inside this.</summary>
    public const int ProbeBytes = 64 * 1024;

    public static bool TryParse(ReadOnlySpan<byte> data, FileSignature.Kind kind, out int width, out int height)
    {
        width = height = 0;
        try
        {
            return kind switch
            {
                FileSignature.Kind.Png  => TryPng(data, out width, out height),
                FileSignature.Kind.Jpeg => TryJpeg(data, out width, out height),
                FileSignature.Kind.Gif  => TryGif(data, out width, out height),
                FileSignature.Kind.Bmp  => TryBmp(data, out width, out height),
                FileSignature.Kind.WebP => TryWebP(data, out width, out height),
                _ => false,
            };
        }
        catch
        {
            // A truncated or malformed header is not an error worth surfacing - the caller just
            // falls back to a bounded decode.
            return false;
        }
    }

    // IHDR is always the first chunk: 8-byte signature, 4-byte length, "IHDR", then width/height.
    private static bool TryPng(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 24 || !d.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        w = BinaryPrimitives.ReadInt32BigEndian(d.Slice(16, 4));
        h = BinaryPrimitives.ReadInt32BigEndian(d.Slice(20, 4));
        return w > 0 && h > 0;
    }

    // Walk the segment chain to the first Start-Of-Frame marker, which carries the real size.
    private static bool TryJpeg(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        int i = 2;                                   // skip SOI (FF D8)
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }     // resync over padding bytes

            byte marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }

            // Standalone markers carry no length field.
            if (marker is 0x01 or >= 0xD0 and <= 0xD9) { i += 2; continue; }

            int len = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 2, 2));
            if (len < 2) return false;

            // SOF0..SOF15, excluding the DHT/JPG/DAC markers that share the range.
            bool isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof)
            {
                if (i + 9 >= d.Length) return false;
                h = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5, 2));
                w = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 7, 2));
                return w > 0 && h > 0;
            }

            i += 2 + len;
        }
        return false;
    }

    private static bool TryGif(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 10) return false;
        w = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(6, 2));
        h = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(8, 2));
        return w > 0 && h > 0;
    }

    // BITMAPINFOHEADER. Height is signed: negative means a top-down bitmap, same magnitude.
    private static bool TryBmp(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 26) return false;
        w = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(18, 4));
        h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(d.Slice(22, 4)));
        return w > 0 && h > 0;
    }

    // Three sub-formats share the RIFF/WEBP wrapper and each stores its size differently.
    private static bool TryWebP(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 30) return false;
        var fourCc = d.Slice(12, 4);

        if (fourCc.SequenceEqual("VP8X"u8))
        {
            // 24-bit little-endian, stored as (dimension - 1).
            w = (d[24] | (d[25] << 8) | (d[26] << 16)) + 1;
            h = (d[27] | (d[28] << 8) | (d[29] << 16)) + 1;
            return w > 0 && h > 0;
        }

        if (fourCc.SequenceEqual("VP8L"u8))
        {
            // 14 bits each, packed after the 0x2F signature byte.
            int bits = d[21] | (d[22] << 8) | (d[23] << 16) | (d[24] << 24);
            w = (bits & 0x3FFF) + 1;
            h = ((bits >> 14) & 0x3FFF) + 1;
            return w > 0 && h > 0;
        }

        if (fourCc.SequenceEqual("VP8 "u8))
        {
            // Lossy keyframe: 3-byte start code, then 14-bit width and height.
            if (d.Length < 30 || d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A) return false;
            w = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(26, 2)) & 0x3FFF;
            h = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(28, 2)) & 0x3FFF;
            return w > 0 && h > 0;
        }

        return false;
    }
}
