using System.Globalization;

namespace PCMonitor.Hardware;

/// <summary>Правила интерпретации сырых данных LibreHardwareMonitor. Отдельно от опроса — чтобы покрываться тестами.</summary>
public static class SensorRules
{
    /// <summary>
    /// Слова в имени «температурного» датчика, которые означают паспортный порог, а не измерение.
    /// Например, NVMe отдаёт «Warning Temperature 69» и «Critical Temperature 74» — это константы.
    /// </summary>
    private static readonly string[] ThresholdMarkers = ["Warning", "Critical", "Threshold", "Limit", "Max Operating"];

    /// <summary>Отключённые термодиоды отдают 0 или 255 — это не температура.</summary>
    public static bool IsPlausibleTemperature(float celsius) => celsius is > 0 and < 150;

    /// <summary>Датчик измеряет температуру (а не хранит порог) и его значение правдоподобно.</summary>
    public static bool IsTemperatureReading(string sensorName, float? value)
    {
        ArgumentNullException.ThrowIfNull(sensorName);

        return value is { } v
            && IsPlausibleTemperature(v)
            && !ThresholdMarkers.Any(m => sensorName.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Имя накопителя для интерфейса. Некоторые NVMe отдают модель из нулевых байт или пробелов —
    /// тогда показываем тип и объём: «NVMe-накопитель 1 ТБ».
    /// </summary>
    public static string StorageDisplayName(string? rawName, string identifier, float? totalSpaceGb, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var cleaned = new string((rawName ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > 0)
        {
            return cleaned;
        }

        var kind = identifier.Contains("/nvme/", StringComparison.OrdinalIgnoreCase) ? "NVMe-накопитель" : "Накопитель";
        return totalSpaceGb is > 0 and var gb ? $"{kind} {FormatCapacity(gb)}" : $"{kind} {ordinal}";
    }

    /// <summary>Объём «по-магазинному»: 1024 ГБ → «1 ТБ», 238 ГБ → «238 ГБ».</summary>
    private static string FormatCapacity(float gigabytes) => gigabytes >= 1000
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} ТБ", gigabytes / 1024)
        : string.Format(CultureInfo.CurrentCulture, "{0:0} ГБ", gigabytes);
}
