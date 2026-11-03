namespace PCMonitor.Core;

public enum FanMode
{
    /// <summary>Скоростью управляет BIOS или драйвер видеокарты.</summary>
    Auto,

    /// <summary>Постоянная скорость в процентах.</summary>
    Fixed,

    /// <summary>Скорость зависит от температуры выбранного датчика.</summary>
    Curve,
}

/// <summary>Точка кривой: температура (°C) → мощность кулера (%).</summary>
public sealed class CurvePoint
{
    public CurvePoint()
    {
    }

    public CurvePoint(double temperature, double percent)
    {
        T = temperature;
        P = percent;
    }

    // Короткие имена сохранены ради совместимости с уже записанным settings.json.
    public double T { get; set; }

    public double P { get; set; }

    public CurvePoint Clone() => new(T, P);
}

/// <summary>Пользовательская настройка одного управляемого кулера.</summary>
public sealed class FanConfig
{
    public FanMode Mode { get; set; } = FanMode.Auto;

    public double Fixed { get; set; } = 50;

    /// <summary>Идентификатор датчика температуры для режима <see cref="FanMode.Curve"/>.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// Точки кривой, отсортированные по температуре. Редактор меняет значения точек на месте,
    /// а поток датчиков читает их по индексу — структура списка при этом не меняется, только заменяется целиком.
    /// </summary>
    public List<CurvePoint> Curve { get; set; } = FanCurve.Default();
}

public static class FanCurve
{
    public static List<CurvePoint> Default() => [new(30, 25), new(50, 40), new(70, 70), new(85, 100)];

    public static List<CurvePoint> Quiet() => [new(35, 20), new(55, 30), new(72, 55), new(88, 100)];

    /// <summary>
    /// Мощность кулера для температуры: линейная интерполяция между точками,
    /// левее первой и правее последней точки — значение крайней точки.
    /// Пустая кривая трактуется как «на всякий случай 100%».
    /// </summary>
    public static double Evaluate(IReadOnlyList<CurvePoint> curve, double temperature)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (curve.Count == 0)
        {
            return 100;
        }

        if (temperature <= curve[0].T)
        {
            return curve[0].P;
        }

        for (var i = 1; i < curve.Count; i++)
        {
            var left = curve[i - 1];
            var right = curve[i];
            if (temperature <= right.T)
            {
                var span = right.T - left.T;
                return span <= 0 ? right.P : left.P + ((right.P - left.P) * (temperature - left.T) / span);
            }
        }

        return curve[^1].P;
    }
}
