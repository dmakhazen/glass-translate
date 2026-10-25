using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassTranslate.Ocr;
using GlassTranslate.Translation;

namespace GlassTranslate;

public partial class App
{
    /// <summary>Builds the README picture: the real window placed over a page, with the page really
    /// recognized and translated. Composed offscreen, since the overlay is hidden from screen capture.</summary>
    private async Task ReadmeShotAsync(string pagePath, string output, string engineId, string target)
    {
        var log = new List<string>();
        try
        {
            var page = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(Path.GetFullPath(pagePath))), PixelFormats.Bgra32, null, 0);
            var data = new byte[page.PixelWidth * page.PixelHeight * 4];
            page.CopyPixels(data, page.PixelWidth * 4, 0);
            var frame = new Frame(page.PixelWidth, page.PixelHeight, data);

            // Size the window so its transparent area exactly fits the page.
            var w = new MainWindow { ShowActivated = false, Left = -6000, Top = 0, Width = 960, Height = 600 };
            w.Show();
            w.Left = -6000;
            await Task.Delay(500);
            w.Width += page.PixelWidth - w.Hole.ActualWidth;
            w.Height += page.PixelHeight - w.Hole.ActualHeight;
            await Task.Delay(500);

            using var ocr = new OcrEngine();
            var regions = TextLayout.Group(frame, ocr.Recognize(frame, ModelStore.AutoGroups()));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var translated = await TranslatorRegistry.Get(engineId).TranslateAsync(regions.Select(r => r.Text).ToList(), null, target, default);
            var keep = Enumerable.Range(0, regions.Count)
                .Where(i => !System.Text.RegularExpressions.Regex.IsMatch(regions[i].Text, @"^[A-Z0-9\W]{1,5}$")
                            && !string.Equals(translated[i].Text.Trim(), regions[i].Text.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var i in keep) log.Add($"{regions[i].Text} => {translated[i].Text}");

            w.UpdateDetected("th");
            w.SetStatus(Strings.Format("Done", regions.Count, 840) + " · " + TranslatorRegistry.Get(engineId).DisplayName, GlassTranslate.MainWindow.StatusKind.Ok);
            await Task.Delay(300);

            var root = (FrameworkElement)w.Content;
            var hole = w.Hole.TranslatePoint(new Point(0, 0), root);
            double ww = root.ActualWidth, wh = root.ActualHeight;
            const double margin = 28;

            var translations = new Canvas { Width = frame.Width, Height = frame.Height };
            OverlayWindow.Paint(translations, 1.0, frame, keep.Select(i => regions[i]).ToList(),
                keep.Select(i => translated[i].Text).ToList(), new FontFamily("Segoe UI"), 0);
            translations.Measure(new Size(frame.Width, frame.Height));
            translations.Arrange(new Rect(0, 0, frame.Width, frame.Height));

            const double scale = 2; // crisp on high-DPI screens; GitHub scales it down
            var windowShot = new RenderTargetBitmap((int)(ww * scale), (int)(wh * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            windowShot.Render(root);
            var textShot = new RenderTargetBitmap((int)(frame.Width * scale), (int)(frame.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            textShot.Render(translations);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var pageColor = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
                dc.DrawRectangle(pageColor, null, new Rect(0, 0, ww + 2 * margin, wh + 2 * margin));
                var origin = new Point(margin + hole.X, margin + hole.Y);
                dc.DrawImage(page, new Rect(origin, new Size(frame.Width, frame.Height)));
                dc.DrawImage(textShot, new Rect(origin, new Size(frame.Width, frame.Height)));
                dc.DrawImage(windowShot, new Rect(margin, margin, ww, wh));
            }
            var final = new RenderTargetBitmap((int)((ww + 2 * margin) * scale), (int)((wh + 2 * margin) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            final.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(final));
            using (var fs = File.Create(output)) enc.Save(fs);
            w.Close();
        }
        catch (Exception ex) { log.Add(ex.ToString()); }
        File.WriteAllLines(output + ".txt", log);
        Shutdown();
    }
}
