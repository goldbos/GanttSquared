using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GanttSquared.Converters;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (FormatException)
            {
                // Fall through to the default brush below.
            }
        }

        return new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
