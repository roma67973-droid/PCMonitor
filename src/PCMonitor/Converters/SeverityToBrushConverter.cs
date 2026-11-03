using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PCMonitor.Core;

namespace PCMonitor.Converters;

/// <summary>Уровень тревожности → кисть текущей темы (Good / Warn / Hot).</summary>
[ValueConversion(typeof(Severity), typeof(Brush))]
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Severity.Critical => "HotBrush",
            Severity.Elevated => "WarnBrush",
            _ => "GoodBrush",
        };
        return Application.Current.TryFindResource(key) ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
