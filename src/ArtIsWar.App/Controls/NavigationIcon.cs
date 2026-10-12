using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ArtIsWar.App.Controls;

public sealed class NavigationIcon : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, Geometry> Icons = new Dictionary<string, string> {
        ["Home"] = "M2,10 L12,2 22,10 M5,9 L5,22 10,22 10,15 14,15 14,22 19,22 19,9",
        ["Warzone"] = "M12,0 L12,7 M12,17 L12,24 M0,12 L7,12 M17,12 L24,12 M6,6 L18,6 18,18 6,18 Z",
        ["Profiles"] = "M3,5 L10,5 12,8 21,8 21,21 3,21 Z M6,2 L17,2 20,5",
        ["Headphones"] = "M3,15 L3,11 C3,0 21,0 21,11 L21,15 M3,12 L7,12 7,21 3,21 Z M17,12 L21,12 21,21 17,21 Z",
        ["Equalizer"] = "M4,2 L4,8 M4,13 L4,22 M1,8 L7,8 7,13 1,13 Z M12,2 L12,14 M12,19 L12,22 M9,14 L15,14 15,19 9,19 Z M20,2 L20,5 M20,10 L20,22 M17,5 L23,5 23,10 17,10 Z",
        ["Audio Setup"] = "M3,3 L21,3 21,21 3,21 Z M7,8 L10,11 17,5 M7,16 L17,16",
        ["Diagnostics"] = "M0,13 L5,13 8,4 12,21 16,9 19,13 24,13",
        ["Settings"] = "M3,5 L21,5 M3,12 L21,12 M3,19 L21,19 M7,2 L7,8 M16,9 L16,15 M10,16 L10,22"
    }.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Icons.TryGetValue(value.ToString() ?? "", out var icon) ? icon : Geometry.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
