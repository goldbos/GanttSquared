using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GanttSquared.Converters;

/// <summary>Left icon rail's column width: wide enough for icon+label when expanded, icon-only when not.</summary>
public sealed class BoolToRailWidthConverter : IValueConverter
{
    public const double CondensedWidth = 48;

    // Sized to fit the longest label ("New Project"/"Open Project") plus icon and padding with
    // a little breathing room, not a round number - 190 left a wide dead strip to the right of
    // every shorter label (most of them, e.g. "Save", "Undo", "Density").
    public const double ExpandedWidth = 150;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(value is true ? ExpandedWidth : CondensedWidth);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
