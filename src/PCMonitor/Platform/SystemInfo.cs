using System.Security.Principal;
using Microsoft.Win32;

namespace PCMonitor.Platform;

/// <summary>Окружение, от которого зависит доступность датчиков.</summary>
public static class SystemInfo
{
    /// <summary>Без прав администратора не читаются MSR процессора и Super I/O материнской платы.</summary>
    public static bool IsAdministrator { get; } = CheckAdministrator();

    /// <summary>Драйвер PawnIO даёт доступ к датчикам материнской платы и температуре CPU.</summary>
    public static bool IsPawnIoInstalled { get; } = CheckPawnIo();

    public static Uri PawnIoDownloadPage { get; } = new("https://pawnio.eu/");

    private static bool CheckAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool CheckPawnIo()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }
}
