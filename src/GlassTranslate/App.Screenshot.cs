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
    /// <summary>Builds the README picture: the real window placed over part of a page, with that part really
    /// recognized and translated while the rest of the page stays as it is. Composed offscreen, since the
    /// overlay is hidden from screen capture.</summary>
    private async Task ReadmeShotAsync(string pagePath, string output, string engineId, string target, Int32Rect area)
    {
        var log = new List<string>();
        try
        {
            var page = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(Path.GetFullPath(pagePath))), PixelFormats.Bgra32, null, 0);
            var crop = new CroppedBitmap(page, area);
            var data = new byte[area.Width * area.Height * 4];
            crop.CopyPixels(data, area.Width * 4, 0);
            var frame = new Frame(area.Width, area.Height, data);

            // Size the window so its transparent area exactly fits the chosen part of the page.
            var w = new MainWindow { ShowActivated = false, Left = -6000, Top = 0, Width = 960, Height = 600 };
            w.Show();
            w.Left = -6000;
            await Task.Delay(500);
            w.Width += area.Width - w.Hole.ActualWidth;
            w.Height += area.Height - w.Hole.ActualHeight;
            await Task.Delay(500);

            using var ocr = new OcrEngine();
            var regions = TextLayout.Group(frame, ocr.Recognize(frame, ModelStore.AutoGroups()));
            var translated = await TranslatorRegistry.Get(engineId).TranslateAsync(regions.Select(r => r.Text).ToList(), null, target, default);
            var keep = Enumerable.Range(0, regions.Count)
                .Where(i => !System.Text.RegularExpressions.Regex.IsMatch(regions[i].Text, @"^[A-Z0-9\W]{1,5}$")
                            && !string.Equals(translated[i].Text.Trim(), regions[i].Text.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var i in keep) log.Add($"{regions[i].Text} => {translated[i].Text}");

            w.UpdateDetected("th");
            w.SetStatus(Strings.Format("Done", regions.Count, 840) + " · " + TranslatorRegistry.Get(engineId).DisplayName,
                GlassTranslate.MainWindow.StatusKind.Ok);
            await Task.Delay(300);

            var root = (FrameworkElement)w.Content;
            var hole = w.Hole.TranslatePoint(new Point(0, 0), root);
            double ww = root.ActualWidth, wh = root.ActualHeight;
            var windowAt = new Point(area.X - hole.X, area.Y - hole.Y);

            var translations = new Canvas { Width = frame.Width, Height = frame.Height };
            OverlayWindow.Paint(translations, 1.0, frame, keep.Select(i => regions[i]).ToList(),
                keep.Select(i => translated[i].Text).ToList(), new FontFamily("Segoe UI"), 0);
            translations.Measure(new Size(frame.Width, frame.Height));
            translations.Arrange(new Rect(0, 0, frame.Width, frame.Height));

            const double scale = 1.5; // crisp enough on high-DPI screens; GitHub scales it down
            RenderTargetBitmap Shot(Visual v, double width, double height)
            {
                var b = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                b.Render(v);
                return b;
            }
            var windowShot = Shot(root, ww, wh);
            var textShot = Shot(translations, frame.Width, frame.Height);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(page, new Rect(0, 0, page.PixelWidth, page.PixelHeight));
                // Soft shadow so the floating window reads as a separate layer above the page.
                for (int k = 1; k <= 12; k++)
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(7, 0, 0, 0)), null,
                        new Rect(windowAt.X - k + 4, windowAt.Y - k + 8, ww + 2 * k, wh + 2 * k), 14 + k, 14 + k);
                // The shadow must not darken what shows through the window's transparent middle.
                dc.DrawImage(crop, new Rect(area.X, area.Y, area.Width, area.Height));
                dc.DrawImage(textShot, new Rect(area.X, area.Y, area.Width, area.Height));
                dc.DrawImage(windowShot, new Rect(windowAt, new Size(ww, wh)));
            }
            var final = Shot(dv, page.PixelWidth, page.PixelHeight);
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
