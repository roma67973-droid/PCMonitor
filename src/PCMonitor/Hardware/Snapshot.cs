namespace PCMonitor.Hardware;

/// <summary>Где физически находится кулер. Определяет, кто им управляет в режиме «Авто».</summary>
public enum FanLocation
{
    Motherboard,
    Gpu,
    Cooler,
}

/// <summary>Группа датчика температуры (для подписи и выбора источника по умолчанию).</summary>
public enum SensorGroup
{
    Cpu,
    Gpu,
    Motherboard,
    Cooler,
    Storage,
}

public sealed record DiskReading(string Name, float? Temperature, float? UsedPercent, float? ActivityPercent);

public sealed record TemperatureReading(string Id, string Label, float Value, SensorGroup Group);

public sealed record FanReading(
    string Key,
    string Name,
    string Hardware,
    FanLocation Location,
    float? Rpm,
    float? Percent,
    bool IsControllable);

/// <summary>Состояние системы на момент одного опроса датчиков. Неизменяемо после публикации.</summary>
public sealed class Snapshot
{
    public string CpuName { get; init; } = string.Empty;

    public float CpuLoad { get; init; }

    public float? CpuTemp { get; init; }

    public float? CpuClockMhz { get; init; }

    public float? CpuPower { get; init; }

    public IReadOnlyList<float> CoreLoads { get; init; } = [];

    public bool HasGpu { get; init; }

    public string GpuName { get; init; } = string.Empty;

    public float GpuLoad { get; init; }

    public float? GpuTemp { get; init; }

    public float? GpuHotSpot { get; init; }

    public float? GpuClockMhz { get; init; }

    public float? GpuMemoryClockMhz { get; init; }

    public float? GpuPower { get; init; }

    public float? GpuFanRpm { get; init; }

    public float? VramUsedMb { get; init; }

    public float? VramTotalMb { get; init; }

    public double RamUsedGb { get; init; }

    public double RamTotalGb { get; init; }

    public float RamLoad { get; init; }

    public double NetDownBytesPerSec { get; init; }

    public double NetUpBytesPerSec { get; init; }

    /// <summary>Пусто, если опрос был облегчённым (окно скрыто).</summary>
    public IReadOnlyList<DiskReading> Disks { get; init; } = [];

    public IReadOnlyList<FanReading> Fans { get; init; } = [];

    public IReadOnlyList<TemperatureReading> Temperatures { get; init; } = [];

    /// <summary>Видны ли датчики материнской платы (без драйвера PawnIO их нет).</summary>
    public bool HasMotherboardSensors { get; init; }
}
