using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCMonitor.Controls;

/// <summary>Кольцевой индикатор (дуга 270°) со скруглёнными концами. Рисуется напрямую, без вложенных элементов.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Отображаемое значение: догоняет <see cref="Value"/> анимацией.</summary>
    private static readonly DependencyProperty DisplayedValueProperty = DependencyProperty.Register(
        "DisplayedValue", typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    /// <summary>Мелкие колебания не анимируем — это лишние перерисовки без визуальной пользы.</summary>
    private const double AnimationThreshold = 3;
    private static readonly Duration AnimationDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>Значение 0–100.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var radius = (size - Thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        drawingContext.DrawGeometry(null, RoundPen(TrackBrush), Arc(center, radius, StartAngle, SweepAngle));

        var value = Math.Clamp((double)GetValue(DisplayedValueProperty), 0, 100);
        if (value < 0.3)
        {
            return;
        }

        var sweep = SweepAngle * value / 100;
        drawingContext.DrawGeometry(null, RoundPen(Brush), Arc(center, radius, StartAngle, sweep));
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RingGauge)d;
        var target = (double)e.NewValue;
        var current = (double)gauge.GetValue(DisplayedValueProperty);

        if (!gauge.IsLoaded || !Motion.GetIsEnabled(gauge) || Math.Abs(target - current) < AnimationThreshold)
        {
            gauge.BeginAnimation(DisplayedValueProperty, null);
            gauge.SetValue(DisplayedValueProperty, target);
            return;
        }

        var animation = new DoubleAnimation(target, AnimationDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Timeline.SetDesiredFrameRate(animation, 24);
        gauge.BeginAnimation(DisplayedValueProperty, animation);
    }

    private Pen RoundPen(Brush brush) => new(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    private static StreamGeometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOnCircle(center, radius, startDegrees), isFilled: false, isClosed: false);
            context.ArcTo(
                PointOnCircle(center, radius, startDegrees + Math.Max(sweepDegrees, 0.01)),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: sweepDegrees > 180,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }
}
