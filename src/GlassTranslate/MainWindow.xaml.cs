using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GlassTranslate.Ocr;
using GlassTranslate.Translation;

namespace GlassTranslate;

public partial class MainWindow : Window
{
    private static readonly int[] Intervals = [0, 1, 2, 3, 5, 10];
    private static readonly double[] FontSizes = [0, 10, 11, 12, 14, 16, 18, 20, 24, 28];

    private readonly Settings _settings = Settings.Load();
    private readonly OcrEngine _ocr = new();
    private readonly OverlayWindow _overlay = new();
    private readonly Dictionary<string, Translation.Translation> _cache = new();
    private readonly CancellationTokenSource _closing = new();
    private byte[]? _lastSignature;
    private bool _busy, _loading = true;
    private string? _detected;

    public MainWindow()
    {
        InitializeComponent();
        Strings.Current = _settings.UiLanguage;
        if (!System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json")))
        {
            // First run: translate into the system language when we support it.
            string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (Languages.Find(sys) != null) _settings.TargetLanguage = sys;
        }
        Left = _settings.Left; Top = _settings.Top; Width = _settings.Width; Height = _settings.Height;
        FitOnScreen();

        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList();
        FontBox.SelectedItem = _settings.FontFamily;
        UiLanguageBox.ItemsSource = Strings.UiLanguages.Select(l => l.Name).ToList();
        UiLanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Strings.UiLanguages, l => l.Code == _settings.UiLanguage));
        EngineBox.ItemsSource = TranslatorRegistry.All.Select(t => t.DisplayName).ToList();
        EngineBox.SelectedIndex = Math.Max(0, TranslatorRegistry.All.ToList().FindIndex(t => t.Id == _settings.Engine));
        PinToggle.IsChecked = _settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        ApplyUiLanguage();
        _loading = false;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += (_, _) => OnGeometryChanged();
        SizeChanged += (_, _) => OnGeometryChanged();
        StateChanged += OnStateChanged;
    }

    // ---------- window plumbing ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_NCHITTEST) return IntPtr.Zero;
        int x = (short)(lParam.ToInt64() & 0xFFFF), y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        // The grip in the status bar acts as the bottom-right resize corner.
        var onGrip = ResizeGrip.PointFromScreen(new Point(x, y));
        if (onGrip.X >= 0 && onGrip.Y >= 0 && onGrip.X <= ResizeGrip.ActualWidth && onGrip.Y <= ResizeGrip.ActualHeight)
        {
            handled = true;
            return Native.HTBOTTOMRIGHT;
        }
        // Resize from the outer 8px band, since a borderless transparent window has no native frame.
        Native.GetWindowRect(hwnd, out var r);
        int band = (int)(8 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        bool l = x < r.Left + band, rt = x >= r.Right - band, t = y < r.Top + band, b = y >= r.Bottom - band;
        int hit = (t, b, l, rt) switch
        {
            (true, _, true, _) => Native.HTTOPLEFT,
            (true, _, _, true) => Native.HTTOPRIGHT,
            (_, true, true, _) => Native.HTBOTTOMLEFT,
            (_, true, _, true) => Native.HTBOTTOMRIGHT,
            (true, _, _, _) => Native.HTTOP,
            (_, true, _, _) => Native.HTBOTTOM,
            (_, _, true, _) => Native.HTLEFT,
            (_, _, _, true) => Native.HTRIGHT,
            _ => 0,
        };
        if (hit == 0) return IntPtr.Zero;
        handled = true;
        return hit;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _overlay.Owner = this;
        _overlay.Show();
        PlaceOverlay();
        _ = RunLoopAsync(_closing.Token);
        if (!_settings.TourShown)
            Dispatcher.BeginInvoke(() => StartTour(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FitOnScreen()
    {
        var area = SystemParameters.VirtualScreenWidth;
        if (Left < SystemParameters.VirtualScreenLeft || Left > SystemParameters.VirtualScreenLeft + area - 100) Left = 100;
        if (Top < SystemParameters.VirtualScreenTop || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100) Top = 100;
    }

    private (int x, int y, int w, int h) HoleRect()
    {
        var p0 = Hole.PointToScreen(new Point(0, 0));
        var p1 = Hole.PointToScreen(new Point(Hole.ActualWidth, Hole.ActualHeight));
        return ((int)Math.Round(p0.X), (int)Math.Round(p0.Y), (int)Math.Round(p1.X - p0.X), (int)Math.Round(p1.Y - p0.Y));
    }

    private void PlaceOverlay()
    {
        if (!IsLoaded || Hole.ActualWidth < 1) return;
        var (x, y, w, h) = HoleRect();
        _overlay.PlaceAt(x, y, w, h);
    }

    private void OnGeometryChanged()
    {
        _overlay.Clear();
        _lastSignature = null;
        PlaceOverlay();
        if (_tourStep >= 0) Dispatcher.BeginInvoke(ShowTourStep, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            e.OriginalSource is not (Border or WrapPanel or DockPanel or Grid or System.Windows.Controls.TextBlock)) return;
        _restoreBounds = null; // moving a maximized frame turns it back into a normal one
        UpdateMaximizeButton();
        DragMove();
    }

    // ---------- window buttons ----------

    private Rect? _restoreBounds;

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Fills the work area of the current monitor (WindowState.Maximized would also cover the taskbar).</summary>
    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (_restoreBounds is { } r)
        {
            _restoreBounds = null;
            Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
        }
        else
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref info);
            var dpi = VisualTreeHelper.GetDpi(this);
            var work = info.rcWork;
            _restoreBounds = new Rect(Left, Top, Width, Height);
            Left = work.Left / dpi.DpiScaleX; Top = work.Top / dpi.DpiScaleY;
            Width = (work.Right - work.Left) / dpi.DpiScaleX; Height = (work.Bottom - work.Top) / dpi.DpiScaleY;
        }
        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        MaximizeButton.Content = _restoreBounds == null ? "" : "";
        MaximizeButton.ToolTip = Strings.Get(_restoreBounds == null ? "Maximize" : "Restore");
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) OnGeometryChanged();
    }

    // ---------- help and status ----------

    private void OnHelp(object sender, RoutedEventArgs e) => HelpPopup.IsOpen = !HelpPopup.IsOpen;

    private void OnDonate(object sender, RoutedEventArgs e) => DonatePopup.IsOpen = !DonatePopup.IsOpen;

    private void OnDonateLink(object sender, RoutedEventArgs e)
    {
        DonatePopup.IsOpen = false;
        if (sender is FrameworkElement { Tag: string url })
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Help text: lines starting with "# " are headings, the rest are bullet points.</summary>
    private void BuildHelp()
    {
        HelpPanel.Children.Clear();
        foreach (var line in Strings.Get("HelpText").Split('\n'))
        {
            if (line.StartsWith("# "))
            {
                HelpPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = line[2..], FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush"),
                    Margin = new Thickness(0, HelpPanel.Children.Count == 0 ? 0 : 10, 0, 6),
                });
                continue;
            }
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var dot = new System.Windows.Controls.TextBlock { Text = "•", Margin = new Thickness(2, 0, 8, 0), Foreground = (Brush)FindResource("AccentBrush") };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 18,
                Foreground = (Brush)FindResource("TextDimBrush"),
            });
            HelpPanel.Children.Add(row);
        }
    }

    private enum StatusKind { Idle, Busy, Ok, Error }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
        StatusDot.Fill = kind switch
        {
            StatusKind.Busy => new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x3D)),
            StatusKind.Ok => new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C)),
            StatusKind.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C)),
            _ => (Brush)FindResource("TextDimBrush"),
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing.Cancel();
        _settings.Left = Left; _settings.Top = Top; _settings.Width = Width; _settings.Height = Height;
        _settings.Save();
        _overlay.Close();
        _ocr.Dispose();
        base.OnClosed(e);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------- controls ----------

    private void ApplyUiLanguage()
    {
        _loading = true;
        Strings.Current = _settings.UiLanguage;

        var sources = new List<string> { AutoLabel() };
        sources.AddRange(Languages.All.Select(l => l.NativeName));
        SourceBox.ItemsSource = sources;
        SourceBox.SelectedIndex = _settings.SourceLanguage == Languages.Auto
            ? 0 : Languages.All.ToList().FindIndex(l => l.Code == _settings.SourceLanguage) + 1;
        TargetBox.ItemsSource = Languages.All.Select(l => l.NativeName).ToList();
        TargetBox.SelectedIndex = Math.Max(0, Languages.All.ToList().FindIndex(l => l.Code == _settings.TargetLanguage));

        IntervalBox.ItemsSource = Intervals.Select(i => i == 0 ? Strings.Get("Manual") : Strings.Format("Every", i)).ToList();
        IntervalBox.SelectedIndex = Math.Max(0, Array.IndexOf(Intervals, _settings.IntervalSeconds));
        FontSizeBox.ItemsSource = FontSizes.Select(s => s == 0 ? Strings.Get("FitSize") : s.ToString(CultureInfo.InvariantCulture)).ToList();
        FontSizeBox.SelectedIndex = Math.Max(0, Array.IndexOf(FontSizes, _settings.FontSize));

        SourceBox.ToolTip = Strings.Get("From");
        TargetBox.ToolTip = Strings.Get("To");
        EngineBox.ToolTip = Strings.Get("Engine");
        IntervalBox.ToolTip = Strings.Get("Interval");
        TranslateButton.ToolTip = Strings.Get("TranslateNow");
        OriginalToggle.ToolTip = Strings.Get("ShowOriginal");
        PinToggle.ToolTip = Strings.Get("AlwaysOnTop");
        SettingsButton.ToolTip = Strings.Get("Settings");
        HelpButton.ToolTip = Strings.Get("Help");
        TourButton.ToolTip = TourStrings.Current.Tour;
        DonateButton.ToolTip = Strings.Get("Donate");
        DonateTitle.Text = Strings.Get("DonateTitle");
        DonateText.Text = Strings.Get("DonateText");
        KofiHint.Text = Strings.Get("KofiHint");
        BoostyHint.Text = Strings.Get("BoostyHint");
        MinimizeButton.ToolTip = Strings.Get("Minimize");
        CloseButton.ToolTip = Strings.Get("Close");
        UpdateMaximizeButton();
        BuildHelp();
        FontLabel.Text = Strings.Get("Font");
        FontSizeLabel.Text = Strings.Get("FontSize");
        UiLanguageLabel.Text = Strings.Get("UiLanguage");
        SetStatus(Strings.Get("Ready"), StatusKind.Idle);
        _loading = false;
    }

    private string AutoLabel()
    {
        string auto = Strings.Get("Auto");
        return _detected != null && Languages.Find(_detected) is { } d ? $"{auto} ({d.NativeName})" : auto;
    }

    private void UpdateDetected(string? code)
    {
        if (code == null || code == _detected) return;
        _detected = code;
        if (SourceBox.ItemsSource is List<string> list)
        {
            _loading = true;
            int sel = SourceBox.SelectedIndex;
            list[0] = AutoLabel();
            SourceBox.Items.Refresh();
            SourceBox.SelectedIndex = sel;
            _loading = false;
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.SourceLanguage = SourceBox.SelectedIndex <= 0 ? Languages.Auto : Languages.All[SourceBox.SelectedIndex - 1].Code;
        if (TargetBox.SelectedIndex >= 0) _settings.TargetLanguage = Languages.All[TargetBox.SelectedIndex].Code;
        _settings.Save();
        _ = RunCycleAsync(force: true);
    }

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EngineBox.SelectedIndex < 0) return;
        _settings.Engine = TranslatorRegistry.All[EngineBox.SelectedIndex].Id;
        _settings.Save();
        _ = RunCycleAsync(force: true);
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IntervalBox.SelectedIndex < 0) return;
        _settings.IntervalSeconds = Intervals[IntervalBox.SelectedIndex];
        _settings.Save();
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (FontBox.SelectedItem is string f) _settings.FontFamily = f;
        if (FontSizeBox.SelectedIndex >= 0) _settings.FontSize = FontSizes[FontSizeBox.SelectedIndex];
        _settings.Save();
        _ = RunCycleAsync(force: true);
    }

    private void OnUiLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || UiLanguageBox.SelectedIndex < 0) return;
        _settings.UiLanguage = Strings.UiLanguages[UiLanguageBox.SelectedIndex].Code;
        _settings.Save();
        ApplyUiLanguage();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsPopup.IsOpen = !SettingsPopup.IsOpen;

    private void OnTranslateNow(object sender, RoutedEventArgs e) => _ = RunCycleAsync(force: true);

    private void OnPinToggled(object sender, RoutedEventArgs e)
    {
        Topmost = PinToggle.IsChecked == true;
        _overlay.Topmost = Topmost;
        if (_loading) return;
        _settings.AlwaysOnTop = Topmost;
        _settings.Save();
    }

    private void OnOriginalToggled(object sender, RoutedEventArgs e) =>
        _overlay.Visibility = OriginalToggle.IsChecked == true ? Visibility.Hidden : Visibility.Visible;

    // ---------- capture → OCR → translate → overlay ----------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int interval = _settings.IntervalSeconds;
            try { await Task.Delay(interval > 0 ? interval * 1000 : 300, ct); }
            catch (TaskCanceledException) { return; }
            if (interval > 0 && !_busy && OriginalToggle.IsChecked != true && WindowState != WindowState.Minimized)
                await RunCycleAsync(force: false);
        }
    }

    private async Task RunCycleAsync(bool force)
    {
        if (_busy || !IsLoaded) return;
        _busy = true;
        try
        {
            var (x, y, w, h) = HoleRect();
            if (w < 8 || h < 8) return;
            var frame = await CaptureAsync(x, y, w, h);
            var sig = frame.Signature();
            if (!force && Frame.SameSignature(sig, _lastSignature)) return;
            // After a scroll the old translations sit in the wrong place; after a small animation they're still fine.
            if (force || Frame.BigChange(sig, _lastSignature)) _overlay.Clear();
            _lastSignature = sig;

            var groups = await EnsureModelsAsync();
            if (groups == null) return;

            SetStatus(Strings.Get("Reading"), StatusKind.Busy);
            var sw = Stopwatch.StartNew();
            var regions = await Task.Run(() => TextLayout.Group(frame, _ocr.Recognize(frame, groups)));
            Log.Write($"ocr {frame.Width}x{frame.Height}: det {_ocr.LastDetectMs} ms, rec {_ocr.LastRecognizeMs} ms, {regions.Count} regions");
            if (regions.Count == 0)
            {
                SetStatus(Strings.Get("NothingFound"), StatusKind.Idle);
                return;
            }

            SetStatus(Strings.Get("Translating"), StatusKind.Busy);
            long t0 = sw.ElapsedMilliseconds;
            var translated = await TranslateAsync(regions.Select(r => r.Text).ToList());
            Log.Write($"translate {sw.ElapsedMilliseconds - t0} ms");
            // Text that is already in the target language stays as it is on the page.
            // Short Latin abbreviations (COD, CRV, TWS) are left alone too.
            var keep = Enumerable.Range(0, regions.Count).Where(i =>
                !System.Text.RegularExpressions.Regex.IsMatch(regions[i].Text, @"^[A-Z0-9\W]{1,5}$") &&
                translated[i].DetectedLanguage != _settings.TargetLanguage &&
                !string.Equals(translated[i].Text.Trim(), regions[i].Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            var shown = keep.Select(i => regions[i]).ToList();
            var translations = keep.Select(i => translated[i].Text).ToList();

            // The page may have scrolled while we were busy; don't paint stale text over new content.
            var again = await CaptureAsync(x, y, w, h);
            if (Frame.BigChange(again.Signature(), sig) || HoleRect() != (x, y, w, h))
            {
                Log.Write("content changed while translating, discarded");
                _lastSignature = null;
                SetStatus(Strings.Get("Ready"), StatusKind.Idle);
                return;
            }

            _overlay.Render(frame, shown, translations, new FontFamily(_settings.FontFamily), _settings.FontSize);
            SetStatus(Strings.Format("Done", regions.Count, sw.ElapsedMilliseconds) +
                              " · " + TranslatorRegistry.Get(_settings.Engine).DisplayName, StatusKind.Ok);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_closing.IsCancellationRequested)
        {
            _lastSignature = null;
            SetStatus(Strings.Format("Error", ex.Message), StatusKind.Error);
            Log.Write(ex.ToString());
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<Frame> CaptureAsync(int x, int y, int w, int h)
    {
        if (_overlay.ExcludedFromCapture) return ScreenCapture.Capture(x, y, w, h);
        // Older Windows: hide our overlay for a moment so it doesn't end up in the screenshot.
        _overlay.Opacity = 0;
        await Task.Delay(60);
        var frame = ScreenCapture.Capture(x, y, w, h);
        _overlay.Opacity = 1;
        return frame;
    }

    /// <summary>Returns the OCR recognizers to use, downloading the selected language's model if needed.</summary>
    private async Task<List<string>?> EnsureModelsAsync()
    {
        if (_settings.SourceLanguage == Languages.Auto)
        {
            return ModelStore.AutoGroups();
        }
        var lang = Languages.Find(_settings.SourceLanguage)!;
        if (!ModelStore.IsInstalled(lang.OcrGroup))
        {
            var progress = new Progress<double>(p =>
                SetStatus(Strings.Format("Downloading", lang.NativeName, (int)(p * 100)), StatusKind.Busy));
            try
            {
                SetStatus(Strings.Format("Downloading", lang.NativeName, 0), StatusKind.Busy);
                await ModelStore.DownloadAsync(lang.OcrGroup, progress, _closing.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStatus(Strings.Format("DownloadFailed", lang.NativeName, ex.Message), StatusKind.Error);
                return null;
            }
        }
        return [lang.OcrGroup];
    }

    private async Task<List<Translation.Translation>> TranslateAsync(List<string> texts)
    {
        var engine = TranslatorRegistry.Get(_settings.Engine);
        string? from = _settings.SourceLanguage == Languages.Auto ? null : _settings.SourceLanguage;
        string to = _settings.TargetLanguage;
        string Key(string t) => $"{engine.Id}|{from}|{to}|{t}";

        var missing = texts.Distinct().Where(t => !_cache.ContainsKey(Key(t))).ToList();
        if (missing.Count > 0)
        {
            var result = await engine.TranslateAsync(missing, from, to, _closing.Token);
            for (int i = 0; i < missing.Count; i++) _cache[Key(missing[i])] = result[i];
            UpdateDetected(result.Select(r => r.DetectedLanguage).GroupBy(c => c).MaxBy(g => g.Count())?.Key);
            if (_cache.Count > 5000) _cache.Clear();
        }
        return texts.Select(t => _cache[Key(t)]).ToList();
    }
}
