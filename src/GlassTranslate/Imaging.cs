using System.Windows.Media;
using GlassTranslate.Ocr;

namespace GlassTranslate;

public static class TextColors
{
    /// <summary>Background = median colour of the box border; text = average of pixels that differ from it.</summary>
    public static (Color bg, Color fg) Pick(Frame f, PixelRect r)
    {
        var rs = new List<byte>(); var gs = new List<byte>(); var bs = new List<byte>();
        void Add(int x, int y) { var (b, g, rr) = f.Pixel(x, y); bs.Add(b); gs.Add(g); rs.Add(rr); }
        for (int x = r.X; x < r.Right; x += 2) { Add(x, r.Y); Add(x, r.Bottom - 1); }
        for (int y = r.Y; y < r.Bottom; y += 2) { Add(r.X, y); Add(r.Right - 1, y); }
        byte Median(List<byte> v) { v.Sort(); return v[v.Count / 2]; }
        var bg = Color.FromRgb(Median(rs), Median(gs), Median(bs));

        long sr = 0, sg = 0, sb = 0, n = 0;
        for (int y = r.Y; y < r.Bottom; y += 2)
            for (int x = r.X; x < r.Right; x += 2)
            {
                var (b, g, rr) = f.Pixel(x, y);
                if (Distance(Color.FromRgb(rr, g, b), bg) < 150) continue;
                sr += rr; sg += g; sb += b; n++;
            }
        var fg = n > 0 ? Color.FromRgb((byte)(sr / n), (byte)(sg / n), (byte)(sb / n)) : Colors.Black;
        return (bg, fg);
    }

    public static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    public static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
    public static double Contrast(Color a, Color b) => Math.Abs(Luma(a) - Luma(b));
}

/// <summary>32-bit BGRA image in physical pixels.</summary>
public sealed class Frame(int width, int height, byte[] bgra)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = bgra;

    public (byte b, byte g, byte r) Pixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int i = (y * Width + x) * 4;
        return (Bgra[i], Bgra[i + 1], Bgra[i + 2]);
    }

    /// <summary>Bilinear sample of one channel (0=B,1=G,2=R) at fractional coordinates.</summary>
    public float Sample(float x, float y, int c)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float fx = x - x0, fy = y - y0;
        float a = Bgra[(y0 * Width + x0) * 4 + c], b = Bgra[(y0 * Width + x1) * 4 + c];
        float d = Bgra[(y1 * Width + x0) * 4 + c], e = Bgra[(y1 * Width + x1) * 4 + c];
        return (a + (b - a) * fx) * (1 - fy) + (d + (e - d) * fx) * fy;
    }

    /// <summary>Small grayscale thumbnail used to detect whether the captured area changed.</summary>
    public byte[] Signature(int size = 48)
    {
        var sig = new byte[size * size];
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                var (b, g, r) = Pixel(i * Width / size, j * Height / size);
                sig[j * size + i] = (byte)((b + g * 2 + r) / 4);
            }
        return sig;
    }

    /// <summary>Share of thumbnail cells that differ noticeably (0..1); 1 when there's nothing to compare.</summary>
    public static double SignatureChange(byte[]? a, byte[]? b)
    {
        if (a == null || b == null || a.Length != b.Length) return 1;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > 12) diff++;
        return (double)diff / a.Length;
    }

    public static bool SameSignature(byte[]? a, byte[]? b) => SignatureChange(a, b) <= 0.0025;

    /// <summary>Scrolling or switching pages, as opposed to a small animation somewhere in the area.</summary>
    public static bool BigChange(byte[]? a, byte[]? b) => SignatureChange(a, b) > 0.2;
}

public static class ScreenCapture
{
    /// <summary>Copies a rectangle of the screen (physical pixels). Windows marked
    /// WDA_EXCLUDEFROMCAPTURE are not included, so our own overlay never gets captured.</summary>
    public static Frame Capture(int x, int y, int w, int h)
    {
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr bmp = Native.CreateCompatibleBitmap(screen, w, h);
        IntPtr old = Native.SelectObject(mem, bmp);
        try
        {
            Native.BitBlt(mem, 0, 0, w, h, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT);
            Native.SelectObject(mem, old);
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32
            };
            var data = new byte[w * h * 4];
            Native.GetDIBits(mem, bmp, 0, (uint)h, data, ref bmi, 0);
            return new Frame(w, h, data);
        }
        finally
        {
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
