using System.Windows;

namespace PCMonitor.Controls;

/// <summary>
/// Наследуемый флаг «анимации включены». Ставится один раз на корне окна (привязкой к настройке),
/// контролы читают его у себя — без обращения к глобальному состоянию.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(Motion),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            FanSpin.Refresh(element);
        }
    }
}
