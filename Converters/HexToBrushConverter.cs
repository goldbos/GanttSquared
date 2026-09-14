using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GanttSquared.Converters;

/// <summary>
/// Converts a "#rrggbb" string to a solid brush. An optional numeric ConverterParameter (e.g.
/// "0.55") darkens the color by that factor first - used to render a Gantt bar's "remaining"
/// portion as a dimmer shade of its own color instead of the same color at reduced opacity,
/// so the bar itself stays fully solid (opaque) rather than letting the canvas grid show through.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Color DefaultColor = Color.FromRgb(0x3B, 0x82, 0xF6);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = DefaultColor;
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                // Keep the default color.
            }
        }

        if (parameter is string factorText && double.TryParse(factorText, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
        {
            color = Color.FromRgb(
                (byte)Math.Clamp(color.R * factor, 0, 255),
                (byte)Math.Clamp(color.G * factor, 0, 255),
                (byte)Math.Clamp(color.B * factor, 0, 255));
        }

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
