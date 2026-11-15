using System.Text;
using DirOpusReImagined.FileSystem.Preview;

namespace DirOpusReImagined.Tests;

public class ExifReaderTests
{
    /// <summary>Builds a TIFF/EXIF block: IFD0 with pointers to an Exif IFD and a GPS IFD.</summary>
    private sealed class ExifBuilder
    {
        public readonly List<(ushort Tag, ushort Type, object Value)> Ifd0 = new(), Exif = new(), Gps = new();
        private readonly bool _le;
        public ExifBuilder(bool littleEndian) => _le = littleEndian;

        private byte[] U16(int v) => _le ? new[] { (byte)v, (byte)(v >> 8) } : new[] { (byte)(v >> 8), (byte)v };
        private byte[] U32(long v) => _le
            ? new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) }
            : new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        private byte[] Encode(ushort type, object value, out int count)
        {
            switch (type)
            {
                case 2: { var b = Encoding.ASCII.GetBytes((string)value + "\0"); count = b.Length; return b; }
                case 3: count = 1; return U16((int)value);
                case 4: count = 1; return U32((int)value);
                case 1: count = 1; return new[] { (byte)(int)value };
                case 5:
                case 10:
                {
                    var pairs = (long[])value;
                    count = pairs.Length / 2;
                    return pairs.SelectMany(p => U32(p)).ToArray();
                }
                default: throw new ArgumentException();
            }
        }

        /// <summary>Lays out IFD0, then the Exif and GPS IFDs, each followed by its long values.</summary>
        public byte[] Build()
        {
            var bytes = new List<byte>();
            bytes.AddRange(_le ? "II"u8.ToArray() : "MM"u8.ToArray());
            bytes.AddRange(U16(42));
            bytes.AddRange(U32(8));

            var ifds = new List<List<(ushort, ushort, object)>> { Ifd0.Select(x => (x.Tag, x.Type, x.Value)).ToList() };
            bool hasExif = Exif.Count > 0, hasGps = Gps.Count > 0;
            if (hasExif) { ifds[0].Add((0x8769, 4, 0)); ifds.Add(Exif.Select(x => (x.Tag, x.Type, x.Value)).ToList()); }
            if (hasGps) { ifds[0].Add((0x8825, 4, 0)); ifds.Add(Gps.Select(x => (x.Tag, x.Type, x.Value)).ToList()); }

            // Work out where each IFD (and its overflow data) will start.
            var starts = new List<int>();
            int pos = 8;
            foreach (var ifd in ifds)
            {
                starts.Add(pos);
                int overflow = ifd.Sum(e => { var v = Encode(e.Item2, e.Item3, out _); return v.Length > 4 ? v.Length : 0; });
                pos += 2 + ifd.Count * 12 + 4 + overflow;
            }
            int sub = 1;
            if (hasExif) ifds[0][ifds[0].FindIndex(e => e.Item1 == 0x8769)] = (0x8769, 4, starts[sub++]);
            if (hasGps) ifds[0][ifds[0].FindIndex(e => e.Item1 == 0x8825)] = (0x8825, 4, starts[sub]);

            for (int i = 0; i < ifds.Count; i++)
            {
                var ifd = ifds[i].OrderBy(e => e.Item1).ToList();
                int dataAt = starts[i] + 2 + ifd.Count * 12 + 4;
                var data = new List<byte>();
                bytes.AddRange(U16(ifd.Count));
                foreach (var (tag, type, value) in ifd)
                {
                    var v = Encode(type, value, out int count);
                    bytes.AddRange(U16(tag));
                    bytes.AddRange(U16(type));
                    bytes.AddRange(U32(count));
                    if (v.Length <= 4) bytes.AddRange(v.Concat(new byte[4 - v.Length]));
                    else { bytes.AddRange(U32(dataAt + data.Count)); data.AddRange(v); }
                }
                bytes.AddRange(U32(0));
                bytes.AddRange(data);
            }
            return bytes.ToArray();
        }
    }

    private static ExifBuilder Camera(bool le)
    {
        var b = new ExifBuilder(le);
        b.Ifd0.Add((0x010F, 2, "Canon"));
        b.Ifd0.Add((0x0110, 2, "Canon EOS R6"));
        b.Ifd0.Add((0x0112, 3, 6));
        b.Exif.Add((0x9003, 2, "2024:06:01 14:22:05"));
        b.Exif.Add((0x829A, 5, new long[] { 1, 250 }));
        b.Exif.Add((0x829D, 5, new long[] { 28, 10 }));
        b.Exif.Add((0x8827, 3, 400));
        b.Exif.Add((0x920A, 5, new long[] { 50, 1 }));
        b.Exif.Add((0xA405, 3, 50));
        b.Exif.Add((0x9209, 3, 16));
        b.Exif.Add((0xA434, 2, "RF24-105mm F4 L IS USM"));
        b.Gps.Add((1, 2, "N"));
        b.Gps.Add((2, 5, new long[] { 51, 1, 30, 1, 0, 1 }));
        b.Gps.Add((3, 2, "W"));
        b.Gps.Add((4, 5, new long[] { 0, 1, 7, 1, 30, 1 }));
        return b;
    }

    private static byte[] Jpeg(byte[] tiff)
    {
        var app1 = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        int len = app1.Length + 2;
        return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0 }                     // a small APP0 first
            .Concat(new byte[] { 0xFF, 0xE1, (byte)(len >> 8), (byte)len }).Concat(app1)
            .Concat(new byte[] { 0xFF, 0xDA, 0, 2, 1, 2, 3 }).ToArray();
    }

    private static Dictionary<string, string> Fields(PhotoDetails d) => d.Fields.ToDictionary(f => f.Label, f => f.Value);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_camera_jpeg_reads_in_either_byte_order(bool littleEndian)
    {
        var d = ExifReader.Read(Jpeg(Camera(littleEndian).Build()));
        Assert.NotNull(d);
        var f = Fields(d!);
        Assert.Equal("Canon EOS R6", f["Camera"]);          // the make isn't repeated
        Assert.Equal("RF24-105mm F4 L IS USM", f["Lens"]);
        Assert.Equal("2024-06-01 14:22:05", f["Taken"]);
        Assert.Equal("1/250 s", f["Shutter"]);
        Assert.Equal("f/2.8", f["Aperture"]);
        Assert.Equal("400", f["ISO"]);
        Assert.Equal("50 mm (50 mm in 35 mm terms)", f["Focal length"]);
        Assert.Equal("Did not fire", f["Flash"]);
        Assert.Equal("Turned 90° clockwise", f["Orientation"]);
        Assert.Equal("51.5° N, 0.125° W", f["Location"]);
        Assert.Equal(6, d!.Orientation);
    }

    [Fact]
    public void A_make_not_in_the_model_is_shown_with_it()
    {
        var b = new ExifBuilder(true);
        b.Ifd0.Add((0x010F, 2, "NIKON CORPORATION"));
        b.Ifd0.Add((0x0110, 2, "Z 6"));
        Assert.Equal("NIKON CORPORATION Z 6", Fields(ExifReader.Read(b.Build())!)["Camera"]);
    }

    [Fact]
    public void A_bare_tiff_is_its_own_exif()
        => Assert.Equal("Canon EOS R6", Fields(ExifReader.Read(Camera(false).Build())!)["Camera"]);

    [Fact]
    public void Png_exif_chunk()
    {
        var tiff = Camera(true).Build();
        var png = new List<byte> { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 };
        void Chunk(string type, byte[] data)
        {
            png.AddRange(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
            png.AddRange(Encoding.ASCII.GetBytes(type));
            png.AddRange(data);
            png.AddRange(new byte[4]);   // CRC isn't checked
        }
        Chunk("IHDR", new byte[13]);
        Chunk("eXIf", tiff);
        Chunk("IEND", Array.Empty<byte>());
        Assert.Equal("1/250 s", Fields(ExifReader.Read(png.ToArray())!)["Shutter"]);
    }

    [Fact]
    public void Webp_exif_chunk_with_or_without_the_jpeg_prefix()
    {
        foreach (bool prefix in new[] { false, true })
        {
            var data = (prefix ? "Exif\0\0"u8.ToArray() : Array.Empty<byte>()).Concat(Camera(true).Build()).ToArray();
            var chunk = "EXIF"u8.ToArray().Concat(BitConverter.GetBytes(data.Length)).Concat(data)
                .Concat(data.Length % 2 == 1 ? new byte[1] : Array.Empty<byte>()).ToArray();
            var webp = "RIFF"u8.ToArray().Concat(BitConverter.GetBytes(4 + chunk.Length)).Concat("WEBP"u8.ToArray()).Concat(chunk).ToArray();
            Assert.Equal("f/2.8", Fields(ExifReader.Read(webp)!)["Aperture"]);
        }
    }

    [Fact]
    public void A_long_exposure_and_a_negative_bias()
    {
        var b = new ExifBuilder(true);
        b.Exif.Add((0x829A, 5, new long[] { 25, 10 }));
        b.Exif.Add((0x9204, 10, new long[] { unchecked((uint)-2) & 0xFFFFFFFF, 3 }));
        var f = Fields(ExifReader.Read(b.Build())!);
        Assert.Equal("2.5 s", f["Shutter"]);
        Assert.Equal("-0.7 EV", f["Exposure bias"]);
    }

    [Fact]
    public void Below_sea_level()
    {
        var b = new ExifBuilder(true);
        b.Gps.Add((5, 1, 1));
        b.Gps.Add((6, 5, new long[] { 4200, 100 }));
        Assert.Equal("42 m below sea level", Fields(ExifReader.Read(b.Build())!)["Altitude"]);
    }

    [Fact]
    public void A_photo_without_exif_has_no_details()
    {
        Assert.Null(ExifReader.Read(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0, 2 }));
        Assert.Null(ExifReader.Read("just text"u8.ToArray()));
        Assert.Null(ExifReader.Read(Array.Empty<byte>()));
    }

    [Fact]
    public void Damaged_blocks_never_throw()
    {
        var good = Jpeg(Camera(true).Build());
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var bad = (byte[])good.Clone();
            for (int k = 0; k < 6; k++) bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            if (rng.Next(3) == 0) Array.Resize(ref bad, rng.Next(bad.Length));
            ExifReader.Read(bad);   // may give fewer fields, or none, but must not throw
        }
    }

    [Theory]
    [InlineData(1, "Normal")]
    [InlineData(3, "Upside down")]
    [InlineData(8, "Turned 90° anticlockwise")]
    public void Orientation_names(int o, string text) => Assert.Equal(text, ExifReader.OrientationText(o));
}

public class ImageOrientationTests
{
    // Where the stored picture's top-left pixel ends up once shown upright (as ImageMagick's -auto-orient does).
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 3, 0)]   // mirrored: top-right
    [InlineData(3, 3, 1)]   // upside down: bottom-right
    [InlineData(4, 0, 1)]   // upside down, mirrored: bottom-left
    [InlineData(5, 0, 0)]   // diagonal mirror: stays top-left
    [InlineData(6, 1, 0)]   // quarter turn clockwise: top-right of the 2-wide result
    [InlineData(7, 1, 3)]   // other diagonal: bottom-right
    [InlineData(8, 0, 3)]   // quarter turn anticlockwise: bottom-left
    public void The_top_left_corner_lands_where_it_should(int orientation, int x, int y)
        => Assert.Equal((x, y), ImageOrientation.Target(orientation, 0, 0, 4, 2));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void Every_pixel_lands_somewhere_different_inside_the_picture(int orientation)
    {
        int w = 4, h = 3;
        bool swap = ImageOrientation.SwapsSides(orientation);
        int dw = swap ? h : w, dh = swap ? w : h;
        var seen = new HashSet<(int, int)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = ImageOrientation.Target(orientation, x, y, w, h);
                Assert.InRange(t.X, 0, dw - 1);
                Assert.InRange(t.Y, 0, dh - 1);
                Assert.True(seen.Add(t));
            }
    }
}
