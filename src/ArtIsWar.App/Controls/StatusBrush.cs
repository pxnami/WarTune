using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ArtIsWar.App.Controls;

public sealed class StatusBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString() ?? "";
        string color = text.Contains("observed ·", StringComparison.Ordinal) ? "#69C494" :
            text.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ? "#EE737A" :
            text == "Bypassed" ? "#959DAA" : "#DFA76E";
        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
