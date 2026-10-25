using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassTranslate;

/// <summary>Onboarding tour: dims the window, outlines one control at a time and explains it in a card.</summary>
public partial class MainWindow
{
    private const double CardWidth = 290;
    private int _tourStep = -1;

    /// <summary>What each step highlights, in the order of <see cref="TourStrings.Texts.Steps"/>.</summary>
    private FrameworkElement[][] TourTargets() =>
    [
        [Hole], [LogoBadge], [SourceBox], [TargetBox], [EngineBox], [IntervalBox], [TranslateButton],
        [OriginalToggle], [PinToggle], [SettingsButton], [HelpButton, TourButton], [ResizeGrip], [StatusInfo],
    ];

    private void OnTour(object sender, RoutedEventArgs e) => StartTour();


    internal void StartTour(int step = 0)
    {
        SettingsPopup.IsOpen = false;
        HelpPopup.IsOpen = false;
        DonatePopup.IsOpen = false;
        _overlay.Visibility = Visibility.Hidden;
        TourLayer.Visibility = Visibility.Visible;
        _tourStep = step;
        ShowTourStep();
        TourLayer.Focus();
    }

    private void EndTour()
    {
        _tourStep = -1;
        TourLayer.Visibility = Visibility.Collapsed;
        _overlay.Visibility = OriginalToggle.IsChecked == true ? Visibility.Hidden : Visibility.Visible;
        if (!_settings.TourShown)
        {
            _settings.TourShown = true;
            _settings.Save();
        }
    }

    private void ShowTourStep()
    {
        if (_tourStep < 0) return;
        var texts = TourStrings.Current;
        var steps = texts.Steps;
        TourStep.Text = $"{_tourStep + 1} / {steps.Length}";
        TourTitle.Text = steps[_tourStep].Title;
        TourText.Text = steps[_tourStep].Text;
        TourBack.Content = texts.Back;
        TourBack.Visibility = _tourStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool last = _tourStep == steps.Length - 1;
        TourNext.Content = last ? texts.Done : texts.Next;
        TourSkip.Content = texts.Skip;
        TourSkip.Visibility = last ? Visibility.Collapsed : Visibility.Visible;

        UpdateLayout();
        double w = Root.ActualWidth, h = Root.ActualHeight;
        var hl = Rect.Empty;
        foreach (var t in TourTargets()[_tourStep])
            hl.Union(t.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight)));
        if (TourTargets()[_tourStep][0] == Hole) hl.Inflate(-10, -10);
        else hl.Inflate(4, 4);
        hl.Intersect(new Rect(1, 1, Math.Max(0, w - 2), Math.Max(0, h - 2)));

        TourDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, w, h), 10, 10), new RectangleGeometry(hl, 8, 8));
        TourHighlight.Margin = new Thickness(hl.X, hl.Y, 0, 0);
        TourHighlight.Width = hl.Width;
        TourHighlight.Height = hl.Height;

        // Card goes below the highlight, else above it, else inside it (the big translation area).
        double cw = Math.Min(CardWidth, w - 16);
        TourCard.Width = cw;
        TourCard.Measure(new Size(cw, double.PositiveInfinity));
        double ch = TourCard.DesiredSize.Height;
        double y = hl.Bottom + 10 + ch <= h - 6 ? hl.Bottom + 10
            : hl.Top - 10 - ch >= 6 ? hl.Top - 10 - ch
            : hl.Top + (hl.Height - ch) / 2;
        double x = hl.Left + cw <= w - 8 ? hl.Left : hl.Right - cw;
        if (TourTargets()[_tourStep][0] == Hole) x = hl.Left + (hl.Width - cw) / 2;
        TourCard.Margin = new Thickness(Math.Clamp(x, 8, Math.Max(8, w - cw - 8)), Math.Clamp(y, 6, Math.Max(6, h - ch - 6)), 0, 0);
    }

    private void OnTourNext(object sender, RoutedEventArgs e)
    {
        if (_tourStep >= TourStrings.Current.Steps.Length - 1) { EndTour(); return; }
        _tourStep++;
        ShowTourStep();
    }

    private void OnTourBack(object sender, RoutedEventArgs e)
    {
        if (_tourStep <= 0) return;
        _tourStep--;
        ShowTourStep();
    }

    private void OnTourSkip(object sender, RoutedEventArgs e) => EndTour();

    private void OnTourKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: EndTour(); break;
            case Key.Right or Key.Enter or Key.Space: OnTourNext(sender, e); break;
            case Key.Left: OnTourBack(sender, e); break;
            default: return;
        }
        e.Handled = true;
    }
}
