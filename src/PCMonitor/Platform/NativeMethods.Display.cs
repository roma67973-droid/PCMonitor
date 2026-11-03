using System.Runtime.InteropServices;

namespace PCMonitor.Platform;

// WinAPI для вкладки «Экран»: DDC/CI (dxva2), гамма-таблица (gdi32), сведения о мониторах (user32).
internal static unsafe partial class NativeMethods
{
    public const int GammaRampLength = 256;

    private const uint MonitorDefaultToNearest = 2;
    private const uint EddGetDeviceInterfaceName = 1;

    /// <summary>HMONITOR экрана, на который попадает точка (в физических пикселях).</summary>
    public static IntPtr MonitorFromPoint(int x, int y) => MonitorFromPointNative(new POINT { X = x, Y = y }, MonitorDefaultToNearest);

    /// <summary>Физические мониторы за одним HMONITOR (обычно один, несколько — в режиме дублирования).</summary>
    public static IReadOnlyList<(IntPtr Handle, string Description)> GetPhysicalMonitors(IntPtr hmonitor)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmonitor, out var count) || count == 0)
        {
            return [];
        }

        var buffer = new PHYSICAL_MONITOR[count];
        fixed (PHYSICAL_MONITOR* p = buffer)
        {
            if (!GetPhysicalMonitorsFromHMONITOR(hmonitor, count, p))
            {
                return [];
            }

            var result = new List<(IntPtr, string)>((int)count);
            for (var i = 0; i < count; i++)
            {
                result.Add((p[i].hPhysicalMonitor, new string(p[i].szPhysicalMonitorDescription)));
            }

            return result;
        }
    }

    /// <summary>Путь интерфейса монитора вида <c>\\?\DISPLAY#MSI3CA9#5&amp;...#{guid}</c> для экрана <c>\\.\DISPLAY1</c>.</summary>
    public static string? GetMonitorInterfaceName(string screenDeviceName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        return EnumDisplayDevicesW(screenDeviceName, 0, ref device, EddGetDeviceInterfaceName)
            ? new string(device.DeviceID)
            : null;
    }

    /// <summary>Текущая гамма-таблица экрана (768 значений) или <c>null</c>, если драйвер её не отдал.</summary>
    public static ushort[]? GetGammaRamp(string screenDeviceName)
    {
        var ramp = new ushort[GammaRampLength * 3];
        return WithScreenDc(screenDeviceName, dc =>
        {
            fixed (ushort* p = ramp)
            {
                return GetDeviceGammaRamp(dc, p);
            }
        }) ? ramp : null;
    }

    /// <summary>Записывает гамма-таблицу экрана: по 256 значений на красный, зелёный и синий.</summary>
    public static bool SetGammaRamp(string screenDeviceName, ushort[] ramp)
    {
        if (ramp.Length != GammaRampLength * 3)
        {
            throw new ArgumentException("Нужно 768 значений", nameof(ramp));
        }

        return WithScreenDc(screenDeviceName, dc =>
        {
            fixed (ushort* p = ramp)
            {
                return SetDeviceGammaRamp(dc, p);
            }
        });
    }

    private static bool WithScreenDc(string screenDeviceName, Func<IntPtr, bool> action)
    {
        var dc = CreateDCW(screenDeviceName, null, null, IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return action(dc);
        }
        finally
        {
            _ = DeleteDC(dc);
        }
    }

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMonitorBrightness(IntPtr monitor, uint value);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorContrast(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMonitorContrast(IntPtr monitor, uint value);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyPhysicalMonitor(IntPtr monitor);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hmonitor, out uint count);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicalMonitorsFromHMONITOR(IntPtr hmonitor, uint count, PHYSICAL_MONITOR* monitors);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    private static partial IntPtr MonitorFromPointNative(POINT point, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayDevicesW(string device, uint index, ref DISPLAY_DEVICEW info, uint flags);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateDCW(string driver, string? device, string? output, IntPtr initData);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDeviceGammaRamp(IntPtr dc, ushort* ramp);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDeviceGammaRamp(IntPtr dc, ushort* ramp);

    // Имена полей повторяют Win32 API.
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        public fixed char szPhysicalMonitorDescription[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAY_DEVICEW
    {
        public uint cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }
}
