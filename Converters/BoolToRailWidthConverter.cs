using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GanttSquared.Converters;

/// <summary>Left icon rail's column width: wide enough for icon+label when expanded, icon-only when not.</summary>
public sealed class BoolToRailWidthConverter : IValueConverter
{
    public const double CondensedWidth = 48;
    public const double ExpandedWidth = 190;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(value is true ? ExpandedWidth : CondensedWidth);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
