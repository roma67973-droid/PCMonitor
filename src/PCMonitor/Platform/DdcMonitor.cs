using System.IO;
using System.Text;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PCMonitor.Platform;

/// <summary>
/// Внешний монитор, управляемый по DDC/CI: настоящая яркость подсветки и контраст.
/// DDC/CI идёт по шине I2C кабеля и на многих мониторах отвечает через раз с ошибкой
/// контрольной суммы, поэтому каждая команда повторяется. Вызовы медленные (десятки мс) —
/// только из фонового потока и по одному за раз.
/// </summary>
public sealed class DdcMonitor : IDisposable
{
    private const int Attempts = 5;
    private const int RetryDelayMs = 50;

    // Дескриптор 0 — нормальное значение (Windows нумерует физические мониторы с нуля), поэтому закрытие — отдельным флагом.
    private readonly IntPtr _handle;
    private volatile bool _disposed;

    private DdcMonitor(IntPtr handle, string name, string screenDeviceName)
    {
        _handle = handle;
        Name = name;
        ScreenDeviceName = screenDeviceName;
    }

    /// <summary>Модель монитора из EDID (например, «Optix G27C2»), иначе описание от Windows.</summary>
    public string Name { get; }

    /// <summary>Экран Windows, на котором показывается монитор (<c>\\.\DISPLAY1</c>).</summary>
    public string ScreenDeviceName { get; }

    /// <summary>Все физические мониторы всех экранов. Неподдерживающие DDC/CI тоже попадают в список.</summary>
    public static List<DdcMonitor> EnumerateAll()
    {
        var result = new List<DdcMonitor>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var b = screen.Bounds;
            var hmonitor = NativeMethods.MonitorFromPoint(b.X + (b.Width / 2), b.Y + (b.Height / 2));
            var edidName = ReadEdidName(screen.DeviceName);
            foreach (var (handle, description) in NativeMethods.GetPhysicalMonitors(hmonitor))
            {
                result.Add(new DdcMonitor(handle, edidName ?? description, screen.DeviceName));
            }
        }

        return result;
    }

    public bool TryGetBrightness(out int percent) => TryGet(NativeMethods.GetMonitorBrightness, out percent);

    public bool TrySetBrightness(int percent) => TrySet(NativeMethods.GetMonitorBrightness, NativeMethods.SetMonitorBrightness, percent);

    public bool TryGetContrast(out int percent) => TryGet(NativeMethods.GetMonitorContrast, out percent);

    public bool TrySetContrast(int percent) => TrySet(NativeMethods.GetMonitorContrast, NativeMethods.SetMonitorContrast, percent);

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ = NativeMethods.DestroyPhysicalMonitor(_handle);
        }
    }

    private delegate bool RangeGetter(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    private bool TryGet(RangeGetter get, out int percent)
    {
        percent = 0;
        if (!TryRange(get, out var min, out var cur, out var max))
        {
            return false;
        }

        // Кривая прошивка может вернуть значение вне своего же диапазона.
        percent = (int)Math.Clamp(Math.Round(((long)cur - min) * 100.0 / (max - min)), 0, 100);
        return true;
    }

    private bool TrySet(RangeGetter get, Func<IntPtr, uint, bool> set, int percent)
    {
        if (!TryRange(get, out var min, out _, out var max))
        {
            return false;
        }

        var value = min + (uint)Math.Round((max - min) * Math.Clamp(percent, 0, 100) / 100.0);
        return Retry(() => !_disposed && set(_handle, value));
    }

    private bool TryRange(RangeGetter get, out uint min, out uint cur, out uint max)
    {
        uint a = 0, b = 0, c = 0;
        var ok = Retry(() => !_disposed && get(_handle, out a, out b, out c) && c > a);
        (min, cur, max) = (a, b, c);
        return ok;
    }

    private static bool Retry(Func<bool> action)
    {
        for (var i = 0; i < Attempts; i++)
        {
            if (action())
            {
                return true;
            }

            if (i < Attempts - 1)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }

        return false;
    }

    /// <summary>Имя модели из EDID в реестре: Enum\DISPLAY\&lt;код&gt;\&lt;экземпляр&gt;\Device Parameters\EDID.</summary>
    private static string? ReadEdidName(string screenDeviceName)
    {
        // \\?\DISPLAY#MSI3CA9#5&2c9e1b0&0&UID4352#{e6f07b5f-...}
        var parts = NativeMethods.GetMonitorInterfaceName(screenDeviceName)?.Split('#');
        if (parts is not { Length: >= 3 })
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            return key?.GetValue("EDID") is byte[] edid ? ParseEdidName(edid) : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Ищет в четырёх 18-байтных дескрипторах EDID дескриптор имени (тег 0xFC).</summary>
    internal static string? ParseEdidName(byte[] edid)
    {
        for (var offset = 54; offset + 18 <= Math.Min(edid.Length, 126); offset += 18)
        {
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
            {
                var name = Encoding.ASCII.GetString(edid, offset + 5, 13).Split('\n')[0].Trim();
                return name.Length > 0 ? name : null;
            }
        }

        return null;
    }
}
