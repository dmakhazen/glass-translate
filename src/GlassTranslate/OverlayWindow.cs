using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GlassTranslate.Ocr;

namespace GlassTranslate;

/// <summary>Click-through window that paints translations over the original text, Google Lens style.
/// It is excluded from screen capture, so the next OCR pass still sees the original page underneath.</summary>
public sealed class OverlayWindow : Window
{
    private readonly Canvas _canvas = new();

    public bool ExcludedFromCapture { get; private set; }

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            ExcludedFromCapture = Environment.GetEnvironmentVariable("GLASS_NO_EXCLUDE") == null
                && Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
        };
    }

    public void PlaceAt(int x, int y, int w, int h)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, Math.Max(1, w), Math.Max(1, h),
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    public void Clear() => _canvas.Children.Clear();

    public void Render(Frame frame, IReadOnlyList<TextRegion> blocks, IReadOnlyList<string> translations,
        FontFamily font, double fixedSize) =>
        Paint(_canvas, VisualTreeHelper.GetDpi(this).DpiScaleX, frame, blocks, translations, font, fixedSize);

    public static void Paint(Canvas canvas, double scale, Frame frame, IReadOnlyList<TextRegion> blocks,
        IReadOnlyList<string> translations, FontFamily font, double fixedSize)
    {
        canvas.Children.Clear();
        // Bottom to top, so each block's text is drawn over the extra background of the block below it.
        foreach (int i in Enumerable.Range(0, blocks.Count).OrderByDescending(i => blocks[i].Box.Y))
        {
            var b = blocks[i];
            var (bg, fg) = PickColors(frame, b.Box);
            double w = b.Box.W / scale, h = b.Box.H / scale;
            var text = new System.Windows.Controls.TextBlock
            {
                Text = translations[i],
                Foreground = new SolidColorBrush(fg),
                FontFamily = font,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text.FontSize = fixedSize > 0 ? fixedSize : FitFontSize(text, w, h, b.LineHeight / scale);
            // Thai (and Vietnamese) marks above and below the letters stick out of the detected box,
            // so the background reaches a little further than the text to cover them.
            // Stacked marks above Thai letters reach the highest, hence the larger top margin.
            double top = Math.Round(b.LineHeight / scale * 0.35), bottom = Math.Round(b.LineHeight / scale * 0.2);
            var box = new Border
            {
                Background = new SolidColorBrush(bg),
                Width = w,
                MinHeight = h + top + bottom,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(1, top, 1, bottom),
                Child = text,
            };
            Canvas.SetLeft(box, b.Box.X / scale);
            Canvas.SetTop(box, b.Box.Y / scale - top);
            canvas.Children.Add(box);
        }
    }

    /// <summary>Largest font size (up to the original's) at which the translation fits the original block.</summary>
    private static double FitFontSize(System.Windows.Controls.TextBlock text, double w, double h, double lineHeight)
    {
        double size = Math.Clamp(lineHeight * 0.62, 8, 72);
        while (size > 7)
        {
            text.FontSize = size;
            text.Measure(new Size(Math.Max(1, w - 2), double.PositiveInfinity));
            if (text.DesiredSize.Height <= h + 1) break;
            size *= 0.92;
        }
        return Math.Max(size, 7);
    }

    private static (Color bg, Color fg) PickColors(Frame f, PixelRect r)
    {
        var (bg, fg) = TextColors.Pick(f, r);
        // Keep the page's own text colour unless it would be hard to read.
        if (TextColors.Contrast(fg, bg) < 60)
            fg = TextColors.Luma(bg) > 140 ? Colors.Black : Colors.White;
        return (bg, fg);
    }
}
