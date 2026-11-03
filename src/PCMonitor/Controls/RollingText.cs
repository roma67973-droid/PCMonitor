using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace PCMonitor.Controls;

/// <summary>
/// Текст с числом, которое плавно перетекает к новому значению («63°C» → «67°C» через 64, 65, 66),
/// вместо <c>Text</c>: <c>c:RollingText.Text="{Binding CpuTempText}"</c>. Анимируется только первое
/// число строки и только если остальной текст не поменялся; иначе текст ставится сразу.
/// Идея — animated-number / sliding-number из motion-primitives (MIT).
/// </summary>
public static partial class RollingText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(RollingText), new PropertyMetadata(null, OnTextChanged));

    private static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(RollingText), new PropertyMetadata(0.0, OnValueChanged));

    private static readonly DependencyProperty TemplateProperty = DependencyProperty.RegisterAttached(
        "Template", typeof(NumberTemplate), typeof(RollingText), new PropertyMetadata(null));

    private static readonly Duration RollDuration = TimeSpan.FromMilliseconds(320);

    public static string? GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }

    /// <summary>Разбирает «префикс, число, суффикс». <c>null</c> — числа в строке нет.</summary>
    internal static NumberTemplate? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var m = NumberPattern().Match(text);
        if (!m.Success)
        {
            return null;
        }

        var digits = m.Groups["num"].Value;
        var separator = digits.Contains(',', StringComparison.Ordinal) ? "," : ".";
        var decimals = digits.Contains(separator, StringComparison.Ordinal) ? digits.Length - digits.IndexOf(separator, StringComparison.Ordinal) - 1 : 0;
        var value = double.Parse(digits.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        return new NumberTemplate(m.Groups["pre"].Value, value, decimals, separator, m.Groups["post"].Value);
    }

    internal static string Format(NumberTemplate template, double value)
    {
        // + 0.0 превращает −0 в 0: при переходе через ноль не должно мелькать «-0°C».
        var number = (Math.Round(value, template.Decimals) + 0.0).ToString("F" + template.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return template.Prefix + number.Replace(".", template.Separator, StringComparison.Ordinal) + template.Suffix;
    }

    [GeneratedRegex(@"^(?<pre>[^\d-]*)(?<num>-?\d+(?:[.,]\d+)?)(?<post>.*)$", RegexOptions.Singleline)]
    private static partial Regex NumberPattern();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        var text = (string?)e.NewValue;
        var next = Parse(text);
        var previous = (NumberTemplate?)block.GetValue(TemplateProperty);
        var canRoll = next is not null && previous is not null && next.SameShape(previous)
            && block.IsLoaded && block.IsVisible && Motion.GetIsEnabled(block);

        block.SetValue(TemplateProperty, next);
        if (!canRoll)
        {
            block.BeginAnimation(ValueProperty, null);
            if (next is not null)
            {
                block.SetValue(ValueProperty, next.Value);
            }

            block.Text = text ?? string.Empty;
            return;
        }

        var from = (double)block.GetValue(ValueProperty);
        var roll = new DoubleAnimation(from, next!.Value, RollDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        // Цифрам хватает 30 кадров/с, а перерисовок текста вдвое меньше.
        Timeline.SetDesiredFrameRate(roll, 30);
        block.BeginAnimation(ValueProperty, roll);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock block && block.GetValue(TemplateProperty) is NumberTemplate template)
        {
            block.Text = Format(template, (double)e.NewValue);
        }
    }

    internal sealed record NumberTemplate(string Prefix, double Value, int Decimals, string Separator, string Suffix)
    {
        /// <summary>Меняется только число: те же подписи, единицы и точность.</summary>
        public bool SameShape(NumberTemplate other) =>
            Prefix == other.Prefix && Suffix == other.Suffix && Decimals == other.Decimals && Separator == other.Separator;
    }
}
