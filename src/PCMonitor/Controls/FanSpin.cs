using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCMonitor.Controls;

/// <summary>
/// Вращение иконки кулера со скоростью, зависящей от оборотов.
/// Анимация перезапускается только при заметном изменении оборотов и не тикает на скрытых элементах.
/// </summary>
public static class FanSpin
{
    public static readonly DependencyProperty RpmProperty = DependencyProperty.RegisterAttached(
        "Rpm", typeof(double), typeof(FanSpin), new PropertyMetadata(0.0, (d, _) => Refresh(d as FrameworkElement)));

    /// <summary>Текущий период оборота (с) — чтобы не перезапускать анимацию из-за мелких колебаний.</summary>
    private static readonly DependencyProperty PeriodProperty = DependencyProperty.RegisterAttached(
        "Period", typeof(double), typeof(FanSpin), new PropertyMetadata(0.0));

    private const double MinRpm = 50;
    private const double VisualRpmFactor = 1800; // Реальные 1000+ об/мин глазом не различить — показываем условную скорость.
    private const double MinPeriodSeconds = 0.35;
    private const double MaxPeriodSeconds = 4;
    private const double RestartTolerance = 0.15;
    private const int FrameRate = 24;

    public static double GetRpm(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(RpmProperty);
    }

    public static void SetRpm(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RpmProperty, value);
    }

    internal static void Refresh(FrameworkElement? element)
    {
        if (element is null || element.ReadLocalValue(RpmProperty) == DependencyProperty.UnsetValue)
        {
            return;
        }

        var rotation = EnsureRotation(element);
        var rpm = GetRpm(element);
        var spinning = rpm > MinRpm && element.IsVisible && Motion.GetIsEnabled(element);
        var period = spinning ? Math.Clamp(VisualRpmFactor / rpm, MinPeriodSeconds, MaxPeriodSeconds) : 0;

        var current = (double)element.GetValue(PeriodProperty);
        if (spinning && current > 0 && Math.Abs(period - current) / current < RestartTolerance)
        {
            return;
        }

        element.SetValue(PeriodProperty, period);
        if (!spinning)
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            return;
        }

        var from = rotation.Angle % 360;
        var animation = new DoubleAnimation(from, from + 360, TimeSpan.FromSeconds(period)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        rotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private static RotateTransform EnsureRotation(FrameworkElement element)
    {
        if (element.RenderTransform is RotateTransform existing)
        {
            return existing;
        }

        var rotation = new RotateTransform();
        element.RenderTransform = rotation;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.IsVisibleChanged += (_, _) =>
        {
            element.SetValue(PeriodProperty, 0.0);
            Refresh(element);
        };
        return rotation;
    }
}
