using System.Globalization;
using System.Windows.Data;

namespace PCMonitor.Converters;

/// <summary>Процент (0–100) → длина в пикселях от <c>ConverterParameter</c> (полная длина). Минимум 2 px, чтобы полоска была видна.</summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class PercentToLengthConverter : IValueConverter
{
    private const double MinimumLength = 2;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percent = value is double d ? d : 0;
        var full = double.TryParse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)
            ? p
            : 0;
        return Math.Max(MinimumLength, Math.Clamp(percent, 0, 100) / 100 * full);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
