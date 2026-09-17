using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GanttSquared.Converters;

/// <summary>
/// Visibility.Visible when the bound enum value's name matches ConverterParameter (a string,
/// e.g. "Gantt"), Collapsed otherwise - lets XAML switch on a single ActiveTab-style enum
/// property without a bool flag per tab.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Inverse of EnumEqualsConverter: Visible unless the bound enum value's name matches
/// ConverterParameter - used for chrome shared by every tab except one (e.g. the task/resource
/// list column and its splitter, which both Gantt and Resources use but Dashboard doesn't).
/// </summary>
public sealed class EnumNotEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
