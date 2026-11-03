namespace PCMonitor.Core;

/// <summary>Насколько тревожно значение. Определяет цвет в интерфейсе и на значке в трее.</summary>
public enum Severity
{
    Normal,
    Elevated,
    Critical,
}

/// <summary>Пороговые значения температуры для одного типа устройства.</summary>
public readonly record struct Thresholds(double Warm, double Hot)
{
    public Severity Classify(double? value)
    {
        if (value is not { } v)
        {
            return Severity.Normal;
        }

        if (v >= Hot)
        {
            return Severity.Critical;
        }

        return v >= Warm ? Severity.Elevated : Severity.Normal;
    }
}

/// <summary>Единое место для всех температурных порогов программы.</summary>
public static class Thermals
{
    public static readonly Thresholds Cpu = new(Warm: 75, Hot: 85);

    public static readonly Thresholds Gpu = new(Warm: 75, Hot: 85);

    public static readonly Thresholds Disk = new(Warm: 50, Hot: 60);

    /// <summary>Аварийный порог: все управляемые кулеры принудительно на 100%.</summary>
    public const double EmergencyCpu = 90;

    /// <inheritdoc cref="EmergencyCpu"/>
    public const double EmergencyGpu = 87;

    /// <summary>Заполнение ОЗУ, при котором статус системы становится «повышенным».</summary>
    public const double RamPressurePercent = 92;

    /// <summary>Температура CPU, начиная с которой минута считается «в перегреве» в истории.</summary>
    public static double CpuOverheat => Cpu.Hot;
}
