using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace ArtIsWar.App.Controls;

public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    private static readonly Brush Inactive = Frozen(43, 48, 56), Active = Frozen(178, 115, 100), Warning = Frozen(217, 162, 105), Peak = Frozen(215, 78, 85);
    private static Brush Frozen(byte red, byte green, byte blue) { var brush = new SolidColorBrush(Color.FromRgb(red, green, blue)); brush.Freeze(); return brush; }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        const int segments = 36;
        double gap = 3, width = Math.Max(1, (ActualWidth - gap * (segments - 1)) / segments);
        double level = Math.Clamp(Value / 100, 0, 1);
        for (int i = 0; i < segments; i++)
        {
            var brush = level > i / (double)segments ? i >= 32 ? Peak : i >= 27 ? Warning : Active : Inactive;
            context.DrawRoundedRectangle(brush, null, new Rect(i * (width + gap), 0, width, ActualHeight), 1, 1);
        }
    }
}
