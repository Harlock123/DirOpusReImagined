using System;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Identifies a file from its leading bytes.
///
/// <para>Extensions are a hint, not evidence — they are renamed, missing on Unix, and wrong often
/// enough that a preview keyed on them alone shows the wrong thing with confidence. Magic numbers
/// are cheap (the first few dozen bytes, which the registry already reads) and decisive.</para>
///
/// <para><see cref="Kind.Zip"/> is deliberately coarse: DOCX, XLSX, PPTX, ODT, EPUB and JAR are all
/// ZIP containers and cannot be told apart from the header alone. Distinguishing them means looking
/// at the entries inside, which is the job of a provider that has decided to open the archive.</para>
/// </summary>
public static class FileSignature
{
    public enum Kind
    {
        Unknown,
        Jpeg, Png, Gif, Bmp, WebP, Tiff, Ico,     // images
        Pdf,
        Zip, SevenZip, Rar, Gzip, Bzip2, Xz, Tar, // containers
        Ole2,                                     // legacy .doc/.xls/.ppt
        Elf, PeExe,                               // executables
        Rtf, Utf8Bom, Utf16Bom,
    }

    /// <summary>Bytes worth reading for detection. TAR's magic sits at offset 257, which sets the floor.</summary>
    public const int HeadBytes = 512;

    public static Kind Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3  && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return Kind.Jpeg;
        if (Match(head, 0, 0x89, (byte)'P', (byte)'N', (byte)'G'))                      return Kind.Png;
        if (StartsWith(head, "GIF87a") || StartsWith(head, "GIF89a"))                    return Kind.Gif;
        if (StartsWith(head, "BM"))                                                      return Kind.Bmp;
        if (StartsWith(head, "RIFF") && head.Length >= 12 &&
            head.Slice(8, 4).SequenceEqual("WEBP"u8))                                    return Kind.WebP;
        if (Match(head, 0, 0x49, 0x49, 0x2A, 0x00) || Match(head, 0, 0x4D, 0x4D, 0x00, 0x2A)) return Kind.Tiff;
        if (Match(head, 0, 0x00, 0x00, 0x01, 0x00))                                      return Kind.Ico;

        if (StartsWith(head, "%PDF-"))                                                   return Kind.Pdf;
        if (StartsWith(head, "{\\rtf"))                                                  return Kind.Rtf;

        // ZIP: normal (PK\3\4), empty (PK\5\6) and spanned (PK\7\8) central-directory variants.
        if (head.Length >= 4 && head[0] == 'P' && head[1] == 'K' &&
            ((head[2] == 3 && head[3] == 4) || (head[2] == 5 && head[3] == 6) || (head[2] == 7 && head[3] == 8)))
            return Kind.Zip;

        if (Match(head, 0, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C))                          return Kind.SevenZip;
        if (StartsWith(head, "Rar!"))                                                    return Kind.Rar;
        if (Match(head, 0, 0x1F, 0x8B))                                                  return Kind.Gzip;
        if (StartsWith(head, "BZh"))                                                     return Kind.Bzip2;
        if (Match(head, 0, 0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00))      return Kind.Xz;
        if (head.Length >= 262 && head.Slice(257, 5).SequenceEqual("ustar"u8))           return Kind.Tar;

        if (Match(head, 0, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1))              return Kind.Ole2;
        if (Match(head, 0, 0x7F, (byte)'E', (byte)'L', (byte)'F'))                       return Kind.Elf;
        if (StartsWith(head, "MZ"))                                                      return Kind.PeExe;

        if (Match(head, 0, 0xEF, 0xBB, 0xBF))                                            return Kind.Utf8Bom;
        if (Match(head, 0, 0xFF, 0xFE) || Match(head, 0, 0xFE, 0xFF))                    return Kind.Utf16Bom;

        return Kind.Unknown;
    }

    /// <summary>True for kinds Avalonia's decoder can be expected to render.</summary>
    public static bool IsImage(Kind kind) => kind is Kind.Jpeg or Kind.Png or Kind.Gif
                                                  or Kind.Bmp or Kind.WebP or Kind.Ico or Kind.Tiff;

    /// <summary>Short human label, used in info cards and status lines.</summary>
    public static string Describe(Kind kind) => kind switch
    {
        Kind.Jpeg => "JPEG image",      Kind.Png  => "PNG image",       Kind.Gif => "GIF image",
        Kind.Bmp  => "Bitmap image",    Kind.WebP => "WebP image",      Kind.Tiff => "TIFF image",
        Kind.Ico  => "Icon",            Kind.Pdf  => "PDF document",    Kind.Rtf => "Rich text",
        Kind.Zip  => "ZIP container",   Kind.SevenZip => "7z archive",  Kind.Rar => "RAR archive",
        Kind.Gzip => "gzip stream",     Kind.Bzip2 => "bzip2 stream",   Kind.Xz => "xz stream",
        Kind.Tar  => "TAR archive",     Kind.Ole2 => "Legacy Office document (OLE2)",
        Kind.Elf  => "ELF executable",  Kind.PeExe => "Windows executable",
        Kind.Utf8Bom or Kind.Utf16Bom => "Text",
        _ => "Unknown",
    };

    private static bool StartsWith(ReadOnlySpan<byte> head, string ascii)
    {
        if (head.Length < ascii.Length) return false;
        for (int i = 0; i < ascii.Length; i++)
            if (head[i] != (byte)ascii[i]) return false;
        return true;
    }

    private static bool Match(ReadOnlySpan<byte> head, int offset, params byte[] pattern)
    {
        if (head.Length < offset + pattern.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (head[offset + i] != pattern[i]) return false;
        return true;
    }
}
