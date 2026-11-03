using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCMonitor.Converters;

/// <summary>Visible, если значение равно <c>ConverterParameter</c> (например, панель для одного режима кулера).</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
