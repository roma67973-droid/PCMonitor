using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCMonitor.Core;

namespace PCMonitor.Controls;

/// <summary>
/// Редактор кривой «температура → мощность кулера». Точки перетаскиваются мышью и меняются на месте;
/// по отпусканию мыши вызывается <see cref="EditedCommand"/>.
/// </summary>
public sealed class CurveEditor : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(List<CurvePoint>), typeof(CurveEditor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentTemperatureProperty = DependencyProperty.Register(
        nameof(CurrentTemperature), typeof(double), typeof(CurveEditor), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EditedCommandProperty = DependencyProperty.Register(
        nameof(EditedCommand), typeof(ICommand), typeof(CurveEditor));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(CurveEditor), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(CurveEditor), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(CurveEditor), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MinTemperature = 20;
    private const double MaxTemperature = 100;

    /// <summary>Минимальный зазор между соседними точками по температуре, чтобы кривая оставалась функцией.</summary>
    private const double MinPointGap = 2;

    private const double HitRadius = 12;
    private const double PadLeft = 40;
    private const double PadRight = 14;
    private const double PadTop = 12;
    private const double PadBottom = 26;

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private int _dragIndex = -1;
    private int _hoverIndex = -1;

    public CurveEditor() => Focusable = false;

    public List<CurvePoint>? Points { get => (List<CurvePoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    /// <summary>Текущая температура датчика — отмечается на графике вместе с итоговой мощностью.</summary>
    public double CurrentTemperature { get => (double)GetValue(CurrentTemperatureProperty); set => SetValue(CurrentTemperatureProperty, value); }

    public ICommand? EditedCommand { get => (ICommand?)GetValue(EditedCommandProperty); set => SetValue(EditedCommandProperty, value); }

    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    private Rect PlotArea => new(PadLeft, PadTop, Math.Max(1, ActualWidth - PadLeft - PadRight), Math.Max(1, ActualHeight - PadTop - PadBottom));

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        // Прозрачная подложка нужна, чтобы элемент ловил мышь по всей площади.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        DrawGrid(drawingContext);

        var points = Points;
        if (points is not { Count: > 0 })
        {
            return;
        }

        var accent = Accent switch
        {
            SolidColorBrush s => s.Color,
            GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
            _ => Colors.DeepSkyBlue,
        };

        DrawCurve(drawingContext, points, accent);
        DrawCurrentTemperature(drawingContext, points, accent);
        DrawPoints(drawingContext, points);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _dragIndex = HitTestPoint(e.GetPosition(this));
        if (_dragIndex >= 0)
        {
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var position = e.GetPosition(this);
        if (_dragIndex >= 0 && Points is { } points)
        {
            var (temperature, percent) = FromScreen(position);
            var low = _dragIndex > 0 ? points[_dragIndex - 1].T + MinPointGap : MinTemperature;
            var high = _dragIndex < points.Count - 1 ? points[_dragIndex + 1].T - MinPointGap : MaxTemperature;
            points[_dragIndex].T = Math.Round(Math.Clamp(temperature, low, high));
            points[_dragIndex].P = Math.Round(Math.Clamp(percent, 0, 100));
            InvalidateVisual();
            return;
        }

        var hover = HitTestPoint(position);
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Cursor = hover >= 0 ? Cursors.Hand : null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragIndex < 0)
        {
            return;
        }

        _dragIndex = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
        if (EditedCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverIndex >= 0 && _dragIndex < 0)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    private void DrawGrid(DrawingContext dc)
    {
        var plot = PlotArea;
        var pen = new Pen(GridBrush, 1);
        pen.Freeze();

        for (var percent = 0; percent <= 100; percent += 25)
        {
            var y = Math.Round(plot.Bottom - (percent / 100.0 * plot.Height)) + 0.5;
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Label($"{percent}%", 10.5);
            dc.DrawText(label, new Point(plot.Left - label.Width - 8, y - (label.Height / 2)));
        }

        for (var t = (int)MinTemperature; t <= MaxTemperature; t += 10)
        {
            var x = Math.Round(ToScreen(t, 0).X) + 0.5;
            dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Label($"{t}°", 10.5);
            dc.DrawText(label, new Point(x - (label.Width / 2), plot.Bottom + 6));
        }
    }

    private void DrawCurve(DrawingContext dc, List<CurvePoint> points, Color accent)
    {
        var plot = PlotArea;
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var areaContext = area.Open())
        {
            var first = ToScreen(MinTemperature, points[0].P);
            lineContext.BeginFigure(first, isFilled: false, isClosed: false);
            areaContext.BeginFigure(new Point(plot.Left, plot.Bottom), isFilled: true, isClosed: true);
            areaContext.LineTo(first, isStroked: false, isSmoothJoin: false);

            foreach (var screen in points.Select(p => ToScreen(p.T, p.P)).Append(ToScreen(MaxTemperature, points[^1].P)))
            {
                lineContext.LineTo(screen, isStroked: true, isSmoothJoin: true);
                areaContext.LineTo(screen, isStroked: false, isSmoothJoin: false);
            }

            areaContext.LineTo(new Point(plot.Right, plot.Bottom), isStroked: false, isSmoothJoin: false);
        }

        line.Freeze();
        area.Freeze();

        var fill = new LinearGradientBrush(Color.FromArgb(90, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(Accent, 2.5) { LineJoin = PenLineJoin.Round }, line);
    }

    private void DrawCurrentTemperature(DrawingContext dc, List<CurvePoint> points, Color accent)
    {
        if (double.IsNaN(CurrentTemperature))
        {
            return;
        }

        var plot = PlotArea;
        var percent = FanCurve.Evaluate(points, CurrentTemperature);
        var at = ToScreen(Math.Clamp(CurrentTemperature, MinTemperature, MaxTemperature), percent);

        var dash = new Pen(TextBrush, 1) { DashStyle = new DashStyle([3, 3], 0) };
        dash.Freeze();
        dc.DrawLine(dash, new Point(at.X, plot.Top), new Point(at.X, plot.Bottom));

        var halo = new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B));
        halo.Freeze();
        dc.DrawEllipse(halo, null, at, 10, 10);
        dc.DrawEllipse(Brushes.White, new Pen(Accent, 2), at, 4, 4);

        var label = Label($"{CurrentTemperature:0}° → {percent:0}%", 11);
        dc.DrawText(label, new Point(Math.Min(at.X + 8, plot.Right - label.Width), plot.Top));
    }

    private void DrawPoints(DrawingContext dc, List<CurvePoint> points)
    {
        var plot = PlotArea;
        var outline = new Pen(Brushes.White, 2);
        outline.Freeze();

        for (var i = 0; i < points.Count; i++)
        {
            var screen = ToScreen(points[i].T, points[i].P);
            var active = i == _dragIndex || i == _hoverIndex;
            var radius = active ? 8 : 6.5;
            dc.DrawEllipse(Accent, outline, screen, radius, radius);
            if (!active)
            {
                continue;
            }

            var label = Label($"{points[i].T:0}° · {points[i].P:0}%", 11.5);
            var x = Math.Clamp(screen.X - (label.Width / 2), plot.Left, plot.Right - label.Width);
            var y = screen.Y - 30 < plot.Top ? screen.Y + 14 : screen.Y - 30;
            dc.DrawRoundedRectangle(GridBrush, null, new Rect(x - 6, y - 2, label.Width + 12, label.Height + 4), 6, 6);
            dc.DrawText(label, new Point(x, y));
        }
    }

    private FormattedText Label(string text, double size) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Point ToScreen(double temperature, double percent)
    {
        var plot = PlotArea;
        return new Point(
            plot.Left + ((temperature - MinTemperature) / (MaxTemperature - MinTemperature) * plot.Width),
            plot.Bottom - (percent / 100 * plot.Height));
    }

    private (double Temperature, double Percent) FromScreen(Point screen)
    {
        var plot = PlotArea;
        return (
            MinTemperature + ((screen.X - plot.Left) / plot.Width * (MaxTemperature - MinTemperature)),
            (plot.Bottom - screen.Y) / plot.Height * 100);
    }

    private int HitTestPoint(Point position)
    {
        if (Points is not { } points)
        {
            return -1;
        }

        return points.FindIndex(p => (ToScreen(p.T, p.P) - position).Length < HitRadius);
    }
}
