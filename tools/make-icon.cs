#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishAot=false
#:property PublishTrimmed=false

// Draws the app icon and writes it as a multi-size .ico (PNG entries) plus a 256 px preview PNG.
// Usage: dotnet run tools/make-icon.cs -- <out.ico> <preview.png>
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
string ico = args.Length > 0 ? args[0] : "app.ico";
string preview = args.Length > 1 ? args[1] : "app.png";

var t = new Thread(() =>
{
    var pngs = sizes.Select(s => Render(s)).ToList();
    File.WriteAllBytes(preview, pngs[^1]);
    using var fs = File.Create(ico);
    using var w = new BinaryWriter(fs);
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
        w.Write(pngs[i].Length); w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) w.Write(p);
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
Console.WriteLine($"wrote {ico}");

static byte[] Render(int size)
{
    // Designed on a 256 grid and scaled; tiny sizes get thicker strokes so they stay readable.
    var dv = new DrawingVisual();
    using (var dc = dv.RenderOpen())
    {
        double k = size / 256.0;
        dc.PushTransform(new ScaleTransform(k, k));
        var bg = new LinearGradientBrush(Color.FromRgb(0x6A, 0xA6, 0xFF), Color.FromRgb(0x2F, 0x6F, 0xEB), 45);
        dc.DrawRoundedRectangle(bg, null, new Rect(8, 8, 240, 240), 56, 56);

        // Viewfinder corners: the "frame" you put over text.
        double sw = size <= 24 ? 22 : size <= 48 ? 17 : 14;
        var pen = new Pen(Brushes.White, sw) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        double a = 50, b = 206, l = 44;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            void Corner(Point p0, Point p1, Point p2) { c.BeginFigure(p0, false, false); c.LineTo(p1, true, true); c.LineTo(p2, true, true); }
            Corner(new(a, a + l), new(a, a), new(a + l, a));
            Corner(new(b - l, a), new(b, a), new(b, a + l));
            Corner(new(b, b - l), new(b, b), new(b - l, b));
            Corner(new(a + l, b), new(a, b), new(a, b - l));
        }
        dc.DrawGeometry(null, pen, g);

        // "文A": source and target script. At 16-24 px only the "A" is legible, so draw it alone and larger.
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var cjk = new Typeface(new FontFamily("Microsoft YaHei UI, Yu Gothic UI, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        FormattedText Text(string s, Typeface tf, double em) =>
            new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, em, Brushes.White, 1.0);
        if (size <= 24)
        {
            var big = Text("A", face, 120);
            dc.DrawText(big, new Point(128 - big.Width / 2, 128 - big.Height / 2));
        }
        else
        {
            var zh = Text("文", cjk, 74);
            var en = Text("A", face, 84);
            dc.DrawText(zh, new Point(78, 66));
            dc.DrawText(en, new Point(178 - en.Width, 190 - en.Height + 6));
        }
        dc.Pop();
    }
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(dv);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}
