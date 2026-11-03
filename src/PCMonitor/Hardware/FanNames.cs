namespace PCMonitor.Hardware;

/// <summary>Человеческие русские имена вместо «System Fan #2» и прочих имён из драйверов.</summary>
public static class FanNames
{
    private static readonly (string Prefix, string Name)[] Prefixes =
    [
        ("GPU Fan", "Кулер видеокарты"),
        ("CPU Optional", "Доп. кулер процессора"),
        ("CPU Fan", "Кулер процессора"),
        ("System Fan", "Корпусной"),
        ("Chassis Fan", "Корпусной"),
        ("Pump", "Помпа"),
        ("Fan", "Вентилятор"),
    ];

    public static string Humanize(string driverName)
    {
        ArgumentNullException.ThrowIfNull(driverName);

        if (driverName == "CPU")
        {
            return "Кулер процессора";
        }

        foreach (var (prefix, name) in Prefixes)
        {
            if (driverName.StartsWith(prefix, StringComparison.Ordinal))
            {
                // «CPU Optional Fan» целиком заменяется, у остальных сохраняем номер: «System Fan #2» → «Корпусной #2».
                var suffix = prefix == "CPU Optional" ? string.Empty : driverName[prefix.Length..].TrimEnd();
                return name + suffix;
            }
        }

        return driverName;
    }
}
