using System.Windows;
using System.Windows.Media;
using ArtIsWar.Core;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace ArtIsWar.App.Controls;

public sealed class ResponseGraph : FrameworkElement
{
    public static readonly DependencyProperty FiltersProperty = DependencyProperty.Register(nameof(Filters), typeof(IEnumerable<EqFilter>), typeof(ResponseGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<EqFilter>? Filters { get => (IEnumerable<EqFilter>?)GetValue(FiltersProperty); set => SetValue(FiltersProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 80 || ActualHeight < 45) return;
        double width = ActualWidth - 60, height = ActualHeight - 30;
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(42, 47, 56)), 1);
        foreach (double db in new[] { -18.0, -12, -6, 0, 6, 12 })
        {
            double y = (12 - db) / 30 * height;
            dc.DrawLine(db == 0 ? new Pen(new SolidColorBrush(Color.FromRgb(70, 76, 87)), 1) : grid, new(36, y), new(width + 36, y)); Label(dc, $"{db:g}", new(0, y - 7));
        }
        foreach (double frequency in new[] { 20.0, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            double x = 36 + Math.Log10(frequency / 20) / 3 * width;
            dc.DrawLine(grid, new(x, 0), new(x, height)); Label(dc, frequency >= 1000 ? $"{frequency / 1000:g}k" : $"{frequency:g}", new(x - 12, height + 7));
        }
        if (Filters == null) return;
        var filters = Filters.ToArray(); var geometry = new StreamGeometry(); var area = new StreamGeometry();
        var points = new System.Windows.Point[251];
        for (int i = 0; i <= 250; i++)
        {
            double frequency = 20 * Math.Pow(1000, i / 250.0);
            double db = Math.Clamp(EqResponse.Decibels(filters, frequency, 48000), -18, 12);
            points[i] = new(36 + i / 250.0 * width, (12 - db) / 30 * height);
        }
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        using (var context = area.Open())
        {
            context.BeginFigure(new(36, height * .4), true, true);
            context.PolyLineTo(points, true, false);
            context.LineTo(new(width + 36, height * .4), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(14, 148, 40, 46)), null, area);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(184, 67, 72)), 1.5), geometry);
    }
    private void Label(DrawingContext dc, string text, System.Windows.Point point) => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(149,157,170)), VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
}
