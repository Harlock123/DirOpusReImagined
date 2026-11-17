using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DirOpusReImagined
{
    /// <summary>What to take out of a photo.</summary>
    public enum StripMode
    {
        /// <summary>Only where it was taken (the GPS position); camera, date and the rest stay. JPEG only; other types lose all their details.</summary>
        Location,
        /// <summary>Every detail: EXIF, XMP, IPTC, comments and text. The way the photo is turned is kept, and so is its colour profile.</summary>
        All,
    }

    /// <summary>
    /// Takes the details out of photos before they are shared — where they were taken, the
    /// camera and its serial number, the date, the software — without touching the picture
    /// itself: the image data is copied byte for byte, so there is no loss of quality.
    /// JPEG, PNG and WebP are understood.
    /// </summary>
    public static class PhotoPrivacy
    {
        public static bool CanStrip(string name)
            => Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".jpe" or ".png" or ".webp";

        /// <summary>The photo without the details <paramref name="mode"/> removes. Throws <see cref="InvalidDataException"/> for a file it can't read.</summary>
        public static byte[] Strip(byte[] data, StripMode mode)
        {
            if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8) return StripJpeg(data, mode);
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return StripPng(data);
            if (data.Length >= 12 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 4) == "WEBP") return StripWebP(data);
            throw new InvalidDataException("It isn't a JPEG, PNG or WebP picture.");
        }

        // ---- JPEG ----

        private static byte[] StripJpeg(byte[] d, StripMode mode)
        {
            var output = new MemoryStream(d.Length);
            output.Write(d, 0, 2);
            int p = 2;
            bool wroteOrientation = false;
            while (p + 4 <= d.Length)
            {
                if (d[p] != 0xFF) throw new InvalidDataException("The JPEG is damaged.");
                byte marker = d[p + 1];
                if (marker == 0xFF) { p++; continue; }   // fill byte
                if (marker == 0xDA || marker == 0xD9)
                {
                    output.Write(d, p, d.Length - p);   // the image data, as it is
                    return output.ToArray();
                }
                int len = (d[p + 2] << 8) | d[p + 3];
                if (len < 2 || p + 2 + len > d.Length) throw new InvalidDataException("The JPEG is damaged.");
                var segment = d.AsSpan(p, 2 + len);
                bool isExif = marker == 0xE1 && len >= 8 && segment.Slice(4, 6).SequenceEqual("Exif\0\0"u8);
                bool isXmp = marker == 0xE1 && !isExif;
                bool isIcc = marker == 0xE2 && len >= 16 && segment.Slice(4, 12).SequenceEqual("ICC_PROFILE\0"u8);

                if (mode == StripMode.All)
                {
                    bool drop = isExif || isXmp || marker == 0xED || marker == 0xFE || marker == 0xEC
                                || (marker == 0xE2 && !isIcc) || marker is >= 0xE3 and <= 0xEF;
                    if (isExif && !wroteOrientation)
                    {
                        int o = Orientation(segment[10..].ToArray());
                        if (o is > 1 and <= 8) { output.Write(OrientationOnlyExif(o)); wroteOrientation = true; }
                    }
                    if (!drop) output.Write(segment);
                }
                else
                {
                    if (isExif)
                    {
                        var copy = segment.ToArray();
                        RemoveGps(copy.AsSpan(10));
                        output.Write(copy);
                    }
                    else if (isXmp && Encoding.UTF8.GetString(segment).Contains("GPS", StringComparison.Ordinal)) { /* XMP can repeat the position: drop it */ }
                    else output.Write(segment);
                }
                p += 2 + len;
            }
            throw new InvalidDataException("The JPEG ends early.");
        }

        private readonly record struct Tiff(byte[] D, bool Little)
        {
            public int U16(int at) => Little ? D[at] | (D[at + 1] << 8) : (D[at] << 8) | D[at + 1];
            public int U32(int at) => Little ? D[at] | (D[at + 1] << 8) | (D[at + 2] << 16) | (D[at + 3] << 24)
                                             : (D[at] << 24) | (D[at + 1] << 16) | (D[at + 2] << 8) | D[at + 3];
            public void W16(int at, int v)
            {
                if (Little) { D[at] = (byte)v; D[at + 1] = (byte)(v >> 8); }
                else { D[at] = (byte)(v >> 8); D[at + 1] = (byte)v; }
            }
        }

        private static Tiff? OpenTiff(byte[] t)
        {
            if (t.Length < 8) return null;
            bool little = t[0] == 'I' && t[1] == 'I';
            if (!little && !(t[0] == 'M' && t[1] == 'M')) return null;
            return new Tiff(t, little);
        }

        /// <summary>The orientation (1–8) in a TIFF block, or 0.</summary>
        internal static int Orientation(byte[] tiffBlock)
        {
            if (OpenTiff(tiffBlock) is not { } t) return 0;
            try
            {
                int ifd = t.U32(4);
                int count = t.U16(ifd);
                for (int i = 0; i < count; i++)
                {
                    int e = ifd + 2 + i * 12;
                    if (t.U16(e) == 0x0112) return t.U16(e + 8);
                }
            }
            catch (IndexOutOfRangeException) { }
            return 0;
        }

        /// <summary>
        /// Blanks the GPS block of a TIFF/EXIF block in place: its values are overwritten with zeros
        /// and it is left with no entries, so nothing of the position stays in the file.
        /// </summary>
        internal static bool RemoveGps(Span<byte> block)
        {
            var bytes = block.ToArray();
            if (OpenTiff(bytes) is not { } t) return false;
            bool changed = false;
            try
            {
                int ifd0 = t.U32(4);
                int count = t.U16(ifd0);
                for (int i = 0; i < count; i++)
                {
                    int e = ifd0 + 2 + i * 12;
                    if (t.U16(e) != 0x8825) continue;
                    int gps = t.U32(e + 8);
                    if (gps <= 0 || gps + 2 > bytes.Length) break;
                    int n = t.U16(gps);
                    for (int k = 0; k < n; k++)
                    {
                        int g = gps + 2 + k * 12;
                        if (g + 12 > bytes.Length) break;
                        int type = t.U16(g + 2);
                        long items = (uint)t.U32(g + 4);
                        long size = items * TypeSize(type);
                        if (size > 4)
                        {
                            int at = t.U32(g + 8);
                            if (at > 0 && at + size <= bytes.Length) Array.Clear(bytes, at, (int)size);
                        }
                        Array.Clear(bytes, g, 12);
                    }
                    t.W16(gps, 0);
                    changed = true;
                }
            }
            catch (IndexOutOfRangeException) { return false; }
            if (changed) bytes.CopyTo(block);
            return changed;
        }

        private static int TypeSize(int type) => type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 11 => 4,
            5 or 10 or 12 => 8,
            _ => 1,
        };

        /// <summary>A small EXIF segment holding only the orientation, so a sideways photo still shows upright.</summary>
        internal static byte[] OrientationOnlyExif(int orientation)
        {
            var tiff = new byte[]
            {
                (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,   // little-endian TIFF, IFD0 at 8
                1, 0,                                        // one entry
                0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0,   // Orientation, SHORT, 1
                0, 0, 0, 0,                                  // no next IFD
            };
            var seg = new List<byte> { 0xFF, 0xE1 };
            int len = 2 + 6 + tiff.Length;
            seg.Add((byte)(len >> 8)); seg.Add((byte)len);
            seg.AddRange("Exif\0\0"u8.ToArray());
            seg.AddRange(tiff);
            return seg.ToArray();
        }

        // ---- PNG ----

        private static readonly HashSet<string> PngDetailChunks = new() { "eXIf", "tEXt", "zTXt", "iTXt", "tIME" };

        private static byte[] StripPng(byte[] d)
        {
            var output = new MemoryStream(d.Length);
            output.Write(d, 0, 8);
            int p = 8;
            while (p + 12 <= d.Length)
            {
                int len = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
                if (len < 0 || p + 12 + len > d.Length) throw new InvalidDataException("The PNG is damaged.");
                string type = Encoding.ASCII.GetString(d, p + 4, 4);
                if (!PngDetailChunks.Contains(type)) output.Write(d, p, 12 + len);
                p += 12 + len;
                if (type == "IEND") break;
            }
            return output.ToArray();
        }

        // ---- WebP ----

        private static byte[] StripWebP(byte[] d)
        {
            var chunks = new List<(string Type, byte[] Body)>();
            int p = 12;
            while (p + 8 <= d.Length)
            {
                string type = Encoding.ASCII.GetString(d, p, 4);
                int len = d[p + 4] | (d[p + 5] << 8) | (d[p + 6] << 16) | (d[p + 7] << 24);
                if (len < 0 || p + 8 + len > d.Length) throw new InvalidDataException("The WebP is damaged.");
                chunks.Add((type, d.AsSpan(p + 8, len).ToArray()));
                p += 8 + len + (len & 1);
            }
            var output = new MemoryStream(d.Length);
            output.Write("RIFF\0\0\0\0WEBP"u8);
            foreach (var (type, body) in chunks)
            {
                if (type is "EXIF" or "XMP ") continue;
                if (type == "VP8X" && body.Length >= 1) body[0] &= unchecked((byte)~(0x08 | 0x04));   // no EXIF, no XMP
                output.Write(Encoding.ASCII.GetBytes(type));
                output.Write(BitConverter.GetBytes(body.Length));
                output.Write(body);
                if ((body.Length & 1) == 1) output.WriteByte(0);
            }
            var bytes = output.ToArray();
            BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
            return bytes;
        }

        /// <summary>"name_clean.jpg" beside it (or in <paramref name="folder"/>), numbered when taken.</summary>
        public static string CleanName(string source, string folder, Func<string, bool> exists)
        {
            string stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
            string candidate = Path.Combine(folder, $"{stem}_clean{ext}");
            for (int n = 2; exists(candidate); n++) candidate = Path.Combine(folder, $"{stem}_clean ({n}){ext}");
            return candidate;
        }
    }
}
