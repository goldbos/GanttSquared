using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GanttSquared.ViewModels;

namespace GanttSquared.Converters;

/// <summary>
/// Collapses a column to 0 width outside the Gantt tab, full width (ConverterParameter, a
/// pixel value) on it - used for the Properties panel column and its splitter, which are only
/// ever shown on Gantt. A hidden Border still reserves its column's width in a Grid, so this is
/// what actually gives that space back to the canvas/dashboard on the other tabs, rather than
/// just leaving it blank.
/// </summary>
public sealed class ActiveTabToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MainTab tab || tab != MainTab.Gantt)
            return new GridLength(0);

        var width = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0;
        return new GridLength(width);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
