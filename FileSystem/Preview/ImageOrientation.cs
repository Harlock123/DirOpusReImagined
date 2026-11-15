using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Turns and mirrors a decoded picture the way its EXIF orientation (1–8) says, so a photo stored
/// sideways shows upright.
/// </summary>
public static class ImageOrientation
{
    /// <summary>True for the orientations that swap width and height (a quarter turn).</summary>
    public static bool SwapsSides(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>Where the pixel at (<paramref name="x"/>, <paramref name="y"/>) of a w×h picture goes once turned upright.</summary>
    public static (int X, int Y) Target(int orientation, int x, int y, int w, int h) => orientation switch
    {
        2 => (w - 1 - x, y),             // mirrored
        3 => (w - 1 - x, h - 1 - y),     // upside down
        4 => (x, h - 1 - y),             // upside down, mirrored
        5 => (y, x),                     // mirrored along the diagonal
        6 => (h - 1 - y, x),             // a quarter turn clockwise
        7 => (h - 1 - y, w - 1 - x),     // the other diagonal
        8 => (y, w - 1 - x),             // a quarter turn anticlockwise
        _ => (x, y),
    };

    /// <summary>The picture turned upright, or null when it can't be (an unusual pixel format), in which case it's shown as stored.</summary>
    public static Bitmap? Apply(Bitmap source, int orientation)
    {
        if (orientation is < 2 or > 8) return null;
        var format = source.Format;
        if (format is not { } pf || pf.BitsPerPixel != 32) return null;

        int w = source.PixelSize.Width, h = source.PixelSize.Height;
        var pixels = new int[w * h];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { source.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length * 4, w * 4); }
        finally { handle.Free(); }

        bool swap = SwapsSides(orientation);
        int dw = swap ? h : w, dh = swap ? w : h;
        var turned = new int[dw * dh];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (tx, ty) = Target(orientation, x, y, w, h);
                turned[ty * dw + tx] = pixels[y * w + x];
            }

        var result = new WriteableBitmap(new PixelSize(dw, dh), source.Dpi, pf, source.AlphaFormat ?? AlphaFormat.Premul);
        using (var fb = result.Lock())
        {
            for (int row = 0; row < dh; row++)
                Marshal.Copy(turned, row * dw, fb.Address + row * fb.RowBytes, dw);
        }
        return result;
    }
}
