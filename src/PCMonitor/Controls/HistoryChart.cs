using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCMonitor.Core;

namespace PCMonitor.Controls;

/// <summary>
/// График истории CPU и GPU. Для температуры: средняя линия и коридор до пиков, зона перегрева.
/// Для нагрузки: линии с заливкой. При наведении — вертикальная линия с подсказкой.
/// Точки агрегируются по ширине графика: неделя (~10 тыс. минут) превращается в несколько сотен столбцов.
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = Register(nameof(Points), typeof(IReadOnlyList<HistoryPoint>), null);
    public static readonly DependencyProperty RangeMinutesProperty = Register(nameof(RangeMinutes), typeof(int), 60);
    public static readonly DependencyProperty ShowLoadProperty = Register(nameof(ShowLoad), typeof(bool), false);
    public static readonly DependencyProperty OverheatThresholdProperty = Register(nameof(OverheatThreshold), typeof(double), 85.0);
    public static readonly DependencyProperty CpuBrushProperty = Register(nameof(CpuBrush), typeof(Brush), Brushes.DeepSkyBlue);
    public static readonly DependencyProperty GpuBrushProperty = Register(nameof(GpuBrush), typeof(Brush), Brushes.MediumPurple);
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty TextBrushProperty = Register(nameof(TextBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty HotBrushProperty = Register(nameof(HotBrush), typeof(Brush), Brushes.Red);
    public static readonly DependencyProperty TooltipBrushProperty = Register(nameof(TooltipBrush), typeof(Brush), Brushes.Black);

    private const double PadLeft = 44;
    private const double PadRight = 12;
    private const double PadTop = 12;
    private const double PadBottom = 26;
    private const double PixelsPerBucket = 3;
    private const double HoverSnapDistance = 30;

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private List<Bucket> _buckets = [];
    private double _mouseX = double.NaN;

    public HistoryChart() => Cursor = Cursors.Cross;

    public IReadOnlyList<HistoryPoint>? Points { get => (IReadOnlyList<HistoryPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    public int RangeMinutes { get => (int)GetValue(RangeMinutesProperty); set => SetValue(RangeMinutesProperty, value); }

    public bool ShowLoad { get => (bool)GetValue(ShowLoadProperty); set => SetValue(ShowLoadProperty, value); }

    public double OverheatThreshold { get => (double)GetValue(OverheatThresholdProperty); set => SetValue(OverheatThresholdProperty, value); }

    public Brush CpuBrush { get => (Brush)GetValue(CpuBrushProperty); set => SetValue(CpuBrushProperty, value); }

    public Brush GpuBrush { get => (Brush)GetValue(GpuBrushProperty); set => SetValue(GpuBrushProperty, value); }

    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public Brush HotBrush { get => (Brush)GetValue(HotBrushProperty); set => SetValue(HotBrushProperty, value); }

    public Brush TooltipBrush { get => (Brush)GetValue(TooltipBrushProperty); set => SetValue(TooltipBrushProperty, value); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _mouseX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _mouseX = double.NaN;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth < 120 || ActualHeight < 80)
        {
            return;
        }

        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var scale = new Scale(this, nowMinute - RangeMinutes);
        _buckets = Aggregate(Points ?? [], scale.FromMinute, scale.Width);
        scale.FitValues(_buckets, ShowLoad, OverheatThreshold);

        if (!ShowLoad)
        {
            DrawOverheatZone(drawingContext, scale);
        }

        DrawValueGrid(drawingContext, scale);
        DrawTimeGrid(drawingContext, scale, nowMinute);

        if (_buckets.Count == 0)
        {
            var empty = Label("История копится, пока программа работает — загляните через пару минут", TextBrush, 13);
            drawingContext.DrawText(empty, new Point(scale.Left + ((scale.Width - empty.Width) / 2), scale.Top + ((scale.Height - empty.Height) / 2)));
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(new Rect(scale.Left, scale.Top - 2, scale.Width, scale.Height + 4)));
        if (ShowLoad)
        {
            DrawSeries(drawingContext, scale, b => b.CpuLoad, null, CpuBrush);
            DrawSeries(drawingContext, scale, b => b.GpuLoad, null, GpuBrush);
        }
        else
        {
            DrawSeries(drawingContext, scale, b => b.GpuTemp, b => b.GpuTempMax, GpuBrush);
            DrawSeries(drawingContext, scale, b => b.CpuTemp, b => b.CpuTempMax, CpuBrush);
        }

        drawingContext.Pop();

        DrawHover(drawingContext, scale);
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) => DependencyProperty.Register(
        name, type, typeof(HistoryChart), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Color ColorOf(Brush brush, Color fallback) => brush is SolidColorBrush s ? s.Color : fallback;

    /// <summary>Сводит поминутные точки в столбцы по ~3 пикселя: среднее средних и максимум максимумов.</summary>
    private List<Bucket> Aggregate(IReadOnlyList<HistoryPoint> points, long fromMinute, double width)
    {
        var minutesPerBucket = Math.Max(1, (int)Math.Ceiling(RangeMinutes / Math.Max(60, width / PixelsPerBucket)));
        var buckets = new List<Bucket>();
        var i = 0;
        while (i < points.Count)
        {
            var key = (points[i].Minute - fromMinute) / minutesPerBucket;
            var acc = default(BucketAccumulator);
            while (i < points.Count && (points[i].Minute - fromMinute) / minutesPerBucket == key)
            {
                acc.Add(points[i++]);
            }

            buckets.Add(acc.Build());
        }

        return buckets;
    }

    private void DrawOverheatZone(DrawingContext dc, Scale scale)
    {
        if (scale.Max <= OverheatThreshold)
        {
            return;
        }

        var hot = ColorOf(HotBrush, Colors.Red);
        var y = scale.Y(OverheatThreshold);

        var zone = new SolidColorBrush(Color.FromArgb(0x16, hot.R, hot.G, hot.B));
        zone.Freeze();
        dc.DrawRectangle(zone, null, new Rect(scale.Left, scale.Top, scale.Width, y - scale.Top));

        var dash = new Pen(new SolidColorBrush(Color.FromArgb(0x90, hot.R, hot.G, hot.B)), 1) { DashStyle = new DashStyle([4, 4], 0) };
        dash.Freeze();
        dc.DrawLine(dash, new Point(scale.Left, y), new Point(scale.Left + scale.Width, y));

        var label = Label("перегрев", new SolidColorBrush(Color.FromArgb(0xC0, hot.R, hot.G, hot.B)), 10.5);
        dc.DrawText(label, new Point(scale.Left + scale.Width - label.Width - 4, y - label.Height - 2));
    }

    private void DrawValueGrid(DrawingContext dc, Scale scale)
    {
        var pen = new Pen(GridBrush, 1);
        pen.Freeze();
        var step = scale.Max - scale.Min > 60 ? 20 : 10;
        for (var v = scale.Min; v <= scale.Max + 0.01; v += step)
        {
            var y = scale.Y(v);
            dc.DrawLine(pen, new Point(scale.Left, y), new Point(scale.Left + scale.Width, y));
            var label = Label(ShowLoad ? $"{v:0}%" : $"{v:0}°", TextBrush);
            dc.DrawText(label, new Point(scale.Left - label.Width - 8, y - (label.Height / 2)));
        }
    }

    private void DrawTimeGrid(DrawingContext dc, Scale scale, long nowMinute)
    {
        var pen = new Pen(GridBrush, 1);
        pen.Freeze();

        var tick = RangeMinutes switch { <= 60 => 10, <= 360 => 60, <= 1440 => 180, _ => 1440 };
        // Деления по местному времени: «18:00», а не «15:00 UTC».
        var utcOffset = (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
        var first = (((scale.FromMinute + utcOffset) / tick) + 1) * tick - utcOffset;

        for (var minute = first; minute <= nowMinute; minute += tick)
        {
            var x = scale.X(minute);
            dc.DrawLine(pen, new Point(x, scale.Top), new Point(x, scale.Top + scale.Height));
            var time = DateTime.UnixEpoch.AddMinutes(minute).ToLocalTime();
            var label = Label(time.ToString(tick >= 1440 ? "dd.MM" : "HH:mm", CultureInfo.CurrentCulture), TextBrush);
            if (x - (label.Width / 2) > scale.Left - 4 && x + (label.Width / 2) < ActualWidth)
            {
                dc.DrawText(label, new Point(x - (label.Width / 2), scale.Top + scale.Height + 6));
            }
        }
    }

    private void DrawSeries(DrawingContext dc, Scale scale, Func<Bucket, double> average, Func<Bucket, double>? peak, Brush brush)
    {
        var color = ColorOf(brush, Colors.DeepSkyBlue);

        // Разрыв линии, если компьютер был выключен или программа не работала.
        var gapMinutes = Math.Max(3, RangeMinutes / scale.Width * 6);
        var segments = new List<List<Bucket>>();
        List<Bucket>? current = null;
        var previousMinute = double.NaN;
        foreach (var bucket in _buckets)
        {
            if (double.IsNaN(average(bucket)))
            {
                current = null;
                continue;
            }

            if (current is null || bucket.Minute - previousMinute > gapMinutes)
            {
                current = [];
                segments.Add(current);
            }

            current.Add(bucket);
            previousMinute = bucket.Minute;
        }

        // Одиночные минуты (например, сразу после запуска) — кружками: линией из одной точки их не видно.
        foreach (var single in segments.Where(s => s.Count == 1))
        {
            dc.DrawEllipse(brush, null, new Point(scale.X(single[0].Minute), scale.Y(average(single[0]))), 3.5, 3.5);
        }

        var line = new StreamGeometry();
        var band = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var bandContext = band.Open())
        {
            foreach (var segment in segments.Where(s => s.Count > 1))
            {
                Point At(Bucket b, Func<Bucket, double> value) => new(scale.X(b.Minute), scale.Y(value(b)));

                lineContext.BeginFigure(At(segment[0], average), isFilled: false, isClosed: false);
                foreach (var b in segment.Skip(1))
                {
                    lineContext.LineTo(At(b, average), isStroked: true, isSmoothJoin: true);
                }

                if (peak is not null)
                {
                    // Коридор: вперёд по пикам, назад по средним.
                    bandContext.BeginFigure(At(segment[0], peak), isFilled: true, isClosed: true);
                    foreach (var b in segment.Skip(1))
                    {
                        bandContext.LineTo(At(b, peak), isStroked: false, isSmoothJoin: false);
                    }

                    for (var i = segment.Count - 1; i >= 0; i--)
                    {
                        bandContext.LineTo(At(segment[i], average), isStroked: false, isSmoothJoin: false);
                    }
                }
                else
                {
                    var bottom = scale.Top + scale.Height;
                    bandContext.BeginFigure(new Point(scale.X(segment[0].Minute), bottom), isFilled: true, isClosed: true);
                    foreach (var b in segment)
                    {
                        bandContext.LineTo(At(b, average), isStroked: false, isSmoothJoin: false);
                    }

                    bandContext.LineTo(new Point(scale.X(segment[^1].Minute), bottom), isStroked: false, isSmoothJoin: false);
                }
            }
        }

        line.Freeze();
        band.Freeze();

        Brush fill = peak is not null
            ? new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B))
            : new LinearGradientBrush(Color.FromArgb(0x50, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        var pen = new Pen(brush, 2.2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, null, band);
        dc.DrawGeometry(null, pen, line);
    }

    private void DrawHover(DrawingContext dc, Scale scale)
    {
        if (double.IsNaN(_mouseX) || _mouseX < scale.Left || _mouseX > scale.Left + scale.Width || _buckets.Count == 0)
        {
            return;
        }

        var nearest = _buckets.MinBy(b => Math.Abs(scale.X(b.Minute) - _mouseX));
        var x = scale.X(nearest.Minute);
        if (Math.Abs(x - _mouseX) > HoverSnapDistance)
        {
            return;
        }

        var crosshair = new Pen(TextBrush, 1) { DashStyle = DashStyles.Dash };
        crosshair.Freeze();
        dc.DrawLine(crosshair, new Point(x, scale.Top), new Point(x, scale.Top + scale.Height));

        var cpu = ShowLoad ? nearest.CpuLoad : nearest.CpuTemp;
        var gpu = ShowLoad ? nearest.GpuLoad : nearest.GpuTemp;
        if (!double.IsNaN(cpu))
        {
            DrawMarker(dc, new Point(x, scale.Y(cpu)), CpuBrush);
        }

        if (!double.IsNaN(gpu))
        {
            DrawMarker(dc, new Point(x, scale.Y(gpu)), GpuBrush);
        }

        var time = DateTime.UnixEpoch.AddMinutes(nearest.Minute).ToLocalTime();
        string Describe(double value, double max) => double.IsNaN(value)
            ? Units.Missing
            : ShowLoad ? Units.Percent(value) : $"{Units.Temperature(value)}  (макс {max:0}°)";

        FormattedText[] lines =
        [
            Label(time.ToString(RangeMinutes > 1440 ? "dd.MM HH:mm" : "HH:mm", CultureInfo.CurrentCulture), TextBrush),
            Label("CPU  " + Describe(cpu, nearest.CpuTempMax), CpuBrush, 12.5),
            Label("GPU  " + Describe(gpu, nearest.GpuTempMax), GpuBrush, 12.5),
        ];

        var width = lines.Max(l => l.Width) + 22;
        var height = lines.Sum(l => l.Height) + 16;
        var left = x + 12 + width > scale.Left + scale.Width ? x - 12 - width : x + 12;
        var box = new Rect(left, scale.Top + 8, width, height);
        var border = new Pen(GridBrush, 1);
        border.Freeze();
        dc.DrawRoundedRectangle(TooltipBrush, border, box, 8, 8);

        var y = box.Y + 8;
        foreach (var line in lines)
        {
            dc.DrawText(line, new Point(box.X + 11, y));
            y += line.Height;
        }
    }

    private static void DrawMarker(DrawingContext dc, Point at, Brush brush)
    {
        dc.DrawEllipse(brush, null, at, 5, 5);
        dc.DrawEllipse(Brushes.White, null, at, 2, 2);
    }

    private FormattedText Label(string text, Brush brush, double size = 11) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Агрегированная точка графика (несколько минут в одном столбце).</summary>
    private readonly record struct Bucket(
        double Minute, double CpuTemp, double CpuTempMax, double GpuTemp, double GpuTempMax, double CpuLoad, double GpuLoad);

    private struct BucketAccumulator
    {
        private double _minuteSum;
        private double _cpuTempSum;
        private double _gpuTempSum;
        private double _cpuLoadSum;
        private double _gpuLoadSum;
        private double _cpuTempMax;
        private double _gpuTempMax;
        private int _cpuCount;
        private int _gpuCount;
        private int _count;

        public void Add(HistoryPoint p)
        {
            if (p.HasCpuTemp)
            {
                _cpuTempSum += p.CpuTemp;
                _cpuTempMax = _cpuCount++ == 0 ? p.CpuTempMax : Math.Max(_cpuTempMax, p.CpuTempMax);
            }

            if (p.HasGpuTemp)
            {
                _gpuTempSum += p.GpuTemp;
                _gpuTempMax = _gpuCount++ == 0 ? p.GpuTempMax : Math.Max(_gpuTempMax, p.GpuTempMax);
            }

            _cpuLoadSum += p.CpuLoad;
            _gpuLoadSum += p.GpuLoad;
            _minuteSum += p.Minute;
            _count++;
        }

        public readonly Bucket Build() => new(
            _minuteSum / _count,
            _cpuCount > 0 ? _cpuTempSum / _cpuCount : double.NaN,
            _cpuCount > 0 ? _cpuTempMax : double.NaN,
            _gpuCount > 0 ? _gpuTempSum / _gpuCount : double.NaN,
            _gpuCount > 0 ? _gpuTempMax : double.NaN,
            _cpuLoadSum / _count,
            _gpuLoadSum / _count);
    }

    /// <summary>Преобразование «минута/значение» ↔ пиксели для текущего размера и периода.</summary>
    private sealed class Scale(HistoryChart chart, long fromMinute)
    {
        public long FromMinute { get; } = fromMinute;

        public double Left => PadLeft;

        public double Top => PadTop;

        public double Width { get; } = chart.ActualWidth - PadLeft - PadRight;

        public double Height { get; } = chart.ActualHeight - PadTop - PadBottom;

        public double Min { get; private set; }

        public double Max { get; private set; } = 100;

        public double X(double minute) => Left + ((minute - FromMinute) / chart.RangeMinutes * Width);

        public double Y(double value) => Top + Height - ((Math.Clamp(value, Min, Max) - Min) / (Max - Min) * Height);

        /// <summary>Нагрузка — всегда 0–100%. Температура — по данным с шагом 10° и обязательно с порогом перегрева.</summary>
        public void FitValues(List<Bucket> buckets, bool load, double overheat)
        {
            if (load)
            {
                (Min, Max) = (0, 100);
                return;
            }

            var low = buckets.SelectMany(b => new[] { b.CpuTemp, b.GpuTemp }).Where(v => !double.IsNaN(v)).DefaultIfEmpty(30).Min();
            var high = buckets.SelectMany(b => new[] { b.CpuTempMax, b.GpuTempMax }).Where(v => !double.IsNaN(v)).DefaultIfEmpty(80).Max();
            Max = Math.Min(120, Math.Ceiling((Math.Max(high, overheat) + 5) / 10) * 10);
            Min = Math.Max(0, Math.Floor((low - 5) / 10) * 10);
            if (Max - Min < 40)
            {
                Min = Math.Max(0, Max - 40);
            }
        }
    }
}
