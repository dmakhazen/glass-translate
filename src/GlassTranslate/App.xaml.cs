using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassTranslate.Ocr;

namespace GlassTranslate;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Diagnostics: GlassTranslate.exe --ocr-test image.png out.txt [group,group]
        if (e.Args is ["--ocr-test", var image, var output, ..])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var groups = e.Args.Length > 3 ? e.Args[3].Split(',') : ModelStore.AutoGroups().ToArray();
            var bmp = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(Path.GetFullPath(image))), PixelFormats.Bgra32, null, 0);
            var data = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
            bmp.CopyPixels(data, bmp.PixelWidth * 4, 0);
            using var ocr = new OcrEngine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var frame = new Frame(bmp.PixelWidth, bmp.PixelHeight, data);
            var lines = ocr.Recognize(frame, groups);
            var report = lines.Select(l => $"{l.Box} {l.Confidence:F2} {TextColors.Pick(frame, l.Box)} {l.Text}").ToList();
            report.Add($"--- regions ({sw.ElapsedMilliseconds} ms)");
            report.AddRange(TextLayout.Group(frame, lines).Select(r => $"{r.Box} {r.Text}"));
            File.WriteAllLines(output, report);
            Shutdown();
            return;
        }
        // Diagnostics: GlassTranslate.exe --render-test image.png out.png engine targetLang
        if (e.Args is ["--render-test", var img, var outPng, var engineId, var target])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RenderTestAsync(img, outPng, engineId, target);
            return;
        }
        // Diagnostics: GlassTranslate.exe --ui-snapshot out.png [help|settings] — screenshot of the window.
        if (e.Args is ["--ui-snapshot", var shot, ..])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var w = new MainWindow { Left = 60, Top = 60, Width = 820, Height = 560 };
            w.Show();
            _ = SnapshotAsync(w, shot, e.Args.Length > 2 ? e.Args[2] : "");
            return;
        }
        // README picture: GlassTranslate.exe --readme-shot page.png out.png engine targetLang x y width height
        // (the rectangle is the part of the page the frame covers)
        if (e.Args is ["--readme-shot", var page, var picture, var shotEngine, var shotTarget, var ax, var ay, var aw, var ah])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = ReadmeShotAsync(page, picture, shotEngine, shotTarget,
                new Int32Rect(int.Parse(ax), int.Parse(ay), int.Parse(aw), int.Parse(ah)));
            return;
        }
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }

    private async Task SnapshotAsync(MainWindow w, string output, string popup)
    {
        await Task.Delay(800);
        if (popup == "help") w.HelpPopup.IsOpen = true;
        if (popup == "settings") w.SettingsPopup.IsOpen = true;
        if (popup == "donate") w.DonatePopup.IsOpen = true;
        if (popup.StartsWith("tour")) w.StartTour(int.Parse(popup[4..]));
        await Task.Delay(300);
        await Task.Delay(700);
        var dpi = VisualTreeHelper.GetDpi(w);
        int x = (int)(w.Left * dpi.DpiScaleX), y = (int)(w.Top * dpi.DpiScaleY);
        var frame = ScreenCapture.Capture(x, y, (int)(w.ActualWidth * dpi.DpiScaleX), (int)((w.ActualHeight + 120) * dpi.DpiScaleY));
        var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(output)) enc.Save(fs);
        w.Close();
        Shutdown();
    }

    private async Task RenderTestAsync(string image, string output, string engineId, string target)
    {
        var log = new List<string>();
        try
        {
            var src = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(Path.GetFullPath(image))), PixelFormats.Bgra32, null, 0);
            var data = new byte[src.PixelWidth * src.PixelHeight * 4];
            src.CopyPixels(data, src.PixelWidth * 4, 0);
            var frame = new Frame(src.PixelWidth, src.PixelHeight, data);
            using var ocr = new OcrEngine();
            ocr.Recognize(frame, ModelStore.AutoGroups()); // warm-up: model loading isn't part of a normal cycle
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var regions = TextLayout.Group(frame, ocr.Recognize(frame, ModelStore.AutoGroups()));
            log.Add($"ocr {sw.ElapsedMilliseconds} ms (det {ocr.LastDetectMs}, rec {ocr.LastRecognizeMs})");
            sw.Restart();
            var tr = await Translation.TranslatorRegistry.Get(engineId).TranslateAsync(regions.Select(r => r.Text).ToList(), null, target, default);
            log.Add($"translate {sw.ElapsedMilliseconds} ms");
            for (int i = 0; i < regions.Count; i++) log.Add($"{regions[i].Text} => {tr[i].Text} [{tr[i].DetectedLanguage}]");

            var canvas = new System.Windows.Controls.Canvas { Width = frame.Width, Height = frame.Height };
            var bg = new System.Windows.Controls.Image { Source = src, Width = frame.Width, Height = frame.Height };
            OverlayWindow.Paint(canvas, 1.0, frame, regions, tr.Select(t => t.Text).ToList(), new FontFamily("Segoe UI"), 0);
            canvas.Children.Insert(0, bg);
            canvas.Measure(new Size(frame.Width, frame.Height));
            canvas.Arrange(new Rect(0, 0, frame.Width, frame.Height));
            var rtb = new RenderTargetBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(output)) enc.Save(fs);
        }
        catch (Exception ex) { log.Add(ex.ToString()); }
        File.WriteAllLines(output + ".txt", log);
        Shutdown();
    }
}
