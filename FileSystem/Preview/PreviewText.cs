using System;
using System.IO;
using System.Text;
using System.Threading;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Byte-level helpers shared by the preview providers and the viewer that renders them.
///
/// <para>These were private to <c>FileViewer</c>. They moved here so the F3 viewer and the live
/// preview cannot drift apart — a binary-sniffing heuristic that differs between the two would show
/// the same file as text in one window and hex in the other.</para>
/// </summary>
public static class PreviewText
{
    /// <summary>Reads at most <paramref name="max"/> bytes, plus one so the caller can tell whether
    /// the file was actually longer. Works on non-seekable provider streams.</summary>
    public static byte[] ReadUpTo(Stream stream, int max, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int total = 0, read;
        while (total <= max && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            ms.Write(buffer, 0, read);
            total += read;
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Heuristic: binary if it contains NUL bytes, or a high fraction of the sampled bytes are
    /// non-printable control characters. High bytes are ignored so UTF-8 text is not misread.
    /// </summary>
    public static bool LooksBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return false;
        int sample = Math.Min(data.Length, 8192);
        int suspicious = 0;
        for (int i = 0; i < sample; i++)
        {
            byte b = data[i];
            if (b == 0) return true;
            bool printable = b >= 0x20 && b < 0x7F;
            bool ws = b is 0x09 or 0x0A or 0x0D or 0x0C or 0x08;
            if (!printable && !ws && b < 0x80) suspicious++;
        }
        return suspicious > sample / 10;
    }

    /// <summary>Decodes as text, honouring a BOM when present and otherwise assuming UTF-8.
    /// Invalid sequences become U+FFFD rather than throwing.</summary>
    public static string DecodeText(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return new UTF8Encoding(false, false).GetString(data, 3, data.Length - 3);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);

        return new UTF8Encoding(false, false).GetString(data);
    }

    /// <summary>Classic offset / bytes / ASCII hex dump, 16 bytes per line.</summary>
    public static string BuildHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 4);
        var ascii = new StringBuilder(16);
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            ascii.Clear();
            for (int j = 0; j < 16; j++)
            {
                if (i + j < data.Length)
                {
                    byte b = data[i + j];
                    sb.Append(b.ToString("X2")).Append(' ');
                    ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }
                else
                {
                    sb.Append("   ");
                }
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ').Append(ascii).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Human-readable byte count for status lines and info cards.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "unknown";
        string[] units = { "bytes", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes:N0} bytes" : $"{v:0.##} {units[u]}";
    }
}
