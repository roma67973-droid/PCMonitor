using System.Globalization;
using System.Windows.Data;

namespace PCMonitor.Converters;

/// <summary>
/// Для групп радиокнопок: кнопка отмечена, если значение свойства равно <c>ConverterParameter</c>;
/// при отметке кнопки свойству присваивается параметр (enum, число или bool).
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(
            System.Convert.ToString(value, CultureInfo.InvariantCulture),
            System.Convert.ToString(parameter, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        var text = System.Convert.ToString(parameter, CultureInfo.InvariantCulture)!;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type.IsEnum
            ? Enum.Parse(type, text, ignoreCase: true)
            : System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }
}
