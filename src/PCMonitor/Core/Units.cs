using System.Globalization;

namespace PCMonitor.Core;

/// <summary>Форматирование величин для интерфейса. Единый формат во всех местах программы.</summary>
public static class Units
{
    public const string Missing = "—";

    private const double KiB = 1024;
    private const double MiB = 1024 * 1024;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Temperature(double? celsius) =>
        celsius is { } t ? string.Format(Culture, "{0:0}°C", t) : Missing;

    public static string Percent(double? value) =>
        value is { } v ? string.Format(Culture, "{0:0}%", v) : Missing;

    public static string Rpm(double? rpm) =>
        rpm is { } r ? string.Format(Culture, "{0:0} об/мин", r) : Missing;

    public static string Watts(double? watts) =>
        watts is { } w ? string.Format(Culture, "{0:0} Вт", w) : Missing;

    public static string Megahertz(double? mhz) =>
        mhz is { } f ? string.Format(Culture, "{0:0} МГц", f) : Missing;

    public static string Gigahertz(double? mhz) =>
        mhz is { } f ? string.Format(Culture, "{0:0.00} ГГц", f / 1000) : Missing;

    public static string TransferRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= MiB => string.Format(Culture, "{0:0.0} МБ/с", bytesPerSecond / MiB),
        >= KiB => string.Format(Culture, "{0:0} КБ/с", bytesPerSecond / KiB),
        _ => string.Format(Culture, "{0:0} Б/с", bytesPerSecond),
    };

    /// <summary>«45 мин», «2 ч 5 мин».</summary>
    public static string Minutes(int minutes) => minutes >= 60
        ? string.Format(Culture, "{0} ч {1} мин", minutes / 60, minutes % 60)
        : string.Format(Culture, "{0} мин", minutes);

    /// <summary>«3 ч 12 мин» или «2 д 4 ч» для длинных интервалов.</summary>
    public static string Uptime(TimeSpan span) => span.TotalDays >= 1
        ? string.Format(Culture, "{0} д {1} ч", (int)span.TotalDays, span.Hours)
        : string.Format(Culture, "{0} ч {1} мин", span.Hours, span.Minutes);
}
