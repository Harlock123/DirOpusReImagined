using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>A photo's details from its EXIF block, and how the picture is turned (EXIF orientation 1–8).</summary>
public sealed record PhotoDetails(IReadOnlyList<InfoField> Fields, int Orientation);

/// <summary>
/// Reads the EXIF block of a JPEG, TIFF, PNG (eXIf chunk) or WebP (EXIF chunk): camera, lens,
/// date taken, exposure, flash, orientation and GPS position. Only the start of the file is
/// needed. Every offset is checked against the data, so a damaged or hostile block gives fewer
/// fields rather than an exception.
/// </summary>
public static class ExifReader
{
    /// <summary>How much of the start of a file to read for its EXIF block.</summary>
    public const int ProbeBytes = 256 * 1024;

    public static PhotoDetails? Read(byte[] data)
    {
        try
        {
            int start = FindTiff(data, out int length);
            if (start < 0) return null;
            var tiff = new Tiff(data.AsSpan(start, length).ToArray());
            if (!tiff.Valid) return null;
            return Describe(tiff);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Where the TIFF-structured EXIF data starts in the file, or -1.</summary>
    internal static int FindTiff(byte[] d, out int length)
    {
        length = 0;
        bool At(int i, string s) => i >= 0 && i + s.Length <= d.Length && Encoding.ASCII.GetString(d, i, s.Length) == s;

        // A TIFF file is its own EXIF block.
        if (d.Length >= 8 && (At(0, "II*\0") || At(0, "MM\0*"))) { length = d.Length; return 0; }

        // JPEG: an APP1 segment starting "Exif\0\0".
        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
        {
            int p = 2;
            while (p + 4 <= d.Length && d[p] == 0xFF)
            {
                byte marker = d[p + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { p += 2; continue; }
                if (marker == 0xD9 || marker == 0xDA) break;   // end, or the image data begins
                int len = (d[p + 2] << 8) | d[p + 3];
                if (len < 2) break;
                if (marker == 0xE1 && At(p + 4, "Exif\0\0"))
                {
                    length = Math.Min(len - 8, d.Length - (p + 10));
                    return length > 8 ? p + 10 : -1;
                }
                p += 2 + len;
            }
            return -1;
        }

        // PNG: an eXIf chunk.
        if (d.Length >= 8 && d[0] == 0x89 && At(1, "PNG"))
        {
            int p = 8;
            while (p + 8 <= d.Length)
            {
                long len = ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];
                if (len < 0 || p + 8 + len > d.Length) break;
                if (At(p + 4, "eXIf")) { length = (int)len; return p + 8; }
                if (At(p + 4, "IEND")) break;
                p += 12 + (int)len;
            }
            return -1;
        }

        // WebP: an EXIF chunk (some writers keep JPEG's "Exif\0\0" prefix).
        if (At(0, "RIFF") && At(8, "WEBP"))
        {
            int p = 12;
            while (p + 8 <= d.Length)
            {
                long len = BitConverter.ToUInt32(d, p + 4);
                if (p + 8 + len > d.Length) break;
                if (At(p, "EXIF"))
                {
                    int s = p + 8;
                    if (At(s, "Exif\0\0")) { s += 6; len -= 6; }
                    length = (int)len;
                    return s;
                }
                p += 8 + (int)len + (int)(len & 1);
            }
        }
        return -1;
    }

    private static PhotoDetails Describe(Tiff t)
    {
        var ifd0 = t.ReadIfd(t.FirstIfd);
        var exif = ifd0.TryGetValue(0x8769, out var e) ? t.ReadIfd((int)t.UInt(e)) : new Dictionary<ushort, Tiff.Entry>();
        var gps = ifd0.TryGetValue(0x8825, out var g) ? t.ReadIfd((int)t.UInt(g)) : new Dictionary<ushort, Tiff.Entry>();

        var fields = new List<InfoField>();
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) fields.Add(new InfoField(label, value.Trim())); }

        string? make = Str(t, ifd0, 0x010F), model = Str(t, ifd0, 0x0110);
        Add("Camera", model != null && make != null && !model.StartsWith(make.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
            ? $"{make} {model}" : model ?? make);
        Add("Lens", Str(t, exif, 0xA434));
        Add("Taken", Date(Str(t, exif, 0x9003) ?? Str(t, ifd0, 0x0132)));

        if (Rational(t, exif, 0x829A) is { } shutter && shutter > 0)
            Add("Shutter", shutter < 1 ? $"1/{Math.Round(1 / shutter):0} s" : $"{shutter:0.#} s");
        if (Rational(t, exif, 0x829D) is { } f && f > 0) Add("Aperture", $"f/{f:0.#}");
        if (exif.TryGetValue(0x8827, out var iso)) Add("ISO", t.UInt(iso).ToString(CultureInfo.InvariantCulture));
        if (Rational(t, exif, 0x920A) is { } fl && fl > 0)
        {
            string equiv = exif.TryGetValue(0xA405, out var e35) && t.UInt(e35) > 0 ? $" ({t.UInt(e35)} mm in 35 mm terms)" : "";
            Add("Focal length", $"{fl:0.#} mm{equiv}");
        }
        if (Rational(t, exif, 0x9204, signed: true) is { } bias && Math.Abs(bias) > 0.01)
            Add("Exposure bias", $"{bias:+0.#;-0.#} EV");
        if (exif.TryGetValue(0x9209, out var flash)) Add("Flash", (t.UInt(flash) & 1) == 1 ? "Fired" : "Did not fire");

        int orientation = ifd0.TryGetValue(0x0112, out var o) ? (int)t.UInt(o) : 1;
        if (orientation is < 1 or > 8) orientation = 1;
        if (orientation != 1) Add("Orientation", OrientationText(orientation));

        if (Coordinate(t, gps, 2, 1, "N", "S") is { } lat && Coordinate(t, gps, 4, 3, "E", "W") is { } lon)
            Add("Location", $"{lat}, {lon}");
        if (Rational(t, gps, 6) is { } alt)
        {
            bool below = gps.TryGetValue(5, out var aref) && t.UInt(aref) == 1;
            Add("Altitude", $"{alt:0} m {(below ? "below" : "above")} sea level");
        }
        Add("Software", Str(t, ifd0, 0x0131));
        Add("Artist", Str(t, ifd0, 0x013B));
        return new PhotoDetails(fields, orientation);
    }

    public static string OrientationText(int o) => o switch
    {
        2 => "Mirrored",
        3 => "Upside down",
        4 => "Upside down, mirrored",
        5 => "Turned 90° anticlockwise, mirrored",
        6 => "Turned 90° clockwise",
        7 => "Turned 90° clockwise, mirrored",
        8 => "Turned 90° anticlockwise",
        _ => "Normal",
    };

    private static string? Str(Tiff t, Dictionary<ushort, Tiff.Entry> ifd, ushort tag)
        => ifd.TryGetValue(tag, out var e) ? t.Ascii(e) : null;

    private static double? Rational(Tiff t, Dictionary<ushort, Tiff.Entry> ifd, ushort tag, bool signed = false, int index = 0)
        => ifd.TryGetValue(tag, out var e) ? t.Rational(e, index, signed) : null;

    private static string? Date(string? exif)
        => exif != null && DateTime.TryParseExact(exif.Trim('\0', ' '), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                                                  DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;

    /// <summary>"51.50123° N" from GPS degrees, minutes and seconds.</summary>
    private static string? Coordinate(Tiff t, Dictionary<ushort, Tiff.Entry> gps, ushort valueTag, ushort refTag, string pos, string neg)
    {
        if (!gps.TryGetValue(valueTag, out var v) || v.Count < 3) return null;
        double? d = t.Rational(v, 0, false), m = t.Rational(v, 1, false), s = t.Rational(v, 2, false);
        if (d == null || m == null || s == null) return null;
        double deg = d.Value + m.Value / 60 + s.Value / 3600;
        string r = gps.TryGetValue(refTag, out var re) ? (t.Ascii(re) ?? pos) : pos;
        return $"{deg.ToString("0.#####", CultureInfo.InvariantCulture)}° {(r.StartsWith(neg) ? neg : pos)}";
    }

    /// <summary>Just enough of TIFF to read EXIF: byte order, IFDs, and typed values, all bounds-checked.</summary>
    private sealed class Tiff
    {
        public readonly record struct Entry(ushort Type, int Count, int ValueAt);

        private readonly byte[] _d;
        private readonly bool _le;
        public bool Valid { get; }
        public int FirstIfd { get; }

        public Tiff(byte[] d)
        {
            _d = d;
            if (d.Length < 8) return;
            if (d[0] == 'I' && d[1] == 'I') _le = true;
            else if (d[0] == 'M' && d[1] == 'M') _le = false;
            else return;
            if (U16(2) != 42) return;
            FirstIfd = (int)U32(4);
            Valid = FirstIfd >= 8 && FirstIfd < d.Length;
        }

        private ushort U16(int at) => at < 0 || at + 2 > _d.Length ? (ushort)0
            : _le ? (ushort)(_d[at] | (_d[at + 1] << 8)) : (ushort)((_d[at] << 8) | _d[at + 1]);

        private uint U32(int at) => at < 0 || at + 4 > _d.Length ? 0u
            : _le ? (uint)(_d[at] | (_d[at + 1] << 8) | (_d[at + 2] << 16) | (_d[at + 3] << 24))
                  : (uint)((_d[at] << 24) | (_d[at + 1] << 16) | (_d[at + 2] << 8) | _d[at + 3]);

        private static int TypeSize(ushort type) => type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 => 4, 5 or 10 => 8, _ => 0 };

        public Dictionary<ushort, Entry> ReadIfd(int at)
        {
            var result = new Dictionary<ushort, Entry>();
            if (at < 8 || at + 2 > _d.Length) return result;
            int n = U16(at);
            for (int i = 0; i < n && i < 512; i++)
            {
                int e = at + 2 + i * 12;
                if (e + 12 > _d.Length) break;
                ushort tag = U16(e), type = U16(e + 2);
                uint count = U32(e + 4);
                int size = TypeSize(type);
                if (size == 0 || count == 0 || count > int.MaxValue / 8) continue;
                long bytes = (long)size * count;
                int valueAt = bytes <= 4 ? e + 8 : (int)U32(e + 8);
                if (valueAt < 0 || valueAt + bytes > _d.Length) continue;
                result[tag] = new Entry(type, (int)count, valueAt);
            }
            return result;
        }

        public uint UInt(Entry e) => e.Type switch
        {
            1 or 7 => _d[e.ValueAt],
            3 => U16(e.ValueAt),
            4 => U32(e.ValueAt),
            _ => 0,
        };

        public string? Ascii(Entry e)
        {
            if (e.Type != 2) return null;
            string s = Encoding.ASCII.GetString(_d, e.ValueAt, e.Count);
            int nul = s.IndexOf('\0');
            s = (nul >= 0 ? s[..nul] : s).Trim();
            return s.Length == 0 ? null : s;
        }

        public double? Rational(Entry e, int index, bool signed)
        {
            if ((e.Type != 5 && e.Type != 10) || index >= e.Count) return null;
            int at = e.ValueAt + index * 8;
            double num = e.Type == 10 || signed ? (int)U32(at) : U32(at);
            double den = e.Type == 10 || signed ? (int)U32(at + 4) : U32(at + 4);
            return den == 0 ? null : num / den;
        }
    }
}
