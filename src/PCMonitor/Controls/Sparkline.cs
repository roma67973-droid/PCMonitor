using System.Windows;
using System.Windows.Media;

namespace PCMonitor.Controls;

/// <summary>Мини-график последних значений с градиентной заливкой под линией.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(
        nameof(FillOpacity), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(0.35, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Запас сверху при автомасштабе, чтобы пик не упирался в край.</summary>
    private const double AutoScaleHeadroom = 1.15;

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    /// <summary>Верх шкалы; 0 — автомасштаб по максимуму данных.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public double FillOpacity { get => (double)GetValue(FillOpacityProperty); set => SetValue(FillOpacityProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        double width = ActualWidth, height = ActualHeight;
        var values = Values;
        if (values is not { Count: >= 2 } || width <= 0 || height <= 0)
        {
            return;
        }

        var max = Maximum > 0 ? Maximum : Math.Max(1, values.Max()) * AutoScaleHeadroom;
        var step = width / (values.Count - 1);
        double Y(double v) => height - 2 - (Math.Clamp(v / max, 0, 1) * (height - 4));

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var areaContext = area.Open())
        {
            lineContext.BeginFigure(new Point(0, Y(values[0])), isFilled: false, isClosed: false);
            areaContext.BeginFigure(new Point(0, height), isFilled: true, isClosed: true);
            for (var i = 0; i < values.Count; i++)
            {
                var point = new Point(i * step, Y(values[i]));
                if (i > 0)
                {
                    lineContext.LineTo(point, isStroked: true, isSmoothJoin: true);
                }

                areaContext.LineTo(point, isStroked: false, isSmoothJoin: false);
            }

            areaContext.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
        }

        line.Freeze();
        area.Freeze();

        var color = Stroke switch
        {
            SolidColorBrush s => s.Color,
            GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
            _ => Colors.DeepSkyBlue,
        };
        var fill = new LinearGradientBrush(Color.FromArgb((byte)(255 * FillOpacity), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        drawingContext.DrawGeometry(fill, null, area);
        drawingContext.DrawGeometry(null, pen, line);
    }
}
